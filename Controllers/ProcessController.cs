using QAAutomation.Api.Models;
using QAAutomation.Api.Observability;
using QAAutomation.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace QAAutomation.Api.Controllers;

/// <summary>
/// API REST para gestionar procesos de automatización.
/// POST /api/process/start       → Inicia proceso, retorna JobId inmediatamente
/// GET  /api/process/status/{id} → Consulta estado del job (polling)
/// GET  /api/process/jobs        → Lista todos los jobs (debug)
/// GET  /api/process/catalog     → Devuelve el catálogo de aplicaciones y sub-procesos
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProcessController : ControllerBase
{
    private readonly JobManager _jobManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProcessController> _logger;
    private readonly IConfiguration _configuration;

    public ProcessController(
        JobManager jobManager,
        IServiceScopeFactory scopeFactory,
        ILogger<ProcessController> logger,
        IConfiguration configuration)
    {
        _jobManager    = jobManager;
        _scopeFactory  = scopeFactory;
        _logger        = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Starts an automation process in the background.
    /// Returns a JobId immediately to avoid HTTP timeouts.
    ///
    /// ── Custom Span ─────────────────────────────────────────────────────────
    /// A "process.job_create" child span is started here using the service's
    /// static ActivitySource.  This makes the job-creation step visible as its
    /// own unit of work in Jaeger / X-Ray, separate from the parent HTTP span
    /// that ASP.NET Core instrumentation creates automatically.
    ///
    /// Semantic conventions followed:
    ///   - span kind  → Internal  (work happens within this service)
    ///   - job.id     → the newly assigned GUID
    ///   - app.name   → NombreAplicacion from the request body
    /// ── ─────────────────────────────────────────────────────────────────────
    /// </summary>
    [HttpPost("start")]
    public IActionResult Start([FromBody] ProcessStartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NombreAplicacion))
            return BadRequest(new { Error = "NombreAplicacion is required" });

        // ── Custom Span: process.job_create ──────────────────────────────────
        // ActivityKind.Internal = work contained within this service boundary.
        // The span is automatically parented to the active ASP.NET Core span.
        using var jobSpan = Telemetry.Source.StartActivity(
            "process.job_create",
            ActivityKind.Internal);

        var job = _jobManager.CreateJob(request.NombreAplicacion);

        // Enrich the span with business-meaningful attributes
        jobSpan?.SetTag("job.id",              job.JobId);
        jobSpan?.SetTag("job.app_name",        request.NombreAplicacion);
        jobSpan?.SetTag("job.initial_status",  job.Status.ToString());

        _logger.LogInformation(
            "Job {JobId} created for Application: {App}",
            job.JobId, request.NombreAplicacion);

        // ─── Detached Background Worker ───────────────────────────────────────
        // NOTE: the background Task runs OUTSIDE this span's lifetime by design.
        // The execution span (playwright automation) is a separate trace root,
        // which is intentional for long-running async jobs.
        _ = Task.Run(async () =>
        {
            // ── Custom Span: process.job_execute ─────────────────────────────
            // Starts a NEW root span (not linked to the HTTP request span).
            // This clearly separates "accepting the job" from "executing the job"
            // in the trace, which is correct for async fire-and-forget patterns.
            using var execSpan = Telemetry.Source.StartActivity(
                "process.job_execute",
                ActivityKind.Internal);

            execSpan?.SetTag("job.id",      job.JobId);
            execSpan?.SetTag("job.app_name", request.NombreAplicacion);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var playwrightService = scope.ServiceProvider
                    .GetRequiredService<PlaywrightService>();

                await playwrightService.ExecuteProcessAsync(request, job.JobId);

                execSpan?.SetStatus(ActivityStatusCode.Ok);
                _logger.LogInformation("✅ Job {JobId} completed successfully", job.JobId);
            }
            catch (Exception ex)
            {
                execSpan?.SetStatus(ActivityStatusCode.Error, ex.Message);
                // Record exception using OTel semantic conventions (no extension method needed)
                execSpan?.SetTag("exception.type",       ex.GetType().FullName);
                execSpan?.SetTag("exception.message",    ex.Message);
                execSpan?.SetTag("exception.stacktrace", ex.StackTrace);
                execSpan?.SetTag("job.error", ex.Message);

                _logger.LogError(ex, "❌ Job {JobId} failed: {Message}", job.JobId, ex.Message);

                _jobManager.UpdateJob(job.JobId, j =>
                {
                    j.Status       = JobStatus.Error;
                    j.Mensaje      = "Error in automation process";
                    j.ErrorDetalle = ex.Message;
                    j.FinalizadoEn = DateTime.Now;
                });
            }
        });

        return Ok(new
        {
            job.JobId,
            Message = "Process started in background",
            Status  = job.Status.ToString()
        });
    }

    /// <summary>
    /// Consulta el estado actual de un job por su ID.
    /// El frontend hace polling a este endpoint cada 2 segundos.
    /// </summary>
    [HttpGet("status/{jobId}")]
    public IActionResult GetStatus(string jobId)
    {
        // ── Custom Span: process.job_status_query ────────────────────────────
        using var span = Telemetry.Source.StartActivity(
            "process.job_status_query",
            ActivityKind.Internal);
        span?.SetTag("job.id", jobId);

        var job = _jobManager.GetJob(jobId);
        if (job == null)
        {
            span?.SetStatus(ActivityStatusCode.Error, "Job not found");
            return NotFound(new { Error = $"Job '{jobId}' no encontrado" });
        }

        span?.SetTag("job.status", job.Status.ToString());
        return Ok(job);
    }

    /// <summary>
    /// Lista todos los jobs registrados (útil para debug y administración).
    /// </summary>
    [HttpGet("jobs")]
    public IActionResult GetAllJobs()
    {
        return Ok(_jobManager.GetAllJobs());
    }

    /// <summary>
    /// Retorna el catálogo de aplicaciones y sub-procesos desde appsettings.json.
    /// GET /api/process/catalog
    /// Formato: [{ nombre, urlEntrada, subProcesos: [{ nombre, processId }] }]
    /// </summary>
    [HttpGet("catalog")]
    public IActionResult GetCatalog()
    {
        var aplicaciones = _configuration
            .GetSection("Aplicaciones")
            .Get<List<AplicacionConfig>>() ?? new List<AplicacionConfig>();

        // Devolver solo lo que necesita el frontend (sin selectores internos)
        var dto = aplicaciones.Select(a => new
        {
            nombre     = a.Nombre,
            subProcesos = a.SubProcesos.Select(p => new
            {
                nombre    = p.Nombre,
                processId = p.ProcessId
            }).ToList()
        }).ToList();

        return Ok(dto);
    }
}
