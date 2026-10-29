using CommerceFlow.Api;
using CommerceFlow.Modules.Ordering.Application;
using CommerceFlow.Modules.Ordering.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddControllers();
builder.Services.AddScoped<IOrderQueries>(_ => new OrderQueries(
    builder.Configuration["COMMERCEFLOW_DB_CONNECTION"]
    ?? throw new InvalidOperationException("COMMERCEFLOW_DB_CONNECTION must be configured.")));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Correlation ID runs before EVERYTHING else, including Swagger and HTTPS redirection -- both
// of those can short-circuit the pipeline (produce a response without calling next()), so any
// middleware placed after them would simply never run for those requests. Every HTTP request
// gets the same observability behavior; there is no "this route doesn't count" exception.
app.UseMiddleware<CorrelationIdMiddleware>();

// Right after correlation ID, and before everything else (including Swagger), so the exception
// handler's error log and Problem Details response can use the same correlation ID no matter
// where in the pipeline something goes wrong.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Comes after correlation ID and exception handling so the request-completion log line carries
// the same correlation ID either way.
app.UseSerilogRequestLogging();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory<Program> in test projects.
public partial class Program;
