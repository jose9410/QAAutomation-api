using ClosedXML.Excel;

namespace QAAutomation.Api.Services;

/// <summary>
/// Servicio de procesamiento de Excel con ClosedXML (sin necesidad de Office instalado).
/// Lee el archivo, evalúa la columna indicada por ReglaValidacion,
/// calcula porcentajes de éxito/error y elimina el archivo físico.
/// </summary>
public class ExcelService
{
    private readonly ILogger<ExcelService> _logger;

    public ExcelService(ILogger<ExcelService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analiza un archivo Excel buscando la columna especificada.
    /// Clasifica cada fila como OK o Error según su valor.
    /// </summary>
    /// <param name="filePath">Ruta del archivo Excel descargado</param>
    /// <param name="columnaValidacion">Nombre de la columna a evaluar</param>
    /// <returns>Resultado con conteos y porcentajes</returns>
    public ExcelResult Analyze(string filePath, string columnaValidacion)
    {
        _logger.LogInformation("Analizando Excel: {Path}, Columna: '{Columna}'",
            filePath, columnaValidacion);

        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheet(1); // Primera hoja

        // ─── Buscar la columna por nombre en la fila de encabezados ───
        var headerRow = worksheet.FirstRowUsed()
            ?? throw new InvalidOperationException("El archivo Excel está vacío.");

        int colIndex = -1;
        string colEncontrada = string.Empty;

        foreach (var cell in headerRow.CellsUsed())
        {
            var headerText = cell.GetString().Trim();
            if (headerText.Equals(columnaValidacion, StringComparison.OrdinalIgnoreCase))
            {
                colIndex = cell.Address.ColumnNumber;
                colEncontrada = headerText;
                break;
            }
        }

        if (colIndex < 0)
        {
            // Listar columnas disponibles para diagnóstico
            var columnasDisponibles = headerRow.CellsUsed()
                .Select(c => c.GetString().Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            throw new InvalidOperationException(
                $"Columna '{columnaValidacion}' no encontrada en el Excel. " +
                $"Columnas disponibles: [{string.Join(", ", columnasDisponibles)}]");
        }

        _logger.LogInformation("Columna '{Columna}' encontrada en posición {Index}",
            colEncontrada, colIndex);

        // ─── Analizar filas de datos (skip header) ───
        var dataRows = worksheet.RowsUsed().Skip(1).ToList();
        int total = dataRows.Count;
        int ok = 0;
        int error = 0;
        var detalle = new List<string>();

        // Valores que se consideran "exitosos"
        var valoresExitosos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "OK", "1", "SI", "SÍ", "YES", "TRUE", "EXITOSO",
            "COMPLETADO", "APROBADO", "CORRECTO"
        };

        foreach (var row in dataRows)
        {
            var valor = row.Cell(colIndex).GetString().Trim();

            if (valoresExitosos.Contains(valor))
            {
                ok++;
            }
            else
            {
                error++;
                // Limitar detalle a 100 registros para no sobrecargar memoria
                if (detalle.Count < 100)
                {
                    detalle.Add($"Fila {row.RowNumber()}: \"{valor}\"");
                }
            }
        }

        _logger.LogInformation(
            "Análisis completado: {Total} registros, {Ok} OK ({Pct:F1}%), {Error} errores",
            total, ok, total > 0 ? (double)ok / total * 100 : 0, error);

        // ─── Eliminar archivo físico temporal ───
        try
        {
            File.Delete(filePath);
            _logger.LogInformation("Archivo temporal eliminado: {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("No se pudo eliminar archivo temporal: {Error}", ex.Message);
        }

        return new ExcelResult
        {
            TotalRegistros = total,
            RegistrosOk = ok,
            RegistrosError = error,
            PorcentajeExito = total > 0 ? Math.Round((double)ok / total * 100, 2) : 0,
            Detalle = detalle
        };
    }
}

/// <summary>Resultado del análisis de Excel.</summary>
public class ExcelResult
{
    public int TotalRegistros { get; set; }
    public int RegistrosOk { get; set; }
    public int RegistrosError { get; set; }
    public double PorcentajeExito { get; set; }
    public List<string> Detalle { get; set; } = new();
}
