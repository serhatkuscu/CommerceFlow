using CommerceFlow.Database.Migrations;
using Testcontainers.MsSql;

namespace CommerceFlow.Legacy.CharacterizationTests;

// One SQL Server container for the whole test assembly -- spinning one up per test or per class
// is minutes of overhead for no benefit here. Combined with [CollectionBehavior
// (DisableTestParallelization = true)] in AssemblyInfo.cs, every DB-touching test runs
// sequentially against this single instance, and each test resets to a known state itself.
public class DatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Everything in the app reads its DB connection from this process environment variable,
        // not from IConfiguration -- matching what production actually does (see
        // database/README.md), so tests set up the same way rather than inventing a different
        // wiring mechanism just for testing.
        Environment.SetEnvironmentVariable("COMMERCEFLOW_DB_CONNECTION", ConnectionString);
        Environment.SetEnvironmentVariable("CommerceFlowEnvironment", "Test");

        MigrationRunner.Migrate(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public void ResetData() => MigrationRunner.ResetData(ConnectionString);
}
