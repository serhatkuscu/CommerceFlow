using System.Data;
using CommerceFlow.Modules.Inventory.Application;
using CommerceFlow.Modules.Inventory.Domain;
using Microsoft.Data.SqlClient;

namespace CommerceFlow.Modules.Inventory.Infrastructure;

// Read-only on purpose: this slice proves Stock can be built from the real dbo.Products row, it
// does not give anything authority to change that row. Direct ADO.NET against the same table
// usp_CreateOrder already owns -- no reference to Legacy.DAL/BLL/Web, same "share the data
// contract, not the code" approach OrderQueries/OrderWriter already established.
public sealed class StockReader(string connectionString) : IStockReader
{
    public async Task<Stock?> GetByProductIdAsync(int productId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(
            "SELECT StockQuantity FROM dbo.Products WHERE ProductId = @ProductId", connection)
        {
            CommandType = CommandType.Text
        };
        command.Parameters.Add("@ProductId", SqlDbType.Int).Value = productId;

        await connection.OpenAsync(ct);
        var result = await command.ExecuteScalarAsync(ct);
        return result is null ? null : Stock.Create(productId, (int)result);
    }
}
