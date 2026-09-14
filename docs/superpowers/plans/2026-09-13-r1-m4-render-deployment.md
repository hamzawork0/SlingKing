# SlingKing R1 · M4 Render Deployment — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put SlingKing on Render's free tier so the client can open it, without it looking broken when it has been idle.

**Architecture:** Two Render services — a Docker web service for the API and a static site for the frontend — declared in a `render.yaml` Blueprint, with secrets held only in Render's dashboard. The database stays on Supabase. An external free cron pings `/api/v1/health` every ten minutes between 19:00 and 02:00 IST, which keeps Render awake and Supabase unpaused with one request. **The application changes in exactly one place: `UseForwardedHeaders`.** Everything else is configuration.

**Tech Stack:** .NET 8.0.408 (`net8.0`), ASP.NET Core; React 19.2.8, TypeScript 5.9.3, Vite 8.2.2; Docker (multi-stage); Render Blueprint (`render.yaml`); Supabase Postgres.

**Spec:** `docs/superpowers/specs/2026-09-13-r1-m4-render-deployment-design.md` — normative. Read it alongside this plan.

---

## Global Constraints

These apply to every task. Do not restate them per task; do not violate them in any task.

- **Everything must be free.** No paid tier, no paid tooling, no licences. This is a standing project constraint, not a preference for this milestone.
- **Target framework is `net8.0` everywhere.** `global.json` pins the SDK to 8.0.408.
- **No secrets in source, ever.** All configuration comes from environment variables. **Never print, echo, or `cat` `backend/.env`.** If a value is needed, read it into a shell variable.
- **`backend/.env` must never enter a Docker build context.** A secret that cannot be copied cannot be layered into an image.
- **Integration tests run only against the `test` schema.** `TestSchemaFixture.AssertTargetsTestSchema` refuses any search path but exactly `test`, and that guard is the only thing between a test run and the owner's real stock records. Never weaken, bypass or temporarily disable it.
- **The Supabase session pooler caps this project at 15 client connections.** `SlingKingApiFactory` bounds itself to 5. Do not add parallelism and do not raise `MaxPoolSize` anywhere.
- **Never use the direct connection host (`db.<ref>.supabase.co`)** — it is IPv6-only and unreachable from this machine.
- **This milestone adds no migration and no database change.** The table count stays at 28.
- **`SlingKing.Application` must not reference any database-provider package** — only `Microsoft.EntityFrameworkCore.Relational` plus the Domain project.
- **Dependency direction is `Api → Application → Domain`, and `Infrastructure → Application → Domain`.** `Application` never references `Infrastructure`.
- **Only one application file changes in this milestone:** `backend/src/SlingKing.Api/Program.cs`. If a task finds itself editing anything else under `backend/src/`, stop and raise it.
- Build clean under `-warnaserror`; `tsc -b` clean; lint adds no new warnings beyond the 4 pre-existing ones in `AuthContext.tsx` (×2), `StockItemPage.tsx` and `ProductForm.tsx`.
- **Commit at the end of every task**, with the message given in that task's final step.

---

## File Structure

```
backend/
├─ Dockerfile                    two-stage build, non-root, $PORT entrypoint
├─ .dockerignore                 excludes bin/, obj/, .env, backups/
└─ src/SlingKing.Api/
   └─ Program.cs                 + ForwardedHeaders options and middleware

backend/tests/SlingKing.IntegrationTests/Api/
└─ ForwardedHeadersTests.cs      proves the rate limiter partitions per client

render.yaml                      both services, health check, SPA rewrite
README.md                        deployment section: variables and the release ritual
```

Everything else in the repository is untouched.

---

## Task 1: Trust the proxy's forwarded headers

The only application change in this milestone, and the one M1 predicted:

> *proxy's IP and every visitor shares one bucket — add `UseForwardedHeaders` when this deploys behind one, or the limit becomes a self-inflicted outage.*

**Files:**
- Modify: `backend/src/SlingKing.Api/Program.cs`
- Test: `backend/tests/SlingKing.IntegrationTests/Api/ForwardedHeadersTests.cs`

**Interfaces:**
- Consumes: `SlingKingApiFactory` (existing test fixture).
- Produces: no new application surface. Later tasks depend only on the behaviour.

**Why the default configuration silently does nothing.** ASP.NET Core's forwarded-headers middleware only trusts proxies on loopback addresses by default. Render's proxy is not loopback, so with default options the headers are **ignored without any error** — the middleware appears installed and changes nothing. `KnownNetworks` and `KnownProxies` must both be cleared. This is the single most likely way for this task to look done and not be.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/SlingKing.IntegrationTests/Api/ForwardedHeadersTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Shouldly;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Api;

