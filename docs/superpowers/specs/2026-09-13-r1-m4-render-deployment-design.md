# R1 · M4 — Deploying to Render

**Date:** 2026-09-13
**Status:** approved, awaiting implementation plan
**Predecessor:** R1 · M3 (tag `r1-m3`) — purchases and suppliers

---

## What this milestone is

Putting SlingKing somewhere the client can open it, on Render's free tier,
without it looking broken when it has been idle.

It is almost entirely configuration. The application changes by four lines.

## What already exists, and why that shrinks this milestone

The app was built to be deployed, even though it never has been. Everything
below is already true and needs no work:

| Already done | Where |
|---|---|
| Every secret and connection string comes from an environment variable | `Program.cs` reads six via `Required(...)` |
| CORS origins are configuration, not code | `SLINGKING_CORS_ORIGINS`, comma-separated |
| Migrations run on startup | `Program.cs:205`, `MigrateAsync()` |
| The admin account is seeded from environment variables | `Program.cs:208-211` via `DatabaseSeeder` |
| A health endpoint that checks the **database**, not just the process | `HealthController` calls `CanConnectAsync` |
| No HTTPS redirect | nothing to loop behind Render's TLS |
| The pool size is settable without code | it lives in the connection string |

Facts settled by reading rather than by asking:

- **Six** variables are hard-required — the app refuses to start without them:
  `SLINGKING_DB_RUNTIME`, `SLINGKING_DB_MIGRATION`, `SLINGKING_JWT_SECRET`,
  `SLINGKING_JWT_ISSUER`, `SLINGKING_JWT_AUDIENCE`, `SLINGKING_CORS_ORIGINS`.
- **Three more are optional with fallbacks**, and one of those fallbacks is a
  trap. `SLINGKING_LOGIN_ATTEMPTS_PER_MINUTE` defaults to `10`, which is
  correct and needs no setting. But `SLINGKING_SEED_ADMIN_EMAIL` and
  `SLINGKING_SEED_ADMIN_PASSWORD` default to empty, and
  `DatabaseSeeder.cs:114` **returns silently** when either is blank. See "the
  silent failure" below.
- The frontend needs exactly one: `VITE_API_BASE_URL`, and Vite **bakes it in
  at build time** — changing it requires a rebuild, not a restart.
- The Docker CLI is installed on the development machine; the daemon needs
  starting. The image can therefore be tested locally.
- Render has **no native .NET runtime**. A Dockerfile is not a preference.

## Decisions taken

| Decision | Choice | Reasoning |
|---|---|---|
| Host | Render, free tier | Standing project constraint: everything free, no paid tooling. |
| Configuration | `render.yaml` Blueprint in the repo; secrets in the dashboard | The configuration becomes readable, reviewable and restorable. A deleted service is recreated from a file rather than from memory. |
| Sleeping | An external free cron pings `/api/v1/health` every 10 minutes, **19:00–02:00 IST** | One request solves both problems — the endpoint touches the database, so it wakes Render and unpauses Supabase together. The window matches when the owner actually enters stock, and costs ~225 of 750 free hours instead of ~744. |
| Backups | Supabase's own daily backups, 7-day window | A shop recording a few purchases a week survives losing at most a day. `backup.ps1` is still run by hand before anything risky. |
| Deploys | **Manual.** Auto-deploy off | Migrations run on startup, so an automatic deploy would change the client's live database as a side effect of a merge. The click makes it a decision. |
| Application changes | `UseForwardedHeaders` only | Everything else is configuration. |

## Architecture

Two Render services plus one external cron.

| Service | Type | Sleeps? | Instance hours |
|---|---|---|---|
| `slingking-api` | Web Service (Docker) | awake 19:00–02:00 IST, asleep otherwise | ~225/month of 750 |
| `slingking` | Static Site | never | none — static sites do not consume them |
| Pinger | external cron (cron-job.org or UptimeRobot) | n/a | none |

The database stays on Supabase and does not move.

### The container

Two stages. The .NET 8 SDK image restores and publishes; the ASP.NET runtime
image carries only the published output and runs as a **non-root** user.

`.dockerignore` excludes `bin/`, `obj/`, `node_modules/`, `backups/`, and —
the one that matters — `backend/.env`. A secret that never enters the build
context cannot be layered into an image.

**Port binding is the single thing that must be right or nothing starts.**
Render assigns a port through `PORT` and expects the process to bind it. A
Dockerfile `ENV` cannot expand a variable that does not exist at build time, so
the entrypoint uses shell form:

