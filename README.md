# plan-d

Medicare Part D quoting for insurance brokers. A patient's drugs and ZIP code go in, and a ranked shortlist of stand-alone Part D prescription drug plans (PDPs) comes out, each with its estimated yearly cost (premium + drugs) and the reasons behind its rank. Medicare Advantage is out of scope.

Build plan and decisions: [plan-d — Build Plan](https://claude.ai/code/artifact/5cdf41e5-36b7-4be1-9551-7aad42114c08).
Data research: [`docs/research/`](docs/research/).

## Layout

| Path | What it is |
| --- | --- |
| `src/PlanD.Engine` | Part D cost simulator and quote service. Plain C#, no database. |
| `src/PlanD.Data` | Postgres schema (`Sql/`), loaders for the CMS/NLM/Census files (`Ingest/`), quote repository |
| `src/PlanD.Api` | ASP.NET Core API: broker login, patient intake, quotes, snapshots; serves the web app |
| `src/PlanD.Cli` | `plan-d` command line: migrate, ingest, zip lookup, drug search, quote, validate |
| `web/` | React app (Vite, Tailwind, shadcn/ui): patient intake and broker workspace |
| `tests/PlanD.Engine.Tests` | Simulator and quote-service tests with hand-computed expectations |
| `data/raw/` | Downloaded source files (git-ignored, ~30 GB) |

## Running it with Docker (no .NET or Node needed)

```sh
docker compose up --build               # → http://localhost:5080 (brokers: /login, patients: /start/maria-alvarez)
docker compose --profile dev up         # live reload: Vite on http://localhost:5173, API on :5080, source mounted
```

- First start restores the CMS reference data (plans, drugs, pharmacies) from `data/dumps/cms.dump`, migrates,
  and loads the demo agencies/brokers/clients if the app is empty. Demo login: `maria@sunshine.example.com` /
  `kitchen-table-demo`.
- `data/dumps/cms.dump` (~40 MB) isn't in git. Make it from a loaded database with `scripts/export-cms.sh`, or copy
  it from a machine that has one. Without it the app starts but has no plans to quote.
- Reset the demo: `docker compose exec app kt demo-reset` (or `docker compose exec api-dev dotnet run --project src/PlanD.Api -- demo-reset`).
- Data CLI inside the image: `docker compose run --rm app cli releases`, `… cli quote --zip 33135 --year 2026 --drug 617310:30`.
- Emails go to the container log (`docker compose logs -f app`).
- Don't run `app` and the dev profile at the same time: both use port 5080.

## Running it on the host

```sh
docker compose up -d                       # Postgres 17 on localhost:5442
dotnet build
alias pland=src/PlanD.Cli/bin/Debug/net10.0/PlanD.Cli

pland db migrate
pland ingest geo data/raw/geo --activate
pland ingest rxnorm data/raw/rxnorm/prescribe/rrf --activate
pland ingest landscape data/raw/landscape/CY2026_Landscape_202609/CY2026_Landscape_202609.csv --year 2026 --activate
pland ingest spuf data/raw/puf/SPUF_2026_20260701 --year 2026 --activate    # ~1 min; streams 26 GB, keeps the PDPs

pland ingest pharmacies data/raw/nppes/NPPES_Data_Dissemination_September_2026_V2.zip --activate

pland zip 33101
pland drugs atorvastatin 20
pland quote --zip 33101 --year 2026 --drug 617310:30 --drug 861007:60
```

## Web app

```sh
dotnet run --project src/PlanD.Api -- migrate       # app schema (and cms, if needed)
dotnet run --project src/PlanD.Api -- demo-reset    # demo agencies, brokers, clients (wipes app data!)
dotnet run --project src/PlanD.Api --no-launch-profile --urls http://localhost:5080

cd web && npm install && npm run dev                # http://localhost:5173, proxies /api to :5080
```

- Brokers: `/login` (demo: `maria@sunshine.example.com` / `kitchen-table-demo`), workspace under `/app`.
- Agency admins (demo: Maria, Denise) manage their brokers under `/app/brokers`: invite, promote, deactivate. An invited
  broker sets up their login at `/join/{token}`.
- Patients: a broker's public link `/start/{slug}` (e.g. `/start/maria-alvarez`), invite/return links `/i/…`, `/r/…`.
- Emails are written to the API log in development (`Email:Provider` = `log`); set `Email:Provider=resend`,
  `Email:ResendApiKey` and `Email:BaseUrl` for real delivery.
- `dotnet build` regenerates `web/src/api/PlanD.Api.json`; run `npm run gen:api` in `web/` to refresh the TypeScript types.
- `npm run build` writes the app into `src/PlanD.Api/wwwroot`, which the API serves.

Every load creates a release in `cms.release`. A release becomes `ready` once its checks pass, and `activate` switches quotes over to it, so a bad CMS file never replaces a good one.

## Tests

```sh
dotnet test
```
