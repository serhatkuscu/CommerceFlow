using CommerceFlow.Database.Migrations;

if (args.Length == 0)
{
    return Usage();
}

var connectionString = Environment.GetEnvironmentVariable("COMMERCEFLOW_DB_CONNECTION")
    ?? "Server=localhost;Database=CommerceFlowDb;Trusted_Connection=True;TrustServerCertificate=True;";

try
{
    return args[0] switch
    {
        "migrate" => MigrationRunner.Migrate(connectionString) ? 0 : 1,
        "reset-data" => MigrationRunner.ResetData(connectionString) ? 0 : 1,
        "list-scripts" => ListScripts(),
        _ => Usage()
    };
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int Usage()
{
    Console.Error.WriteLine("Usage: migrate | reset-data | list-scripts");
    return 1;
}

static int ListScripts()
{
    Console.WriteLine("Migrations:");
    foreach (var name in MigrationRunner.ListMigrationScripts())
        Console.WriteLine($"  {name}");

    Console.WriteLine("SeedData:");
    foreach (var name in MigrationRunner.ListSeedDataScripts())
        Console.WriteLine($"  {name}");

    return 0;
}
