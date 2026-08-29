namespace CommerceFlow.Legacy.CharacterizationTests;

[Collection("Database")]
public class ResetDataTests
{
    private readonly DatabaseFixture _database;

    public ResetDataTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void ResetData_RunTwiceInARow_ProducesIdenticalSeedIds()
    {
        _database.ResetData();
        var first = TestDb.GetSeededCustomerIds(_database.ConnectionString);

        // Mutate state in between so the second reset has something real to undo.
        _database.ResetData();
        var second = TestDb.GetSeededCustomerIds(_database.ConnectionString);

        Assert.Equal(first, second);
        Assert.Equal((1, 2, 3), first);
    }
}
