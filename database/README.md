# CommerceFlow.Database.Migrations

DbUp-based runner for the legacy schema. Scripts are embedded resources (`Scripts/Migrations`,
`Scripts/SeedData`), so behavior is identical whether invoked from the CLI, from tests, or from a
published build — nothing depends on the working directory.

## Commands

```
dotnet run -- migrate
dotnet run -- reset-data
```

- `migrate` applies `Scripts/Migrations/*.sql` in filename order, tracked in DbUp's journal table.
  Safe to re-run; already-applied scripts are skipped. This is what runs in production/CI.
  **Once a script has been applied anywhere, it is never edited — a change is always a new,
  higher-numbered script.**
- `reset-data` runs `migrate`, then deterministically clears and re-seeds `Scripts/SeedData/*.sql`.
  Not tracked by DbUp's journal — it re-runs in full every time. **Refuses to run unless the
  `CommerceFlowEnvironment` environment variable is exactly `Development` or `Test`.** Never run
  this against production.

## Connection string

Read from `COMMERCEFLOW_DB_CONNECTION`, falling back to a local default
(`Server=localhost;Database=CommerceFlowDb;Trusted_Connection=True;TrustServerCertificate=True;`).
