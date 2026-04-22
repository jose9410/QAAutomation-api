namespace QAAutomation.Api.Models;

/// <summary>
/// Representa un sub-proceso dentro de una aplicación.
/// El ProcessId es el GUID real del valor del option en el dropdown de la página.
/// </summary>
public class SubProcesoConfig
{
    public required string Nombre { get; set; }

    /// <summary>GUID del valor del &lt;option&gt; en select#ProcessId</summary>
    public required string ProcessId { get; set; }

    public int Anio { get; set; }
    public int Mes { get; set; }
    public int Dia { get; set; }
}

/// <summary>
/// Configuración de una aplicación web que contiene múltiples sub-procesos.
/// Los selectores corresponden a los IDs reales del HTML inspeccionado.
/// </summary>
public class AplicacionConfig
{
    public required string Nombre { get; set; }

    /// <summary>URL de entrada al módulo de reproceso</summary>
    public required string UrlEntrada { get; set; }

    // ─── Selectores CSS reales (inspeccionados con F12) ───

    /// <summary>select#SelectedYear</summary>
    public string SelectorAnio { get; set; } = "select#SelectedYear";

    /// <summary>select#SelectedMonth</summary>
    public string SelectorMes { get; set; } = "select#SelectedMonth";

    /// <summary>a.NormalDay — links de días del calendario visual</summary>
    public string SelectorDia { get; set; } = "a.NormalDay";

    /// <summary>select#ProcessId — dropdown de proceso (onchange=submit)</summary>
    public string SelectorProceso { get; set; } = "select#ProcessId";

    /// <summary>a#startProcess — botón final de inicio</summary>
    public string SelectorBotonIniciar { get; set; } = "a#startProcess";

    /// <summary>Milisegundos de espera entre pasos del flujo</summary>
    public int EsperaEntrePassosMs { get; set; } = 1500;

    /// <summary>Timeout máximo en segundos por cada navegación/espera</summary>
    public int TimeoutNavegacionMs { get; set; } = 30_000;

    /// <summary>Sub-procesos disponibles dentro de esta aplicación</summary>
    public List<SubProcesoConfig> SubProcesos { get; set; } = new();
}
