namespace QAAutomation.Api.Models;

/// <summary>
/// Estado de un job en memoria. Se actualiza progresivamente durante la ejecución
/// y se consulta vía polling desde el frontend.
/// </summary>
public class JobState
{
    public string JobId { get; set; } = string.Empty;
    public string NombreProceso { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Pendiente;
    public string Mensaje { get; set; } = string.Empty;
    public DateTime IniciadoEn { get; set; } = DateTime.Now;
    public DateTime? FinalizadoEn { get; set; }

    // ─── Resultados del análisis Excel ───
    public int TotalRegistros { get; set; }
    public int RegistrosOk { get; set; }
    public int RegistrosError { get; set; }
    public double PorcentajeExito { get; set; }
    public List<string> Detalle { get; set; } = new();
    public string? ErrorDetalle { get; set; }
}

/// <summary>
/// Estados posibles del flujo de automatización.
/// Se serializan como string en JSON (configurado en Program.cs).
/// </summary>
public enum JobStatus
{
    Pendiente,
    Iniciando,          // Paso 1: Navegando a la URL de entrada
    LanzandoReproceso,  // Pasos 2-4: Seleccionando Año/Mes/Día
    MonitoreandoTablero,// Pasos 5-6: Seleccionando proceso e iniciando
    DescargandoExcel,   // (reservado para futuro)
    ProcesandoExcel,    // (reservado para futuro)
    Completado,
    Error
}
