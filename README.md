# CommerceFlow

Order Management & Fulfillment backend, built as a portfolio project for Senior Backend .NET
interview preparation. It simulates a real legacy .NET Framework → Modular Monolith migration:
DDD, Clean Architecture, CQRS, event-driven integration, ERP resilience, and eventual
microservice extraction — starting from a deliberately realistic legacy baseline, not a clean
slate.

Domain scope: **Order → Stock Reservation → Warehouse Routing → ERP Export → Shipment.**

## Current milestone: M1 complete — target skeleton alongside the M0 legacy baseline

Everything under `database/` and `legacy/` is a **deliberately simple, legacy-style 3-tier
implementation** of order creation and ERP export — anemic models, raw ADO.NET, business
invariants living in a stored procedure, HTTP 200 for business failures. This is not the target
architecture; it exists to be migrated *from* (strangler-fig pattern) starting in M2, not extended
in place. See [`docs/legacy-technical-debt.md`](docs/legacy-technical-debt.md) for what's
intentional.

Everything under `src/` is the target-side skeleton built in M1: `CommerceFlow.Api` plus an
`Ordering` module (`Domain`/`Application`/`Infrastructure`), a deterministically-enforced
dependency graph, Serilog structured logging, correlation ID propagation, and central exception
handling. See [`docs/roadmap.md`](docs/roadmap.md) for the full M0/M1 breakdown and what's
deliberately deferred to later milestones.

## Project structure

| Project | Layer | Responsibility |
|---|---|---|
| `database/CommerceFlow.Database.Migrations` | — | DbUp migration runner: `migrate`, `reset-data`, `list-scripts` |
| `legacy/CommerceFlow.Legacy.Models` | Models | Anemic POCOs shared by DAL/BLL/Web |
| `legacy/CommerceFlow.Legacy.DAL` | Data access | Raw ADO.NET against `usp_CreateOrder` / `usp_GetOrderById` |
| `legacy/CommerceFlow.Legacy.BLL` | Business logic | Thin orchestration over the DAL |
| `legacy/CommerceFlow.Legacy.Web` | API | `OrdersController` (`POST`/`GET /api/orders`) |
| `legacy/CommerceFlow.FakeErp` | — | Stand-in for the real external ERP system |
| `legacy/CommerceFlow.Legacy.ErpExportJob` | Batch | One-shot, Task-Scheduler-style ERP export job |
| `src/CommerceFlow.Api` | API (target) | Composition root: correlation ID middleware, central exception handling, Serilog |
| `src/Modules/Ordering/CommerceFlow.Modules.Ordering.Domain` | Domain (target) | Zero dependencies — no other CommerceFlow project, no NuGet package |
| `src/Modules/Ordering/CommerceFlow.Modules.Ordering.Application` | Application (target) | Depends only on `Domain` |
| `src/Modules/Ordering/CommerceFlow.Modules.Ordering.Infrastructure` | Infrastructure (target) | Depends on `Domain` + `Application` |
| `tests/CommerceFlow.Legacy.CharacterizationTests` | Tests | Testcontainers-backed characterization + concurrency tests (M0) |
| `tests/CommerceFlow.ArchitectureTests` | Tests | Deterministic `.csproj` reference-graph checks (M1) |
| `tests/CommerceFlow.Api.Tests` | Tests | Correlation ID + exception handling behavior (M1) |

## Prerequisites

- .NET 8 SDK
- Docker Desktop (local SQL Server runs in a container — there is no native SQL Server dependency)

## Running SQL Server locally (Docker)

CommerceFlow does not assume a native SQL Server instance in local dev. PowerShell:

```powershell
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<YOUR_LOCAL_SA_PASSWORD>" -p 14330:1433 --name commerceflow-sql -d mcr.microsoft.com/mssql/server:2022-latest
```

Port **14330**, not the default 1433 — mapped to the container's internal 1433, chosen to avoid
colliding with any native SQL Server instance that might already be listening on 1433 on your
machine. Wait for it to accept connections before continuing:

```powershell
do {
    Start-Sleep -Seconds 3
    $ready = docker exec commerceflow-sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "<YOUR_LOCAL_SA_PASSWORD>" -Q "SELECT 1" 2>$null
} until ($ready)
```

## Migration vs. seed/reset — these are different operations

