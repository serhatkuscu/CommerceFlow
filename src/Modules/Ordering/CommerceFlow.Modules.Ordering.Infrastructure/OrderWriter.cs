using System.Data;
using System.Diagnostics;
using CommerceFlow.Modules.Ordering.Application;
using Microsoft.Data.SqlClient;

namespace CommerceFlow.Modules.Ordering.Infrastructure;

// Calls dbo.usp_CreateOrder directly -- same technique CommerceFlow.Legacy.DAL.OrderDataAccess
// already uses (structured @Items parameter typed as dbo.OrderItemTableType, @OrderId as an
// output parameter), but with its own ADO.NET code: this project cannot reference Legacy.DAL, so
// sharing the data contract (the stored procedure, the table type) is the point, sharing the C#
// code is not. Deadlock retry (SqlException 1205) deliberately stays out of scope here -- that
// policy lives in Legacy.DAL, off-limits to reference, and re-implementing it is a separate
// concern this slice was not asked to take on.
public sealed class OrderWriter(string connectionString) : IOrderWriter
{
    public async Task<CreateOrderResult> CreateAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var itemsTable = new DataTable();
        itemsTable.Columns.Add("ProductId", typeof(int));
        itemsTable.Columns.Add("Quantity", typeof(int));
        foreach (var line in request.Items ?? [])
        {
            itemsTable.Rows.Add(line.ProductId, line.Quantity);
        }

        await using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand("dbo.usp_CreateOrder", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = request.CustomerId;

        var itemsParameter = command.Parameters.Add("@Items", SqlDbType.Structured);
        itemsParameter.TypeName = "dbo.OrderItemTableType";
        itemsParameter.Value = itemsTable;

        var orderIdParameter = command.Parameters.Add("@OrderId", SqlDbType.Int);
        orderIdParameter.Direction = ParameterDirection.Output;

        try
        {
            await connection.OpenAsync(ct);
            await command.ExecuteNonQueryAsync(ct);
            return CreateOrderResult.Success((int)orderIdParameter.Value);
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and <= 51004)
        {
            // usp_CreateOrder already built the final, specific message (includes the relevant
            // product/customer id) -- relayed verbatim, never parsed or rewritten, exactly like
            // the legacy DAL does for the same error range.
            return CreateOrderResult.Failure(MapError(ex.Number), ex.Message);
        }
    }

    private static OrderCreationError MapError(int sqlErrorNumber) => sqlErrorNumber switch
    {
        51000 => OrderCreationError.CustomerNotFound,
        51001 => OrderCreationError.ProductNotFound,
        51002 => OrderCreationError.InsufficientStock,
        51003 => OrderCreationError.EmptyOrder,
        51004 => OrderCreationError.InvalidQuantity,
        _ => throw new UnreachableException(),
    };
}
