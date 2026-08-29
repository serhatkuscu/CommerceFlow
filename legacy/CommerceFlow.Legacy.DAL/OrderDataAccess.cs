using System.Data;
using CommerceFlow.Legacy.Models;
using Microsoft.Data.SqlClient;

namespace CommerceFlow.Legacy.DAL;

// Raw ADO.NET against usp_CreateOrder / usp_GetOrderById, matched column-for-column and
// parameter-for-parameter with database/CommerceFlow.Database.Migrations/Scripts/Migrations.
// No interface, no DI container -- BLL news this up directly (see CommerceFlow.Legacy.BLL).
public class OrderDataAccess
{
    private const int DeadlockErrorNumber = 1205;
    private const int MaxAttempts = 3;

    private readonly string _connectionString;

    public OrderDataAccess(string? connectionString = null)
    {
        _connectionString = connectionString
            ?? Environment.GetEnvironmentVariable("COMMERCEFLOW_DB_CONNECTION")
            ?? "Server=localhost;Database=CommerceFlowDb;Trusted_Connection=True;TrustServerCertificate=True;";
    }

    public int CreateOrder(int customerId, IReadOnlyList<OrderLineRequest> items)
    {
        var itemsTable = new DataTable();
        itemsTable.Columns.Add("ProductId", typeof(int));
        itemsTable.Columns.Add("Quantity", typeof(int));
        foreach (var item in items)
        {
            itemsTable.Rows.Add(item.ProductId, item.Quantity);
        }

        // Retry mechanics (attempt counting, backoff, give-up) live in RetryPolicy and are
        // covered by RetryPolicyTests.cs against a fake exception -- not against a real deadlock.
        // What's proved here, by inspection, is just this one line: SqlException.Number == 1205
        // (deadlock victim) is the only thing considered retryable. Canonical ProductId-ascending
        // lock order in usp_CreateOrder makes hitting this rare, not impossible. Bounded retry,
        // not Polly -- Polly is reserved for the ERP integration story.
        return RetryPolicy.Execute(
            operation: () => ExecuteCreateOrder(customerId, itemsTable),
            isRetryable: ex => ex is SqlException { Number: DeadlockErrorNumber },
            maxAttempts: MaxAttempts,
            backoff: attempt => Thread.Sleep(50 * attempt));
    }

    private int ExecuteCreateOrder(int customerId, DataTable itemsTable)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("usp_CreateOrder", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = customerId });

        var itemsParameter = command.Parameters.Add(new SqlParameter("@Items", SqlDbType.Structured));
        itemsParameter.TypeName = "dbo.OrderItemTableType";
        itemsParameter.Value = itemsTable;

        var orderIdParameter = new SqlParameter("@OrderId", SqlDbType.Int) { Direction = ParameterDirection.Output };
        command.Parameters.Add(orderIdParameter);

        try
        {
            connection.Open();
            command.ExecuteNonQuery();
            return (int)orderIdParameter.Value;
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and <= 51004)
        {
            // The sproc already built the final, specific message (includes the relevant
            // product/customer id) -- relayed verbatim, never parsed.
            throw new BusinessRuleException(ex.Number, ex.Message);
        }
    }

    public Order? GetOrderById(int orderId)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("usp_GetOrderById", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add(new SqlParameter("@OrderId", SqlDbType.Int) { Value = orderId });

        connection.Open();
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        var order = new Order
        {
            OrderId = reader.GetInt32(reader.GetOrdinal("OrderId")),
            CustomerId = reader.GetInt32(reader.GetOrdinal("CustomerId")),
            CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
            CustomerEmail = reader.GetString(reader.GetOrdinal("CustomerEmail")),
            OrderStatus = reader.GetInt32(reader.GetOrdinal("OrderStatus")),
            TotalAmount = reader.GetDecimal(reader.GetOrdinal("TotalAmount")),
            IsExportedToErp = reader.GetBoolean(reader.GetOrdinal("IsExportedToErp")),
            ExportedDate = reader.IsDBNull(reader.GetOrdinal("ExportedDate"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("ExportedDate")),
            ErpExportAttempts = reader.GetInt32(reader.GetOrdinal("ErpExportAttempts")),
            CreatedDate = reader.GetDateTime(reader.GetOrdinal("CreatedDate"))
        };

        reader.NextResult();
        while (reader.Read())
        {
            order.Items.Add(new OrderItem
            {
                OrderItemId = reader.GetInt32(reader.GetOrdinal("OrderItemId")),
                OrderId = reader.GetInt32(reader.GetOrdinal("OrderId")),
                ProductId = reader.GetInt32(reader.GetOrdinal("ProductId")),
                Quantity = reader.GetInt32(reader.GetOrdinal("Quantity")),
                UnitPrice = reader.GetDecimal(reader.GetOrdinal("UnitPrice")),
                LineTotal = reader.GetDecimal(reader.GetOrdinal("LineTotal"))
            });
        }

        return order;
    }
}
