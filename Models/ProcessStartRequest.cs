namespace QAAutomation.Api.Models;

/// <summary>
/// DTO que el frontend envía al POST /api/process/start.
/// Contiene la aplicación, el sub-proceso y la fecha seleccionada por el usuario.
/// </summary>
public class ProcessStartRequest
{
    /// <summary>Nombre de la aplicación (ej: "MiAplicacion")</summary>
    public required string NombreAplicacion { get; set; }
}
