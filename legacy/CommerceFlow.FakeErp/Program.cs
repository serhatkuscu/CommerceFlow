// Stands in for the real external ERP system that CommerceFlow.Legacy.ErpExportJob calls.
// Deliberately boringly reliable in M0 -- always succeeds. M5 introduces configurable
// flakiness (timeouts, duplicates, partial failures) once resilience is actually the subject
// under test; conflating that with this milestone would blur what each is characterizing.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost("/api/erp/export", (ErpExportRequest request) =>
{
    Console.WriteLine($"[FakeErp] Received export for OrderId={request.OrderId}, TotalAmount={request.TotalAmount}");
    return Results.Ok(new ErpExportResponse(true, Guid.NewGuid().ToString()));
});

app.Run();

public record ErpExportRequest(int OrderId, int CustomerId, decimal TotalAmount);

public record ErpExportResponse(bool Received, string ErpReferenceId);

public partial class Program;
