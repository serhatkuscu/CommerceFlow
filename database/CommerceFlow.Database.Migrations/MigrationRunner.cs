using System.Reflection;
using DbUp;
using DbUp.Helpers;

namespace CommerceFlow.Database.Migrations;

public static class MigrationRunner
{
    public static IReadOnlyList<string> ListMigrationScripts() =>
        [.. Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(name => name.Contains(".Scripts.Migrations."))
            .OrderBy(name => name, StringComparer.Ordinal)];

    public static IReadOnlyList<string> ListSeedDataScripts() =>
        [.. Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(name => name.Contains(".Scripts.SeedData."))
            .OrderBy(name => name, StringComparer.Ordinal)];

    public static bool Migrate(string connectionString)
    {
        EnsureDatabase.For.SqlDatabase(connectionString);

        var result = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                script => script.Contains(".Scripts.Migrations."))
            .LogToConsole()
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
        {
            Console.Error.WriteLine(result.Error);
        }

        return result.Successful;
    }

    // Destroys and deterministically re-seeds data. Never runs unless the caller
    // has explicitly opted into a non-production environment.
    public static bool ResetData(string connectionString)
    {
        var environment = Environment.GetEnvironmentVariable("CommerceFlowEnvironment");
        var allowed = string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "Test", StringComparison.OrdinalIgnoreCase);

        if (!allowed)
        {
            throw new InvalidOperationException(
                "reset-data refused: CommerceFlowEnvironment must be 'Development' or 'Test' " +
                $"(was '{environment ?? "<unset>"}'). This operation destroys data and must never run against production.");
        }

        if (!Migrate(connectionString))
        {
            return false;
        }

        // Not journaled: seed scripts are meant to re-run every time reset-data is invoked.
        var result = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                script => script.Contains(".Scripts.SeedData."))
            .JournalTo(new NullJournal())
            .LogToConsole()
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
        {
            Console.Error.WriteLine(result.Error);
        }

        return result.Successful;
    }
}
