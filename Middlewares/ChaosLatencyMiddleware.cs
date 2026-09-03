using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace QAAutomation.Api.Middlewares;

public class ChaosLatencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ChaosLatencyMiddleware> _logger;

    public ChaosLatencyMiddleware(RequestDelegate next, ILogger<ChaosLatencyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var isChaosEnabled = Environment.GetEnvironmentVariable("CHAOS_LATENCY_ENABLED") == "true";

        if (isChaosEnabled)
        {
            var latencyStr = Environment.GetEnvironmentVariable("CHAOS_LATENCY_MS");
            if (!int.TryParse(latencyStr, out var latencyMs))
            {
                latencyMs = 600;
            }

            var activity = Activity.Current;
            activity?.SetTag("chaos.injected", "true");
            activity?.SetTag("chaos.latency_ms", latencyMs);
            activity?.SetTag("chaos.experiment", "latency_600ms_service_b");

            _logger.LogWarning("🔥 [CHAOS INJECTION] Inyectando {LatencyMs}ms de latencia artificial en {Path} | TraceId: {TraceId}",
                latencyMs, context.Request.Path, activity?.TraceId.ToString() ?? "N/A");

            await Task.Delay(latencyMs);
        }

        await _next(context);
    }
}
