using System.Collections.Concurrent;
using QAAutomation.Api.Models;

namespace QAAutomation.Api.Services;

/// <summary>
/// Gestión thread-safe de jobs en memoria usando ConcurrentDictionary.
/// Cada job representa una ejecución completa del flujo de automatización.
/// </summary>
public class JobManager
{
    private readonly ConcurrentDictionary<string, JobState> _jobs = new();

    /// <summary>Crea un nuevo job y lo almacena en memoria.</summary>
    public JobState CreateJob(string nombreProceso)
    {
        var job = new JobState
        {
            JobId = Guid.NewGuid().ToString("N")[..12],
            NombreProceso = nombreProceso,
            Status = JobStatus.Pendiente,
            Mensaje = "Job creado, esperando inicio..."
        };
        _jobs[job.JobId] = job;
        return job;
    }

    /// <summary>Actualiza el estado de un job existente de forma thread-safe.</summary>
    public void UpdateJob(string jobId, Action<JobState> updater)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            lock (job) // Lock a nivel de instancia para evitar escrituras concurrentes
            {
                updater(job);
            }
        }
    }

    /// <summary>Obtiene el estado actual de un job.</summary>
    public JobState? GetJob(string jobId)
        => _jobs.TryGetValue(jobId, out var job) ? job : null;

    /// <summary>Lista todos los jobs (para debug/admin).</summary>
    public IReadOnlyList<JobState> GetAllJobs()
        => _jobs.Values.OrderByDescending(j => j.IniciadoEn).ToList();
}
