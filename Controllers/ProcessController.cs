using QAAutomation.Api.Models;
using QAAutomation.Api.Services;
using Microsoft.AspNetCore.Mvc;

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
        _jobManager = jobManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Starts an automation process in the background.
    /// Returns a JobId immediately to avoid HTTP timeouts.
    /// </summary>
    [HttpPost("start")]
    public IActionResult Start([FromBody] ProcessStartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NombreAplicacion))
            return BadRequest(new { Error = "NombreAplicacion is required" });

        var job = _jobManager.CreateJob(request.NombreAplicacion);

        _logger.LogInformation(
            "Job {JobId} created for Application: {App}",
            job.JobId, request.NombreAplicacion);

        // ─── Detached Background Worker ───
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var playwrightService = scope.ServiceProvider
                    .GetRequiredService<PlaywrightService>();

                await playwrightService.ExecuteProcessAsync(request, job.JobId);
                _logger.LogInformation("✅ Job {JobId} completed successfully", job.JobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Job {JobId} failed: {Message}", job.JobId, ex.Message);
                _jobManager.UpdateJob(job.JobId, j =>
                {
                    j.Status = JobStatus.Error;
                    j.Mensaje = "Error in automation process";
                    j.ErrorDetalle = ex.Message;
                    j.FinalizadoEn = DateTime.Now;
                });
            }
        });

        return Ok(new
        {
            job.JobId,
            Message = "Process started in background",
            Status = job.Status.ToString()
        });
    }

    /// <summary>
    /// Consulta el estado actual de un job por su ID.
    /// El frontend hace polling a este endpoint cada 2 segundos.
    /// </summary>
    [HttpGet("status/{jobId}")]
    public IActionResult GetStatus(string jobId)
    {
        var job = _jobManager.GetJob(jobId);
        if (job == null)
            return NotFound(new { Error = $"Job '{jobId}' no encontrado" });

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
            nombre = a.Nombre,
            subProcesos = a.SubProcesos.Select(p => new
            {
                nombre = p.Nombre,
                processId = p.ProcessId
            }).ToList()
        }).ToList();

        return Ok(dto);
    }
}
