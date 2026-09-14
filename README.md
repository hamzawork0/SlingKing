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