```
dotnet SlingKing.Api.dll --urls http://0.0.0.0:${PORT:-10000}
```

The default keeps the image runnable locally.

### The one code change

`UseForwardedHeaders`, in `Program.cs`. M1 left the note that makes this due:

> *proxy's IP and every visitor shares one bucket — add `UseForwardedHeaders`
> when this deploys behind one, or the limit becomes a self-inflicted outage.*

The login rate limiter partitions on `RemoteIpAddress`. Behind Render's proxy
every request carries the proxy's address, so all visitors share one bucket of
ten attempts a minute. With one user that is survivable; with two it is an
outage neither caused. The middleware also makes scheme and host correct for
anything that reads them later.

### Configuration

Eight variables on the API service, all already read from the environment.
**Six are hard-required** — the app throws on startup without them:

| Variable | Value |
|---|---|
| `SLINGKING_DB_RUNTIME` | transaction pooler, port **6543**, with **`Maximum Pool Size=5`** |
| `SLINGKING_DB_MIGRATION` | session pooler, port **5432** |
| `SLINGKING_JWT_SECRET` | freshly generated — **not** the local development value |
| `SLINGKING_JWT_ISSUER` / `_AUDIENCE` | the API's own URL |
| `SLINGKING_CORS_ORIGINS` | the static site's URL |

**Two more must be set even though nothing enforces them:**

| Variable | Value |
|---|---|
| `SLINGKING_SEED_ADMIN_EMAIL` / `_PASSWORD` | the client's login, created on first boot |

`SLINGKING_LOGIN_ATTEMPTS_PER_MINUTE` is deliberately **left unset**: its
fallback is already `10`, which is the intended production value, so setting it
adds a thing to get wrong for no benefit.

**The pool size is load-bearing.** Supabase allows this project **fifteen**
client connections in total. The deployed API holds a pool against that
ceiling, and so does the development machine while work continues. Bounding the
deployment at five leaves room to keep working while the client uses the app.

Secrets are marked `sync: false` in the Blueprint so they live only in Render's
dashboard and never in git.

**There is no change-password screen.** Whatever is set in
`SLINGKING_SEED_ADMIN_PASSWORD` is permanent and visible to the operator in
Render's dashboard. Acceptable for a trial; recorded here so it is a known gap
rather than a discovery.

### The silent failure

This is the most dangerous thing in the milestone, and nothing in the
application will warn about it.

`DatabaseSeeder.cs:114` returns early — without error — when either seed
variable is blank:

```csharp
if (string.IsNullOrWhiteSpace(_adminEmail) || string.IsNullOrWhiteSpace(_adminPassword))
{
    return;
}
```

Forget them and **everything looks perfect**. The container starts, migrations
apply, the health check passes because the database is reachable, and Render
reports the service live and green. The client opens the app and simply cannot
log in, with nothing anywhere explaining why — and the natural next move,
redeploying, changes nothing.

The gate therefore requires **confirming the admin row exists** after the first
deploy, rather than inferring it from a green health check. A green deploy is
not evidence that anyone can sign in.

### The static site

Built with `npm ci && npm run build`, serving `frontend/dist`. Static sites do
not sleep and do not consume instance hours, so the first screen the client
sees is always instant — which is what makes a sleeping API tolerable.

**A rewrite rule is mandatory.** React Router serves `/purchases` and
`/inventory` in the browser, but those paths do not exist on disk. Without
`/* → /index.html`, refreshing on any screen but the root returns 404 — and
refreshing is behaviour every previous milestone's walkthrough explicitly
verified.

### The circular URL problem

The frontend bakes in the API's address; the API's CORS allowlist needs the
frontend's address. Neither URL exists before the services do.

Render derives URLs from service names — `slingking-api.onrender.com` from a
service named `slingking-api` — so if both names are free, both URLs are
predictable and the Blueprint can carry the real values from the start:
**one pass**.

Those subdomains are globally unique across all of Render, so a name may be
taken. If either is, Render assigns a different one and the deploy becomes
**two passes**: ship with the predicted values, read the real URLs, correct
both, redeploy. The plan states both paths so the first attempt cannot fail
confusingly.

### The pinger

A free external cron hits the API's `/api/v1/health` every ten minutes,
**between 19:00 and 02:00 IST** — the hours the owner actually enters stock.
The endpoint is `[AllowAnonymous]`, so no credential sits in a third-party
scheduler, and it calls `CanConnectAsync`, so one request keeps Render awake
**and** Supabase unpaused.

### Why one window serves two different problems

The two sleep problems need very different frequencies, and pricing both at the
stricter one is what makes a pinger expensive:

