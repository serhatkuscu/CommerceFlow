using Microsoft.Data.SqlClient;

namespace CommerceFlow.Legacy.CharacterizationTests;

// Direct DB-state assertions the tests need that OrderDataAccess has no reason to expose
// (e.g. a raw stock quantity or a total order count) -- deliberately separate from the DAL
// under test, so a bug in OrderDataAccess can't hide itself from its own verification.
internal static class TestDb
{
    public static int GetStockQuantity(string connectionString, int productId)
    {
        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand("SELECT StockQuantity FROM dbo.Products WHERE ProductId = @ProductId", connection);
        command.Parameters.AddWithValue("@ProductId", productId);
        connection.Open();
        return (int)command.ExecuteScalar();
    }

    public static int GetOrderCount(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Orders", connection);
        connection.Open();
        return (int)command.ExecuteScalar();
    }

    public static int GetOrderItemCount(string connectionString, int orderId)
    {
        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.OrderItems WHERE OrderId = @OrderId", connection);
        command.Parameters.AddWithValue("@OrderId", orderId);
        connection.Open();
        return (int)command.ExecuteScalar();
    }

    public static (int CustomerId1, int CustomerId2, int CustomerId3) GetSeededCustomerIds(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand("SELECT CustomerId FROM dbo.Customers ORDER BY CustomerId", connection);
        connection.Open();
        using var reader = command.ExecuteReader();
        var ids = new List<int>();
        while (reader.Read())
        {
            ids.Add(reader.GetInt32(0));
        }

        return (ids[0], ids[1], ids[2]);
    }
}
