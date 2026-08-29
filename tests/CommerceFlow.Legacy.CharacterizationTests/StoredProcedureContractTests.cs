using Microsoft.Data.SqlClient;

namespace CommerceFlow.Legacy.CharacterizationTests;

// Structural/contract test, NOT a behavioral test: proves the mechanism usp_CreateOrder relies
// on for deadlock avoidance (grouping stock updates by product and processing them in ascending
// ProductId order) is present in what's actually DEPLOYED in the database -- not just in the
// local .sql file, which could drift from what really got applied. This does not, and cannot,
// prove that canonical ordering prevents deadlocks at runtime -- see ConcurrencyTests.cs for why
// that specific claim isn't meaningfully runtime-testable. It only proves the mechanism exists
// in the object SQL Server is actually executing.
[Collection("Database")]
public class StoredProcedureContractTests
{
    private readonly DatabaseFixture _database;

    public StoredProcedureContractTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void UspCreateOrder_DeployedDefinition_GroupsAndOrdersStockUpdatesByAscendingProductId()
    {
        using var connection = new SqlConnection(_database.ConnectionString);
        using var command = new SqlCommand(
            "SELECT OBJECT_DEFINITION(OBJECT_ID('dbo.usp_CreateOrder'))", connection);
        connection.Open();
        var definition = (string)command.ExecuteScalar();

        // Whitespace-normalized so formatting changes (indentation, line breaks) don't break this
        // on every harmless edit -- it's checking for the SQL shape, not the exact layout.
        var normalized = string.Join(' ', definition.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains(
            "GROUP BY ProductId ORDER BY ProductId ASC",
            normalized,
            StringComparison.OrdinalIgnoreCase);
    }
}