- **`migrate`** applies schema changes (tables, stored procedures). Safe to run repeatedly —
  already-applied scripts are skipped (tracked in DbUp's own journal table). This is what would
  run in any real environment, including production.
- **`reset-data`** is *not* the same thing. It wipes **all** data in `Customers`, `Products`,
  `Orders`, `OrderItems` and reseeds deterministic development fixtures.
  **This is destructive and irreversible for whatever data currently exists in those tables.**
  It refuses to run unless `CommerceFlowEnvironment` is exactly `Development` or `Test` — there is
  no path to running it against anything else.

```powershell
$env:COMMERCEFLOW_DB_CONNECTION = "Server=localhost,14330;Database=CommerceFlowDb;User Id=sa;Password=<YOUR_LOCAL_SA_PASSWORD>;TrustServerCertificate=True;"
cd database/CommerceFlow.Database.Migrations
dotnet run -- migrate

$env:CommerceFlowEnvironment = "Development"
dotnet run -- reset-data   # WARNING: deletes all existing Customers/Products/Orders/OrderItems data
```

## Running the Web API and FakeErp

Two separate terminals — both are long-running processes.

```powershell
# Terminal 1 — FakeErp
$env:ASPNETCORE_URLS = "http://localhost:5296"
cd legacy/CommerceFlow.FakeErp
dotnet run --no-launch-profile
```

```powershell
# Terminal 2 — Web API
$env:COMMERCEFLOW_DB_CONNECTION = "Server=localhost,14330;Database=CommerceFlowDb;User Id=sa;Password=<YOUR_LOCAL_SA_PASSWORD>;TrustServerCertificate=True;"
$env:ASPNETCORE_URLS = "http://localhost:5131"
$env:ASPNETCORE_ENVIRONMENT = "Development"
cd legacy/CommerceFlow.Legacy.Web
dotnet run --no-launch-profile
```

### Swagger

http://localhost:5131/swagger/index.html — only served when `ASPNETCORE_ENVIRONMENT=Development`.

## Running ErpExportJob

One-shot, Task-Scheduler-style: polls once for orders with `IsExportedToErp = 0`, processes
whatever it finds, and exits. **Not** a long-running daemon — safe to run from a third terminal
without leaving anything behind.

```powershell
$env:COMMERCEFLOW_DB_CONNECTION = "Server=localhost,14330;Database=CommerceFlowDb;User Id=sa;Password=<YOUR_LOCAL_SA_PASSWORD>;TrustServerCertificate=True;"
$env:COMMERCEFLOW_ERP_URL = "http://localhost:5296"
cd legacy/CommerceFlow.Legacy.ErpExportJob
dotnet run
```

## Build and test

```powershell
dotnet build

# No Docker needed:
cd tests/CommerceFlow.ArchitectureTests; dotnet test; cd ../..
cd tests/CommerceFlow.Api.Tests; dotnet test; cd ../..

# Needs Docker running -- Testcontainers spins up its own ephemeral SQL Server
# container for the test run and disposes it afterward automatically:
cd tests/CommerceFlow.Legacy.CharacterizationTests; dotnet test; cd ../..
```

## Local environment variables

| Variable | Used by | Purpose | Default if unset |
|---|---|---|---|
| `COMMERCEFLOW_DB_CONNECTION` | Migrations, Web, ErpExportJob | SQL Server connection string | A `Trusted_Connection=True` fallback — **not usable** with the Docker setup above; set it explicitly |
| `CommerceFlowEnvironment` | Migrations (`reset-data` only) | Safety gate; must be `Development` or `Test` | Unset → `reset-data` refuses to run |
| `COMMERCEFLOW_ERP_URL` | ErpExportJob | Where to find FakeErp | `http://localhost:5296` |
| `ASPNETCORE_ENVIRONMENT` | Web, FakeErp | Enables Swagger on Web when `Development` | Unset unless you set it |

## Example connection string

```text
Server=localhost,14330;Database=CommerceFlowDb;User Id=sa;Password=<YOUR_LOCAL_SA_PASSWORD>;TrustServerCertificate=True;
```

Never a real password in source, commits, or this file — always the placeholder above.

## Safe shutdown

```powershell
# Ctrl+C in the Web API and FakeErp terminals
docker stop commerceflow-sql
docker rm commerceflow-sql   # optional — only if you want the container gone entirely, not just stopped
```

## Further reading

- [`docs/roadmap.md`](docs/roadmap.md) — M0–M9 milestones
- [`docs/acceptance-criteria.md`](docs/acceptance-criteria.md) — AC1–AC11, frozen text and verification status
- [`docs/legacy-technical-debt.md`](docs/legacy-technical-debt.md) — what's intentionally rough, and why
- [`docs/architecture/m0-legacy-flow.md`](docs/architecture/m0-legacy-flow.md) — request/data flow diagrams for the current M0 baseline
