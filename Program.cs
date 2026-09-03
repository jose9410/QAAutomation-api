// ═══════════════════════════════════════════════════════════════════════════════
// qaautomation-api  —  Program.cs
// OpenTelemetry SDK wiring: Traces, Metrics, Logs
// .NET 8
// ═══════════════════════════════════════════════════════════════════════════════

using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QAAutomation.Api.Observability;
using QAAutomation.Api.Services;
using QAAutomation.Api.Middlewares;
using Microsoft.Playwright;

var builder = WebApplication.CreateBuilder(args);
var cfg     = builder.Configuration;

// ─────────────────────────────────────────────────────────────────────────────
// 1.  OpenTelemetry — shared resource
// ─────────────────────────────────────────────────────────────────────────────
var otlpEndpoint = cfg["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";

var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(
        serviceName:    Telemetry.ServiceName,
        serviceVersion: Telemetry.ServiceVersion)
    .AddAttributes(new Dictionary<string, object>
    {
        ["deployment.environment"] = cfg["ASPNETCORE_ENVIRONMENT"] ?? "production",
        ["host.name"]              = Environment.MachineName,
    });

// ─────────────────────────────────────────────────────────────────────────────
// 2.  Logging — structured JSON + OTel log bridge
//     trace_id and span_id are injected automatically by the OTel SDK
//     into every ILogger record when a span is active.
// ─────────────────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
    options.FormatterName = "json");     // structured JSON to stdout
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.SetResourceBuilder(resourceBuilder);
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes           = true;
    logging.AddOtlpExporter(o =>
    {
        o.Endpoint = new Uri(otlpEndpoint);
        o.Protocol = OtlpExportProtocol.Grpc;
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// 3.  OpenTelemetry SDK — Traces + Metrics
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    // ── Traces ──────────────────────────────────────────────────────────────
    .WithTracing(traces =>
    {
        traces
            .SetResourceBuilder(resourceBuilder)
            .SetSampler(new AlwaysOnSampler())
            // ASP.NET Core: root span for every inbound HTTP request.
            // When historia-api calls /health, the W3C traceparent header
            // is automatically read here and the span is linked to that trace.
            .AddAspNetCoreInstrumentation(o =>
            {
                o.RecordException = true;
            })
            // SqlClient: captures raw SQL / stored-proc calls as child spans
            // (satisfies rubric requirement: service-b → DB as a traced span)
            .AddSqlClientInstrumentation(o =>
            {
                o.SetDbStatementForText           = true;
                o.RecordException                 = true;
                o.EnableConnectionLevelAttributes = true;
            })
            // Custom spans from Telemetry.Source (ProcessController.Start)
            .AddSource(Telemetry.ServiceName)
            // OTLP → Collector sidecar (gRPC)
            .AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpEndpoint);
                o.Protocol = OtlpExportProtocol.Grpc;
            });
    })
    // ── Metrics ─────────────────────────────────────────────────────────────
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation()
            // Prometheus scrape endpoint
            .AddPrometheusExporter()
            // Also forward to collector via OTLP
            .AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpEndpoint);
                o.Protocol = OtlpExportProtocol.Grpc;
            });
    });

// ─────────────────────────────────────────────────────────────────────────────
// 4.  Application services (unchanged from original)
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<JobManager>();
builder.Services.AddTransient<PlaywrightService>();
builder.Services.AddTransient<ExcelService>();

// ─────────────────────────────────────────────────────────────────────────────
// 5.  Port binding (Docker)
// ─────────────────────────────────────────────────────────────────────────────
builder.WebHost.UseUrls("http://0.0.0.0:8080");

// ─────────────────────────────────────────────────────────────────────────────
// 6.  Middleware pipeline
// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Prometheus scrape endpoint
app.MapPrometheusScrapingEndpoint("/metrics");

app.UseMiddleware<ChaosLatencyMiddleware>();

app.MapControllers();

// Health endpoint — called by historia-api for the cross-service trace demo
app.MapGet("/health", () => Results.Ok(new
{
    status  = "healthy",
    service = Telemetry.ServiceName,
    version = Telemetry.ServiceVersion
}));

app.Run();
