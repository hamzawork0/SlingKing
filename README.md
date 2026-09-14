# SlingKing

Inventory and sales system for a one-person handbag business selling through
Instagram, WhatsApp and offline. Modular monolith in .NET 8 with a React
frontend and Supabase Postgres.

## Running locally

- API: `dotnet run` from `backend/src/SlingKing.Api`
- Frontend: `npm run dev` from `frontend`

Both need a `.env` file (`backend/.env`, `frontend/.env`) — these are
gitignored and not in the repository, so you'll need the values from
somewhere else.

> **After this deploy, `dotnet run` writes to the client's live data.** There is
> one Supabase database and one `public` schema, shared by your machine and the
> deployed API; only the test suite is isolated. Starting the API locally also
> runs `MigrateAsync` against it, so a migration added during development is
> applied to production the moment you press F5 — outside any deploy gate and
> without `backup.ps1` having run. Until development gets its own schema, run
> `backup.ps1` before starting the API with an unapplied migration in the tree.

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
| `SLINGKING_DB_RUNTIME` | transaction pooler, port **6543**. Every parameter matters — see below |
| `SLINGKING_DB_MIGRATION` | session pooler, port **5432**, same tail minus the last three parameters |
| `SLINGKING_JWT_SECRET` | generate a fresh one; never reuse the local development value |
| `SLINGKING_JWT_ISSUER` | the API's URL (already in the Blueprint) |
| `SLINGKING_JWT_AUDIENCE` | the API's URL (already in the Blueprint) |
| `SLINGKING_CORS_ORIGINS` | the frontend's URL (already in the Blueprint) |

Build both strings from the templates in `backend/.env.example`, which are
complete, rather than from prose. The runtime string needs all of:

`Host=…;Port=6543;Database=postgres;Username=postgres.<ref>;Password=…;SSL Mode=Require;Trust Server Certificate=true;No Reset On Close=true;Max Auto Prepare=0;Maximum Pool Size=5`

Two of those fail quietly if dropped. Without `Maximum Pool Size=5` Npgsql
defaults to 100 against Supabase's ceiling of 15 for this project, so the
deploy is green and requests start timing out under real use — locking your own
machine out at the same time. Without `No Reset On Close=true` Npgsql resets
every connection it returns, which is the documented way to get intermittent
errors from a transaction-mode pooler.

**Required in practice, enforced by nothing:**

| Variable | Notes |
|---|---|
| `SLINGKING_SEED_ADMIN_EMAIL` | the owner's login |
| `SLINGKING_SEED_ADMIN_PASSWORD` | there is **no change-password screen** — this is permanent |

> **The seeder fails silently, in two different ways, and the deploy goes green
> for both.** `DatabaseSeeder` returns rather than throwing when the seed
> variables are blank, and returns again when a user with that email already
> exists — in that second case it keeps the old password and discards the one
> you typed into Render. So an admin row existing proves nothing.
>
> Use an email that has never been seeded into `public`, and verify the deploy
> by **signing in with the credential you set in Render**, not by finding the
> row. There is no change-password screen to recover through.

`SLINGKING_LOGIN_ATTEMPTS_PER_MINUTE` is deliberately left unset — its fallback
is already the intended production value of 10.

`MaxPoolSize` matters: Supabase allows this project **15 client connections in
total**, shared with your development machine. Five leaves room to keep working
while the app is in use.

**The static site's only variable:**

| Variable | Notes |
|---|---|
| `VITE_API_BASE_URL` | the API's origin — **no trailing slash and no `/api/v1` suffix**, the frontend adds the path itself. Baked in at build time; changing it needs a rebuild, not a restart |

### Releasing

Auto-deploy is off on both services. Pushing to `main` updates GitHub and
changes nothing live.

To release:

1. Confirm the suite is green — `cd backend; dotnet test` (needs `SLINGKING_DB_TEST` in
   `backend/.env`) and `cd frontend; npx vitest run`
2. Run `backup.ps1` — migrations run on startup, so this is the last point of return
3. Click **Deploy** in Render, API first, then the static site if the frontend changed

A container that cannot reach Supabase crashes during startup migration, before
it serves anything — so a wrong connection string fails the deploy loudly rather
than going green and misbehaving.

> Do not read that as "a bad deploy is safe". Free instances are replaced in
> place — the running one stops before the new one starts — so a failed deploy
> takes the app down until you fix it or roll back. Step 2 is not optional.
> Confirm this on the first real deploy and write down what you observe.

**Changing `VITE_API_BASE_URL` requires rebuilding the static site**, not
restarting it — Vite bakes the value into the bundle at build time.

Once, on the first deploy, confirm the proxy is not passing a client-supplied
address straight through:

`curl -H "X-Forwarded-For: 1.2.3.4" https://slingking-api.onrender.com/api/v1/health`

The request log must show your real address. If it shows `1.2.3.4`, the login
rate limiter partitions on a value the caller chooses — a new fake address per
attempt is a new bucket, which removes the brake on password guessing entirely.
Nothing else will reveal this: there is no error and no log line, only a limiter
that never fires.

### If the service names are taken

`slingking` and `slingking-api` are global to Render. If either is taken, Render
assigns a different subdomain and three variables point at the wrong host:
`SLINGKING_CORS_ORIGINS`, `SLINGKING_JWT_ISSUER`/`_AUDIENCE` and
`VITE_API_BASE_URL`.

The JWT pair is harmless — the same process mints and validates, so any
self-consistent value works. The other two are not, and **they cannot be fixed
in the dashboard**: both carry a `value:` in `render.yaml`, so a dashboard edit
survives until the next Blueprint sync and is then silently reverted, at which
point every browser call starts failing CORS with nothing in the server log.

Correct them in `render.yaml`, commit, and re-sync the Blueprint. Then
**rebuild** the static site — restarting it will not pick up a new
`VITE_API_BASE_URL`.

`SLINGKING_CORS_ORIGINS` is compared as an exact string. A trailing slash on the
origin blocks every browser request and produces no server-side error.

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
