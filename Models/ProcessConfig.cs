namespace KonciliaPruebas.Models;

/// <summary>
/// Configuración dinámica inyectada por proceso.
/// Todos los selectores y URLs son parametrizables para escalar a N procesos distintos.
/// Las credenciales se leen de appsettings.json y se inyectan en el backend (nunca viajan desde el frontend).
/// </summary>
public class ProcessConfig
{
    /// <summary>Nombre descriptivo del proceso (ej: "Reproceso Cartera Vigente")</summary>
    public required string NombreProceso { get; set; }

    /// <summary>Fecha para filtrar el reproceso (formato yyyy-MM-dd)</summary>
    public string Fecha { get; set; } = string.Empty;

    /// <summary>URL de la aplicación web donde se lanza el reproceso</summary>
    public required string UrlDestino { get; set; }

    /// <summary>URL del tablero de control para monitorear el estado del reproceso</summary>
    public required string UrlTableroControl { get; set; }

    /// <summary>Nombre de la columna del Excel a evaluar (regla de validación)</summary>
    public required string ReglaValidacion { get; set; }

    // ─── Selectores CSS parametrizables ───

    /// <summary>Selector CSS del campo de fecha en la página destino</summary>
    public string SelectorFecha { get; set; } = "input[type='date']";

    /// <summary>Selector CSS del botón que dispara el reproceso</summary>
    public string SelectorBotonReproceso { get; set; } = "button#btnReprocesar";

    /// <summary>Selector CSS del botón de exportar/descargar Excel</summary>
    public string SelectorBotonExportar { get; set; } = "button#btnExportar";

    /// <summary>Texto que indica estado exitoso en el tablero (ej: "OK", "Completado")</summary>
    public string TextoEstadoOk { get; set; } = "OK";

    // ─── Timeouts ───

    /// <summary>Timeout máximo de polling al tablero en segundos (default: 300 = 5 min)</summary>
    public int TimeoutPollingSegundos { get; set; } = 300;

    /// <summary>Intervalo de recarga del tablero en milisegundos (default: 5000 = 5s)</summary>
    public int IntervaloPollingMs { get; set; } = 5000;
}
