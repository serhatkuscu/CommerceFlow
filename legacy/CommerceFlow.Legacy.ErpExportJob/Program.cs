using System.Net.Http.Json;
using Microsoft.Data.SqlClient;

// Deliberately does NOT reference CommerceFlow.Legacy.DAL or .BLL, and does not share a model
// type with the rest of the app -- its own hand-rolled ADO.NET query/update code and its own
// local record, independently maintained. This is debt item #12: no shared library existed
// between the web app and the batch export job, so nobody wrote one. Task-Scheduler style:
// polls once, processes whatever is pending, and exits -- not a long-running daemon.

var connectionString = Environment.GetEnvironmentVariable("COMMERCEFLOW_DB_CONNECTION")
    ?? "Server=localhost;Database=CommerceFlowDb;Trusted_Connection=True;TrustServerCertificate=True;";
var erpBaseUrl = Environment.GetEnvironmentVariable("COMMERCEFLOW_ERP_URL")
    ?? "http://localhost:5296";

using var httpClient = new HttpClient { BaseAddress = new Uri(erpBaseUrl) };

var pendingOrders = GetPendingOrders(connectionString);
Console.WriteLine($"Found {pendingOrders.Count} order(s) pending ERP export.");

foreach (var order in pendingOrders)
{
    try
    {
        var response = await httpClient.PostAsJsonAsync("/api/erp/export", new
        {
            orderId = order.OrderId,
            customerId = order.CustomerId,
            totalAmount = order.TotalAmount
        });

        if (response.IsSuccessStatusCode)
        {
            MarkExported(connectionString, order.OrderId);
            Console.WriteLine($"Order {order.OrderId}: exported.");
        }
        else
        {
            // No cap, no backoff -- ErpExportAttempts grows unboundedly on repeated failure.
            // Known bug (AC7), characterized as-is here; fixed properly in M5 with real
            // resilience (Polly retry/circuit breaker) once a genuinely flaky ERP exists.
            IncrementAttempts(connectionString, order.OrderId);
            Console.WriteLine($"Order {order.OrderId}: ERP returned {(int)response.StatusCode}, attempt recorded.");
        }
    }
    catch (HttpRequestException ex)
    {
        IncrementAttempts(connectionString, order.OrderId);
        Console.WriteLine($"Order {order.OrderId}: ERP call failed ({ex.Message}), attempt recorded.");
    }
}

static List<PendingOrder> GetPendingOrders(string connectionString)
{
    var results = new List<PendingOrder>();
    using var connection = new SqlConnection(connectionString);
    using var command = new SqlCommand(
        "SELECT OrderId, CustomerId, TotalAmount FROM dbo.Orders WHERE IsExportedToErp = 0",
        connection);

    connection.Open();
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        results.Add(new PendingOrder(reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2)));
    }

    return results;
}

static void MarkExported(string connectionString, int orderId)
{
    using var connection = new SqlConnection(connectionString);
    using var command = new SqlCommand(
        "UPDATE dbo.Orders SET IsExportedToErp = 1, ExportedDate = SYSUTCDATETIME() WHERE OrderId = @OrderId",
        connection);
    command.Parameters.AddWithValue("@OrderId", orderId);

    connection.Open();
    command.ExecuteNonQuery();
}

static void IncrementAttempts(string connectionString, int orderId)
{
    using var connection = new SqlConnection(connectionString);
    using var command = new SqlCommand(
        "UPDATE dbo.Orders SET ErpExportAttempts = ErpExportAttempts + 1 WHERE OrderId = @OrderId",
        connection);
    command.Parameters.AddWithValue("@OrderId", orderId);

    connection.Open();
    command.ExecuteNonQuery();
}

internal record PendingOrder(int OrderId, int CustomerId, decimal TotalAmount);
