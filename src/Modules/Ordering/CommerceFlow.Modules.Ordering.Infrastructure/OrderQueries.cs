using System.Data;
using CommerceFlow.Modules.Ordering.Application;
using Microsoft.Data.SqlClient;

namespace CommerceFlow.Modules.Ordering.Infrastructure;

public sealed class OrderQueries(string connectionString) : IOrderQueries
{
    public async Task<OrderDetailsDto?> GetByIdAsync(int orderId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand("dbo.usp_GetOrderById", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@OrderId", SqlDbType.Int).Value = orderId;
        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var order = new OrderDetailsDto(
            reader.GetInt32(reader.GetOrdinal("OrderId")),
            reader.GetInt32(reader.GetOrdinal("OrderStatus")),
            reader.GetDecimal(reader.GetOrdinal("TotalAmount")), []);

        if (!await reader.NextResultAsync(ct))
            throw new DataException("usp_GetOrderById did not return its order items result set.");

        while (await reader.ReadAsync(ct))
        {
            order.Items.Add(new OrderItemDto(
                reader.GetInt32(reader.GetOrdinal("ProductId")),
                reader.GetInt32(reader.GetOrdinal("Quantity")),
                reader.GetDecimal(reader.GetOrdinal("UnitPrice")),
                reader.GetDecimal(reader.GetOrdinal("LineTotal"))));
        }
        return order;
    }
}
