using CommerceFlow.Api;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Correlation ID runs before EVERYTHING else, including Swagger and HTTPS redirection -- both
// of those can short-circuit the pipeline (produce a response without calling next()), so any
// middleware placed after them would simply never run for those requests. Every HTTP request
// gets the same observability behavior; there is no "this route doesn't count" exception.
app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Comes right after correlation ID so the request-completion log line carries the same ID.
// Central exception handling (a later slice) will also sit between these two.
app.UseSerilogRequestLogging();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory<Program> in test projects.
public partial class Program;
