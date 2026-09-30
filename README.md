# plan-d

Medicare Part D quoting for insurance brokers. A patient's drugs and ZIP code go in, and a ranked shortlist of stand-alone Part D prescription drug plans (PDPs) comes out, each with its estimated yearly cost (premium + drugs) and the reasons behind its rank. Medicare Advantage is out of scope.

Build plan and decisions: [plan-d — Build Plan](https://claude.ai/code/artifact/5cdf41e5-36b7-4be1-9551-7aad42114c08).
Data research: [`docs/research/`](docs/research/).

## Layout

| Path | What it is |
| --- | --- |
| `src/PlanD.Engine` | Part D cost simulator and quote service. Plain C#, no database. |
| `src/PlanD.Data` | Postgres schema (`Sql/`), loaders for the CMS/NLM/Census files (`Ingest/`), quote repository |
| `src/PlanD.Cli` | `plan-d` command line: migrate, ingest, zip lookup, drug search, quote |
| `tests/PlanD.Engine.Tests` | Simulator and quote-service tests with hand-computed expectations |
| `data/raw/` | Downloaded source files (git-ignored, ~30 GB) |

## Running it

```sh
docker compose up -d                       # Postgres 17 on localhost:5442
dotnet build
alias pland=src/PlanD.Cli/bin/Debug/net10.0/PlanD.Cli

pland db migrate
pland ingest geo data/raw/geo --activate
pland ingest rxnorm data/raw/rxnorm/prescribe/rrf --activate
pland ingest landscape data/raw/landscape/CY2026_Landscape_202609/CY2026_Landscape_202609.csv --year 2026 --activate
pland ingest spuf data/raw/puf/SPUF_2026_20260701 --year 2026 --activate    # ~1 min; streams 26 GB, keeps the PDPs

pland zip 33101
pland drugs atorvastatin 20
pland quote --zip 33101 --year 2026 --drug 617310:30 --drug 861007:60
```

Every load creates a release in `cms.release`. A release becomes `ready` once its checks pass, and `activate` switches quotes over to it, so a bad CMS file never replaces a good one.

## Tests

```sh
dotnet test
```