| Problem | Needs a ping | Pattern that satisfies it |
|---|---|---|
| Render sleeps after 15 minutes idle | more often than every 15 min | every 10 min, but only while she is working |
| Supabase pauses after 7 days idle | once a week | any single ping in the window covers it |

So the narrow window is not a compromise on the Supabase half — it satisfies
that requirement many times over. The only thing given up is an instant
response during hours nobody is using the app.

### The cost

Render bills instance-hours while the service is **running**, and it stays up
roughly 15 minutes after the last request. A seven-hour window therefore costs
the window plus a short tail:

**~7.25 hours/day × 31 ≈ 225 hours a month, against a 750-hour allowance.**

That leaves about **525 hours spare** — room for a second free service later, or
a bad month, without risking suspension. Contrast around-the-clock pinging,
which costs ~744 of 750 and leaves the account unable to run anything else.

### The midnight crossing — the detail that will silently half-work

**19:00–02:00 spans midnight, and cron ranges do not wrap.** A schedule written
as `*/10 19-2 * * *` is either rejected or, worse, quietly interpreted as
something else. It must be two ranges:

```
*/10 19-23,0-2 * * *
```

**The scheduler must be set to Asia/Kolkata**, not UTC. IST is UTC+05:30, so
the window in UTC is 13:30–20:30 — a half-hour offset that no whole-hour cron
range expresses correctly. cron-job.org supports per-job timezones; if a
scheduler offers only UTC, the expression has to become
`30 13 * * *`-style half-hour steps, which is worth avoiding by choosing a
scheduler that handles timezones.

### Alerting

**Failure alerts must be switched on.** A pinger that silently stops is worse
than none, because the app is believed warm right up until the client finds it
cold.

Render's free-tier terms should be confirmed on their site before relying on
these numbers; they may have changed.

### Releasing

Auto-deploy is off. Pushing to `main` updates GitHub and nothing else.

A release is: confirm the suite is green → run `backup.ps1` → click **Deploy**.
The image builds, migrations run, the seeder ensures the admin exists, and
Render switches traffic only once the health check passes. That health check is
what makes the manual gate mean something — a container that starts but cannot
reach Supabase never receives traffic.

## Testing

Most of a deployment cannot be unit tested. The two parts that can are the two
most likely to bite.

**The container, locally.** Start Docker Desktop, build the image, run it with
`PORT` set and the environment pointed at the **`test` schema — never
`public`** — and confirm `/api/v1/health` answers. This catches the entire
class of failures that otherwise appear only in a remote build log: a project
missing from the build context, an entrypoint that does not expand `$PORT`, a
runtime image lacking something the publish output needs. Minutes locally
against a long round trip through Render.

**Forwarded headers, as a real integration test.** Two login attempts carrying
different `X-Forwarded-For` addresses, against a service configured with a low
attempt limit, must land in **separate** rate-limit buckets. Without the
middleware both share the proxy's address, one bucket fills, and the second
caller is locked out by someone else's typing.

**The middleware is removed first and the test watched to fail.** A
rate-limiting test never seen to fail proves nothing, and this is precisely the
failure M1's comment predicted.

**No secret in the image**, verified by inspecting the build context rather
than trusting `.dockerignore` to be correct.

## Gate

1. Full suite green — 153 integration, 87 unit, 37 frontend — plus the new
   forwarded-headers test, **seen to fail** without the middleware
2. The image builds locally and answers `/api/v1/health` against the test schema
3. Both services live on Render with the health check passing
4. **The admin row exists**, confirmed by querying the database — not inferred
   from a green deploy, which is compatible with nobody being able to log in
5. The client can sign in, record a purchase, and **refresh the page on a deep
   link** — the SPA rewrite, and the failure most likely to slip through
6. The pinger is confirmed running with failure alerts enabled
7. `.env` verified absent from the image; the repository still pushes no secrets

## Out of scope

- **CI.** Nothing runs the tests automatically; the gate is a person.
- A custom domain.
- A staging environment.
- **A password-change screen** — a real gap, recorded, not built.
- Auto-deploy.
- A second Render service. Not because the budget forbids it — the narrow ping
  window leaves ~525 hours spare, which is the point of choosing it — but
  because nothing in this milestone needs one.
- Moving the database off Supabase.

## Standing constraints this milestone must honour

- Environment variables for connection strings, JWT secrets, API and CORS
  configuration; no hardcoded secrets
- No internal exceptions reach the frontend
- No deletion of transactional data
- Database transactions for critical operations
- **Everything free: no paid tooling, no paid tier, no licences**