/// <summary>
/// Behind a proxy every request arrives carrying the proxy's address, so a rate limiter
/// partitioned on the connection IP puts every visitor in one shared bucket — one person
/// mistyping a password locks everybody else out. These tests prove the limiter sees the
/// forwarded client address instead.
/// </summary>
[Collection("database")]
public class ForwardedHeadersTests : IClassFixture<ForwardedHeadersTests.ThrottledApiFactory>
{
    private readonly ThrottledApiFactory _factory;

    public ForwardedHeadersTests(ThrottledApiFactory factory) => _factory = factory;

    /// <summary>
    /// The real factory sets the login limit to 1000 so the suite cannot trip it. This one
    /// sets 2, which is the whole point of the test.
    /// </summary>
    /// <remarks>
    /// Setting the variable process-wide is safe here even though environment variables are
    /// global: Program.cs reads it once while the host is being built, so each factory's host
    /// captures whatever was set at ITS build time. Nothing re-reads it later.
    /// </remarks>
    public class ThrottledApiFactory : SlingKingApiFactory
    {
        public const int AttemptsPerMinute = 2;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            Environment.SetEnvironmentVariable(
                "SLINGKING_LOGIN_ATTEMPTS_PER_MINUTE", AttemptsPerMinute.ToString());
        }
    }

    private record LoginRequest(string Email, string Password);

    private static HttpRequestMessage LoginFrom(string clientIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("nobody@slingking.test", "definitely-wrong"))
        };

        request.Headers.Add("X-Forwarded-For", clientIp);
        request.Headers.Add("X-Forwarded-Proto", "https");

        return request;
    }

    [Fact]
    public async Task One_client_exhausting_the_limit_does_not_lock_out_another()
    {
        using var client = _factory.CreateClient();

        // Spend the first address's whole allowance, then one more to be certain it is spent.
        for (var attempt = 0; attempt <= ThrottledApiFactory.AttemptsPerMinute; attempt++)
        {
            await client.SendAsync(LoginFrom("203.0.113.10"));
        }

        var exhausted = await client.SendAsync(LoginFrom("203.0.113.10"));
        exhausted.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // A DIFFERENT client, first attempt. Without the forwarded headers both addresses
        // collapse to the proxy's, share one bucket, and this comes back 429 — someone else's
        // typing locking this person out.
        var innocent = await client.SendAsync(LoginFrom("198.51.100.20"));

        innocent.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        innocent.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_forwarded_scheme_is_honoured()
    {
        // Render terminates TLS and forwards over http. Without X-Forwarded-Proto being
        // trusted, anything that reads Request.Scheme believes the site is plain http.
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/v1/health")
            {
                Headers = { { "X-Forwarded-Proto", "https" }, { "X-Forwarded-For", "203.0.113.30" } }
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd backend; dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~ForwardedHeadersTests`

Expected: `One_client_exhausting_the_limit_does_not_lock_out_another` FAILS — the second address comes back `TooManyRequests`, because without the middleware both addresses are the same connection IP and share one bucket. That failure is the bug this task fixes; do not proceed until you have seen it.

- [ ] **Step 3: Configure the options**

In `backend/src/SlingKing.Api/Program.cs`, in the services section (before `builder.Build()` at line 170), add:

```csharp
// Render terminates TLS and proxies to this container, so every request arrives carrying the
// proxy's address. The rate limiter partitions on the client IP, so without this the whole
// internet shares one bucket of login attempts and one person's typos lock everyone else out.
//
// Clearing KnownNetworks and KnownProxies is load-bearing: by default the middleware trusts
// only loopback proxies, so behind Render it would ignore these headers and change NOTHING —
// installed, running, and silently useless. We cannot know the proxy's address in advance,
// and only Render can reach this container, so trusting the hop it adds is the available
// option.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
```

Add the using directives at the top of the file:

```csharp
using Microsoft.AspNetCore.HttpOverrides;
```

- [ ] **Step 4: Add the middleware, first in the pipeline**

Immediately after `var app = builder.Build();` (line 170) and **before** `app.UseExceptionHandler();`, add:

```csharp
// First in the pipeline on purpose: everything downstream that reads the client's address or
// the request scheme — the rate limiter, the request log — must see the real values rather
// than the proxy's.
app.UseForwardedHeaders();
```

Order matters. Placed after `UseRateLimiter`, the limiter would still partition on the proxy's address and the test would still fail. Placed after `UseSerilogRequestLogging`, the logs would record the proxy for every request.

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd backend; dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~ForwardedHeadersTests`

Expected: 2 passed.

- [ ] **Step 6: Prove the cleared proxy lists are load-bearing**

This is the step that separates a working fix from one that only looks installed.

Temporarily remove the two clearing lines:

```csharp
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
```

Re-run the filter. Expected: `One_client_exhausting_the_limit_does_not_lock_out_another` FAILS again, exactly as it did at Step 2 — because the middleware now runs but refuses to trust a non-loopback proxy, so the headers are ignored.

**Then restore both lines and re-run — 2 passed.** Record the failure output in your report. A configuration whose most important two lines have never been seen to matter is a configuration nobody can safely edit later.

- [ ] **Step 7: Run the whole backend suite**

Run: `cd backend; dotnet build -warnaserror; if ($?) { dotnet test }`

Expected: everything green, zero warnings. Expect **155 integration** (153 plus your 2) and **87 unit**.

- [ ] **Step 8: Commit**

```bash
git add backend/src/SlingKing.Api/Program.cs backend/tests/SlingKing.IntegrationTests/Api/ForwardedHeadersTests.cs
git commit -m "feat: trust the proxy's forwarded headers

M1 left a comment predicting this: behind a proxy every request carries the
proxy's address, so the login rate limiter puts every visitor in one shared
bucket and one person's typos lock everybody out.

KnownNetworks and KnownProxies must be cleared or the middleware trusts only
loopback proxies and silently ignores the headers. Proved by removing those two
lines and watching the test fail exactly as it did before the fix.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: The container

**Files:**
- Create: `backend/Dockerfile`
- Create: `backend/.dockerignore`

**Interfaces:**
- Consumes: nothing.
- Produces: an image whose entrypoint binds `$PORT`. Task 3's Blueprint references `backend/Dockerfile` with `backend` as the build context.

**Docker Desktop must be running.** The CLI is installed on this machine but the daemon is not started by default — `docker version` failing with a connection error means start Docker Desktop, not that Docker is missing.

- [ ] **Step 1: Write the build-context exclusions first**

Create `backend/.dockerignore`. **Write this before the Dockerfile**, so no build ever runs with a context that could contain a secret:

```
# Secrets. This entry must exist before any build does.
.env
**/.env

# Build output — copying it in would poison the restore
**/bin/
**/obj/

# Database dumps
../backups/

# Test scratch
**/TestResults/
**/logs/

# Not part of a backend build
../frontend/
../docs/
../.superpowers/
../.git/
```

- [ ] **Step 2: Write the Dockerfile**

Create `backend/Dockerfile`:

```dockerfile
# Build stage: the SDK image restores and publishes, and is then thrown away.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project files first so a source-only change does not invalidate the restore layer.
COPY SlingKing.sln Directory.Build.props ./
COPY src/SlingKing.Domain/SlingKing.Domain.csproj             src/SlingKing.Domain/
COPY src/SlingKing.Application/SlingKing.Application.csproj   src/SlingKing.Application/
COPY src/SlingKing.Infrastructure/SlingKing.Infrastructure.csproj src/SlingKing.Infrastructure/
COPY src/SlingKing.Api/SlingKing.Api.csproj                   src/SlingKing.Api/

RUN dotnet restore src/SlingKing.Api/SlingKing.Api.csproj

COPY src/ src/
RUN dotnet publish src/SlingKing.Api/SlingKing.Api.csproj \
    -c Release -o /app/publish --no-restore

# Runtime stage: only the published output ships.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# The aspnet image already provides a non-root 'app' user. Running as root in a container
# reachable from the internet buys nothing.
USER app

COPY --from=build --chown=app:app /app/publish .

# Render assigns a port through $PORT and expects the process to bind it. An ENV cannot expand
# a variable that does not exist at build time, so the entrypoint is shell form. The default
# keeps the image runnable locally with no PORT set.
ENTRYPOINT ["sh", "-c", "dotnet SlingKing.Api.dll --urls http://0.0.0.0:${PORT:-10000}"]
```

Note the `.sln` and `Directory.Build.props` are copied because `Directory.Build.props` sets the target framework for every project — without it the build produces the wrong TFM or fails outright.

- [ ] **Step 3: Build the image**

Run from the repository root:

```
docker build -t slingking-api:local -f backend/Dockerfile backend
```

Expected: a successful build. If it fails on a missing project, the `COPY` lines in Step 2 do not match the actual project paths — check them against `backend/src/` rather than guessing.

- [ ] **Step 4: Prove no secret rode along**

Run:

```
docker run --rm --entrypoint sh slingking-api:local -c "ls -la /app | head -20; find /app -name '*.env' -o -name 'appsettings.Development.json' | head"
```

Expected: the published output, and **no `.env` file of any kind**. `appsettings.Development.json` may appear — it contains only log levels, which is why it is tracked in git, but note whether it is there.

This check exists because `.dockerignore` being correct is an assumption until something verifies it, and an image is much harder to un-publish than a file is to un-commit.

- [ ] **Step 5: Run the container against the test schema**

Load the test connection string into a shell variable **without printing it**, then run the container on a port of your choosing:

```bash
cd "C:/Hamza Projects/SlingKing"
while IFS= read -r line; do case "$line" in SLINGKING_DB_TEST=*) DB="${line#SLINGKING_DB_TEST=}";; esac; done < backend/.env

docker run --rm -p 8080:8080 \
  -e PORT=8080 \
  -e SLINGKING_DB_RUNTIME="$DB" \
  -e SLINGKING_DB_MIGRATION="$DB" \
  -e SLINGKING_JWT_SECRET="local-container-check-secret-not-for-production-use" \
  -e SLINGKING_JWT_ISSUER="http://localhost:8080" \
  -e SLINGKING_JWT_AUDIENCE="http://localhost:8080" \
  -e SLINGKING_CORS_ORIGINS="http://localhost:5173" \
  slingking-api:local
```

**Use `SLINGKING_DB_TEST`, never the runtime or migration values.** The container migrates and seeds on startup; pointing it at the owner's real schema would run migrations against live data outside any deploy gate.

The seed variables are deliberately omitted here — the seeder returns silently without them, which is exactly the behaviour Task 5 has to guard against, and this check does not need a user.

- [ ] **Step 6: Confirm it answers**

From another shell, while the container runs:

```
curl.exe -s -o NUL -w "%{http_code}\n" http://localhost:8080/api/v1/health
```

Expected: `200`. A `503` means the container started but could not reach Supabase — check the connection string, not the Dockerfile. No response at all means the port binding is wrong, which is the failure mode Step 2's entrypoint exists to prevent.

Stop the container with Ctrl-C when done.

- [ ] **Step 7: Commit**

```bash
git add backend/Dockerfile backend/.dockerignore
git commit -m "build: containerise the API for Render

Render has no native .NET runtime, so a Dockerfile is a requirement rather than
a preference. Two stages, non-root runtime, and a shell-form entrypoint because
an ENV cannot expand the \$PORT that Render assigns at run time.

.dockerignore is written first and excludes .env: a secret that never enters the
build context cannot be layered into an image.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: The Blueprint

**Files:**
- Create: `render.yaml` (repository root)

**Interfaces:**
- Consumes: `backend/Dockerfile` (Task 2).
- Produces: the service names `slingking-api` and `slingking`, which determine the URLs `https://slingking-api.onrender.com` and `https://slingking.onrender.com` — Task 5 depends on those names.

**This task writes a file and does not deploy anything.** Creating the services on Render is Task 5.

- [ ] **Step 1: Write the Blueprint**

Create `render.yaml` at the repository root:

```yaml
# Render Blueprint. The services are declared here rather than clicked into the dashboard so
# the configuration is readable, reviewable, and restorable — a deleted service is rebuilt
# from this file instead of from memory.
#
# Secrets are NOT here. Every value marked `sync: false` must be set by hand in Render's
# dashboard, which is what keeps them out of git.
services:
  - type: web
    name: slingking-api
    runtime: docker
    plan: free
    region: singapore          # Render has no India region; Singapore is nearest to Supabase ap-south-1
    dockerfilePath: ./backend/Dockerfile
    dockerContext: ./backend
    healthCheckPath: /api/v1/health
    autoDeploy: false          # Migrations run on startup. A merge must not change the client's live data.
    envVars:
      # --- Hard-required. The app throws on startup without these. ---
      - key: SLINGKING_DB_RUNTIME
        sync: false            # transaction pooler, port 6543, with Maximum Pool Size=5
      - key: SLINGKING_DB_MIGRATION
        sync: false            # session pooler, port 5432
      - key: SLINGKING_JWT_SECRET
        sync: false            # freshly generated — NOT the local development value
      - key: SLINGKING_JWT_ISSUER
        value: https://slingking-api.onrender.com
      - key: SLINGKING_JWT_AUDIENCE
        value: https://slingking-api.onrender.com
      - key: SLINGKING_CORS_ORIGINS
        value: https://slingking.onrender.com
      # --- Not enforced by the app, but the client cannot log in without them. ---
      # DatabaseSeeder returns SILENTLY when either is blank: the deploy goes green and
      # nobody can sign in. See the spec's "silent failure" section.
      - key: SLINGKING_SEED_ADMIN_EMAIL
        sync: false
      - key: SLINGKING_SEED_ADMIN_PASSWORD
        sync: false
      # SLINGKING_LOGIN_ATTEMPTS_PER_MINUTE is deliberately absent. Its fallback is already
      # 10, the intended production value, so setting it adds a thing to get wrong.

  - type: web
    name: slingking
    runtime: static
    plan: free
    buildCommand: cd frontend && npm ci && npm run build
    staticPublishPath: ./frontend/dist
    autoDeploy: false
    envVars:
      # Vite BAKES this into the bundle at build time. Changing it needs a rebuild, not a
      # restart — editing the value alone does nothing.
      - key: VITE_API_BASE_URL
        value: https://slingking-api.onrender.com
    routes:
      # React Router serves /purchases and /inventory in the browser; those paths do not
      # exist on disk. Without this, refreshing on any screen but the root returns 404 —
      # and refreshing is behaviour every previous milestone verified working.
      - type: rewrite
        source: /*
        destination: /index.html
```

- [ ] **Step 2: Check the YAML parses**

Run:

```
node -e "const f=require('fs').readFileSync('render.yaml','utf8'); console.log(f.split('\n').length + ' lines'); if(/\t/.test(f)) { console.error('TAB CHARACTER — YAML forbids tabs for indentation'); process.exit(1);} console.log('no tabs');"
```

Expected: a line count and `no tabs`. YAML rejects tab indentation, and an editor inserting one produces an error message that does not mention tabs.

There is no YAML parser in this repository's dependencies and adding one to lint a single file would be a dependency bought for nothing. Render validates the Blueprint when it reads it; this check catches the one class of mistake that is silent locally.

- [ ] **Step 3: Confirm the referenced paths exist**

Run:

```
ls backend/Dockerfile frontend/package.json && grep -n '"build"' frontend/package.json
```

Expected: both files listed, and a `build` script. A Blueprint pointing at a path that does not exist fails at deploy time with a message about the path, several minutes into a remote build.

- [ ] **Step 4: Commit**

```bash
git add render.yaml
git commit -m "build: declare both Render services in a Blueprint

Configuration in the repo rather than clicked into a dashboard, so it can be
read, reviewed and restored. Secrets stay out — every sensitive value is
sync: false and lives only in Render's dashboard.

autoDeploy is off on both services because migrations run on startup, and the
static site carries a rewrite to index.html or refreshing on any deep link
returns 404.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 4: Write down what nobody will remember

**Files:**
- Modify: `README.md` (create it if absent)

**Interfaces:**
- Consumes: the variable names from Task 3's Blueprint.
- Produces: nothing code depends on.

The thing forgotten six months on is never how the code works — it is *which variables have to be set*, and *what the ritual is before clicking Deploy*. Both go in the README.

- [ ] **Step 1: Check what the README already says**

Run: `ls README.md 2>/dev/null && head -30 README.md`

If there is no README, create one with a short project description first — SlingKing is an inventory and sales system for a one-person handbag business, built as a modular monolith in .NET 8 with a React frontend and Supabase Postgres. Keep it to a few lines; this task is about deployment, not marketing.

- [ ] **Step 2: Add the deployment section**

Append to `README.md`:

````markdown
## Deployment

Two services on Render's free tier, declared in `render.yaml`. The database
stays on Supabase.

| | |
|---|---|
| API | `slingking-api` — Docker web service |
| Frontend | `slingking` — static site |
| Database | Supabase (unchanged) |

### Environment variables

Set these in Render's dashboard. Everything marked `sync: false` in
`render.yaml` must be filled in by hand.

**Hard-required — the API will not start without them:**

| Variable | Notes |
|---|---|
| `SLINGKING_DB_RUNTIME` | transaction pooler, port **6543**, include `Maximum Pool Size=5` |
| `SLINGKING_DB_MIGRATION` | session pooler, port **5432** |
| `SLINGKING_JWT_SECRET` | generate a fresh one; never reuse the local development value |
| `SLINGKING_JWT_ISSUER` | the API's URL (already in the Blueprint) |
| `SLINGKING_JWT_AUDIENCE` | the API's URL (already in the Blueprint) |
| `SLINGKING_CORS_ORIGINS` | the frontend's URL (already in the Blueprint) |

**Required in practice, enforced by nothing:**

| Variable | Notes |
|---|---|
| `SLINGKING_SEED_ADMIN_EMAIL` | the owner's login |
| `SLINGKING_SEED_ADMIN_PASSWORD` | there is **no change-password screen** — this is permanent |

> **If the seed variables are blank the deploy still goes green and nobody can
> log in.** `DatabaseSeeder` returns silently rather than failing, so the
> container starts, migrations apply, and the health check passes because the
> database is reachable. The only symptom is a login that never works.
> After the first deploy, confirm the user row exists rather than trusting the
> green tick.

`SLINGKING_LOGIN_ATTEMPTS_PER_MINUTE` is deliberately left unset — its fallback
is already the intended production value of 10.

`MaxPoolSize` matters: Supabase allows this project **15 client connections in
total**, shared with your development machine. Five leaves room to keep working
while the app is in use.

### Releasing

Auto-deploy is off on both services. Pushing to `main` updates GitHub and
changes nothing live.

To release:

1. Confirm the suite is green — `cd backend; dotnet test` and `cd frontend; npx vitest run`
2. Run `backup.ps1` — migrations run on startup, so this is the last point of return
3. Click **Deploy** in Render, API first, then the static site if the frontend changed

Render switches traffic only once `/api/v1/health` passes, so a container that
starts but cannot reach Supabase never receives requests.

**Changing `VITE_API_BASE_URL` requires rebuilding the static site**, not
restarting it — Vite bakes the value into the bundle at build time.

### Keeping it awake

Render's free tier sleeps a service after 15 minutes idle, and Supabase pauses a
free project after 7 days idle. One external cron solves both, because
`/api/v1/health` touches the database:

| | |
|---|---|
| URL | `https://slingking-api.onrender.com/api/v1/health` |
| Schedule | `*/10 19-23,0-2 * * *` |
| Timezone | **Asia/Kolkata** |

The window is 19:00–02:00 IST, when stock actually gets entered. It costs about
**225 of the 750 free instance-hours** a month; pinging around the clock would
cost ~744 and leave the account unable to run anything else.

Two traps in that schedule:

- **The window crosses midnight and cron ranges do not wrap.** `19-2` is
  invalid or silently wrong. It must be written as two ranges, `19-23,0-2`.
- **The scheduler must run in Asia/Kolkata.** IST is UTC+05:30, and no
  whole-hour UTC range expresses a half-hour offset correctly.

Turn on the cron's failure alerts. A pinger that stops silently is worse than
none, because the app is believed warm right up until someone finds it cold.
````

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: record the deployment variables and the release ritual

The thing forgotten six months on is which variables must be set and what the
ritual is before clicking Deploy. Both are now written down, including the
failure where blank seed variables produce a green deploy nobody can log into.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 5: Deploy it, and prove the client can actually use it

**Files:** none. This task creates services in Render's dashboard and a job in an external cron.

**Interfaces:**
- Consumes: `render.yaml` (Task 3), `backend/Dockerfile` (Task 2).
- Produces: a running deployment.

**This task is performed by a person with browser access to Render, Supabase and a cron provider.** An agent cannot click through those dashboards. If you are an agent executing this plan, stop here, report that Tasks 1–4 are complete, and hand this task back.

- [ ] **Step 1: Confirm the branch is releasable**

Run: `cd backend; dotnet build -warnaserror; if ($?) { dotnet test }` then `cd frontend; npx tsc -b; npm run lint; npx vitest run`

Expected: 155 integration, 87 unit, 37 frontend, zero warnings.

- [ ] **Step 2: Take a backup**

Run `backup.ps1`. Migrations run on startup, so the first deploy is the last point of return.

Do not skip this on the reasoning that a bad deploy just fails harmlessly. Free instances are replaced in place — the running one stops before the new one starts — so a failed deploy is an outage until you fix it or roll back, not a no-op.

- [ ] **Step 3: Create the services from the Blueprint**

In Render: **New → Blueprint**, connect the `SlingKing` repository, and let it read `render.yaml`.

**Check the names Render assigns.** `onrender.com` subdomains are globally unique, so `slingking-api` or `slingking` may be taken. If either is, Render assigns a different name and the URLs baked into the Blueprint are wrong. Two paths:

- **Both names free:** the URLs in `render.yaml` are already correct. One pass.
- **Either name taken:** note the real URLs, correct `SLINGKING_JWT_ISSUER`, `SLINGKING_JWT_AUDIENCE`, `SLINGKING_CORS_ORIGINS` and `VITE_API_BASE_URL` to match, commit, and redeploy **both** services. The frontend must be rebuilt, not restarted — its API URL is compiled in.

  **Correct them in `render.yaml` and re-sync the Blueprint — not in Render's dashboard.** All four carry a `value:` in the Blueprint, which makes them Blueprint-managed: a dashboard edit works immediately and is silently reverted on the next sync, at which point every browser call starts failing CORS with nothing in the server log. The JWT pair is the forgiving one, since the same process mints and validates and any self-consistent value works. `SLINGKING_CORS_ORIGINS` is not: it is compared as an exact string, so a trailing slash on the origin blocks every browser request and produces no server-side error at all.

- [ ] **Step 4: Fill in the secrets**

In the API service's Environment tab, set the five `sync: false` values:

`SLINGKING_DB_RUNTIME` (port **6543**), `SLINGKING_DB_MIGRATION` (port **5432**), a freshly generated `SLINGKING_JWT_SECRET`, and `SLINGKING_SEED_ADMIN_EMAIL` / `SLINGKING_SEED_ADMIN_PASSWORD` for the owner's login.

**Build both connection strings from `backend/.env.example`, not from memory.** The runtime string needs every one of these, and two of them fail quietly rather than loudly:

`Host=…;Port=6543;Database=postgres;Username=postgres.<ref>;Password=…;SSL Mode=Require;Trust Server Certificate=true;No Reset On Close=true;Max Auto Prepare=0;Maximum Pool Size=5`

Drop `Maximum Pool Size=5` and Npgsql defaults to 100 against Supabase's ceiling of 15 for this project: the deploy goes green, then requests start timing out under real use and your own machine is locked out at the same time. Drop `No Reset On Close=true` and Npgsql resets every connection it returns, which is the documented way to get intermittent errors from a transaction-mode pooler. The migration string is the same tail without those two and without `Max Auto Prepare=0`.

Never reuse the local `SLINGKING_JWT_SECRET`. A secret that has lived in a developer's `.env` is not a production secret.

**Use a `SLINGKING_SEED_ADMIN_EMAIL` that has never been seeded into `public`.** The seeder skips an email that already has a row and keeps that row's old password — see Step 6.

- [ ] **Step 5: Deploy the API and watch the health check**

Trigger a manual deploy. Watch the log for the migration output, then for Render reporting the service live.

A deploy that builds but never goes live is almost always the health check failing — which means the container started but could not reach Supabase. Check the connection strings before suspecting the image.

**Then check, once, that the proxy is not forwarding a client-supplied address.** This is the one security property of Task 1 that no test can reach, because the test harness leaves the socket address null:

```bash
curl -H "X-Forwarded-For: 1.2.3.4" https://<your-api>/api/v1/health
```

Read the request log line Render prints for it. It must show your real address. If it shows `1.2.3.4`, Render is passing the header through untouched instead of appending to it, and the login rate limiter is now partitioned on a value the caller chooses — a fresh fake address per attempt is a fresh bucket, so the ten-per-minute brake on password guessing is gone entirely. Nothing else will ever reveal this: no error, no log line, just a limiter that never fires. If it happens, say so and stop; the fix is a code change, not a setting.

- [ ] **Step 6: Confirm the admin can actually sign in**

**A green deploy is not evidence that anyone can log in — and neither is the admin row existing.** `DatabaseSeeder.SeedAdminUserAsync` returns without throwing in two different cases: when either seed variable is blank, and when a user with that email already exists. In the second case it keeps the existing password hash and silently discards the password you typed into Render. So the deploy is green, the health check passes, the row is right there in the table, and the only credential that works is one that lives on a developer's machine.

The gate is a successful sign-in, not a row. Two checks, in this order:

```sql
select id, email, created_at from public.users order by id;
```

A row whose `created_at` is older than this deploy means the seeder skipped it. Then sign in at the API with the exact credential you entered in Step 4:

```bash
curl -i -X POST https://<your-api>/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"<the email you set>","password":"<the password you set>"}'
```

Expected: **200** with a token. A **401** means the password in Render is not the password on that row.

If either check fails, the fix depends on which one. Blank or misspelled variables: correct them and redeploy, and the seeder creates the user on the next startup. An email that was already seeded: either switch to an address that has never been used, or delete the row and redeploy. Deleting is safe — only `refresh_tokens.user_id` references `users` — but delete it from `public` deliberately, and take the backup in Step 2 first.

There is no change-password screen, so a wrong password here is not something the client can recover from on their own.

- [ ] **Step 7: Deploy the frontend and walk the app**

Deploy the static site, then open its URL and confirm, as the client would:

1. The login screen loads **instantly** — static sites do not sleep
2. Signing in with the seeded credentials works
3. Products, Inventory and Purchases all load
4. Recording a purchase works end to end, and stock rises
5. **Refresh the page while on `/purchases`** — it must reload the app, not 404. This is the SPA rewrite, and it is the failure most likely to slip through, because every other check passes without it.

- [ ] **Step 8: Set up the pinger**

At cron-job.org (or another free scheduler that supports timezones), create a job:

| | |
|---|---|
| URL | `https://<your-api>/api/v1/health` |
| Schedule | `*/10 19-23,0-2 * * *` |
| Timezone | **Asia/Kolkata** |
| Failure alerts | **on** |

Write the schedule as two ranges. `19-2` does not wrap and will not do what it appears to.

- [ ] **Step 9: Prove the pinger works**

Wait for one scheduled run inside the window and confirm the job reports a 200. Then, outside the window or after a deliberate 20-minute gap, open the app and confirm it takes roughly a minute to answer — which is the cold start, working as designed.

Knowing what a cold start feels like matters: it is the difference between "the app is asleep" and "the app is broken", and you are the one who will have to tell them apart.

- [ ] **Step 10: Tell the client what to expect**

Give them the URL, their login, and one sentence about the evening window: the app is instant between 7pm and 2am, and may take a minute to wake at other times.

An unexplained minute of blank screen is indistinguishable from a broken app. An explained one is a free tier.

- [ ] **Step 11: Tick this plan's boxes and commit**

Mark every checkbox in this file, then:

```bash
git add docs/superpowers/plans/2026-09-13-r1-m4-render-deployment.md
git commit -m "docs: mark M4 plan tasks complete

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage.** Every section of the spec maps to a task:

| Spec section | Task |
|---|---|
| `UseForwardedHeaders`, the only code change | 1 |
| Cleared `KnownNetworks` / `KnownProxies` | 1, proved by removing them in Step 6 |
| Two-stage Dockerfile, non-root, `$PORT` entrypoint | 2 |
| `.dockerignore` excluding `.env` | 2, verified by inspecting the image in Step 4 |
| Local container run against the **test** schema | 2 |
| `render.yaml` Blueprint, secrets `sync: false` | 3 |
| Health check path, `autoDeploy: false` | 3 |
| SPA rewrite to `index.html` | 3, verified in the browser at Task 5 Step 7 |
| `Maximum Pool Size=5` | 3 and 4 (documented), set in Task 5 Step 4 |
| The silent seeding failure | 4 (documented), guarded in Task 5 Step 6 |
| Ping window, cron expression, timezone | 4 (documented), created in Task 5 Step 8 |
| One-pass vs two-pass first deploy | Task 5 Step 3 |
| Gate | Task 5, Steps 1–9 |

**Two deliberate deviations from the spec, flagged rather than applied silently:**

1. **The spec's gate lists "the image builds locally and answers `/api/v1/health`" as a gate item; the plan does it in Task 2** rather than deferring it to the gate task. A container check that happens after the Blueprint is written is a check whose failure invalidates work already done. Moving it earlier costs nothing and fails sooner.
2. **The spec says the forwarded-headers test uses "a service configured with a low attempt limit" without saying how.** The plan sets the limit through an environment variable in a derived factory, and explains in a comment why that is safe despite environment variables being process-global — `Program.cs` reads the value once at host build time, so each factory's host captures its own. This was worth writing down because the previous milestone lost hours to cross-test interference from exactly this kind of global.

**Placeholder scan.** No `TBD`, no "add error handling", no "similar to Task N". Every code step carries the actual content. Task 5's steps are dashboard actions rather than code, and each states the exact value to enter and what to check afterwards.

**Type consistency.** The service names `slingking-api` and `slingking` are fixed in Task 3 and used verbatim in Task 4's README and Task 5's steps. The variable names in Task 3's Blueprint, Task 4's tables and Task 5's Step 4 are the same eight strings throughout, and match the six `Required(...)` calls plus the two seed variables read in `DependencyInjection.cs`. The cron expression `*/10 19-23,0-2 * * *` appears identically in Task 4 and Task 5.

**One thing an executor will hit.** Task 5 cannot be performed by an agent — it is entirely dashboard work across Render, Supabase and a cron provider. The task says so in its opening line so an agent stops and hands back rather than inventing plausible-looking completions for steps it cannot take.
