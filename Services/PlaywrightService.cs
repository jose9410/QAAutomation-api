using System.Diagnostics;
using QAAutomation.Api.Models;
using Microsoft.Playwright;

namespace QAAutomation.Api.Services;

/// <summary>
/// Motor de automatización multi-paso genérico.
///
/// Flujo completo:
///   Paso 1 → Navegar a UrlEntrada (Windows Auth NTLM)
///   Paso 2 → Dropdown Año  (select#SelectedYear)
///   Paso 3 → Dropdown Mes  (select#SelectedMonth)  + espera carga
///   Paso 4 → Click en día  (a.NormalDay texto=día) → navega a nueva URL
///   Paso 5 → Dropdown Proceso (select#ProcessId por GUID) → auto-submit
///   Paso 6 → Click Iniciar (a#startProcess)
/// </summary>
public class PlaywrightService
{
    private readonly ILogger<PlaywrightService> _logger;
    private readonly IConfiguration _configuration;
    private readonly JobManager _jobManager;

    public PlaywrightService(
        ILogger<PlaywrightService> logger,
        IConfiguration configuration,
        JobManager jobManager)
    {
        _logger = logger;
        _configuration = configuration;
        _jobManager = jobManager;
    }

    /// <summary>
    /// Ejecuta el flujo multi-paso de automatización para el proceso indicado.
    /// </summary>
    public async Task ExecuteProcessAsync(ProcessStartRequest request, string jobId)
    {
        // ─── Obtener configuración de la aplicación ───
        var aplicaciones = _configuration
            .GetSection("Aplicaciones")
            .Get<List<AplicacionConfig>>() ?? new();

        var app = aplicaciones.FirstOrDefault(a =>
            a.Nombre.Equals(request.NombreAplicacion, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Aplicación '{request.NombreAplicacion}' no encontrada en appsettings.json");

        if (app.SubProcesos == null || app.SubProcesos.Count == 0)
        {
            throw new InvalidOperationException($"La aplicación '{request.NombreAplicacion}' no tiene procesos configurados para ejecutar.");
        }

        _logger.LogInformation(
            "Iniciando automatización lote: App={App}, Total Procesos={Total}",
            app.Nombre, app.SubProcesos.Count);

        // ─── Lanzar Playwright (Headless=false para depuración) ───
        using var playwright = await Playwright.CreateAsync();

        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,    // ← DEBE ser true para Docker
            SlowMo = 0,         // ← Sin delay en producción
            Args = new[]
            {
                "--ignore-certificate-errors",
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--start-maximized"
            }
        });

        var username = _configuration["Credenciales:Username"] ?? "";
        var password = _configuration["Credenciales:Password"] ?? "";

        var contextOptions = new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = null, // null = maximized window
            AcceptDownloads = true
        };

        if (!string.IsNullOrWhiteSpace(username))
        {
            contextOptions.HttpCredentials = new HttpCredentials
            {
                Username = username,
                Password = password
            };
            _logger.LogInformation("Windows Auth NTLM configurado para: {User}", username);
        }

        await using var context = await browser.NewContextAsync(contextOptions);

        context.Response += (_, response) =>
        {
            if (response.Status == 401)
                _logger.LogWarning("401 en: {Url}", response.Url);
        };

        var page = await context.NewPageAsync();

        // ATENCIÓN: El sistema web lanza un prompt nativo (Window.confirm).
        // Capturamos TODOS los diálogos en esta sesión y aceptamos automáticamente.
        page.Dialog += async (_, dialog) =>
        {
            _logger.LogInformation("Ventana emergente detectada: '{DialogMessage}'. Presionando Aceptar...", dialog.Message);
            await Task.Delay(500); // Pequeña pausa visual si estamos depurando
            await dialog.AcceptAsync();
        };

        int procesados = 0;
        int total = app.SubProcesos.Count;

        foreach (var subProceso in app.SubProcesos)
        {
            procesados++;
            _logger.LogInformation("=========================================");
            _logger.LogInformation("[{Idx}/{Total}] EJECUTANDO: {Process}", procesados, total, subProceso.Nombre);
            _logger.LogInformation("=========================================");

            // ═══════════════════════════════════════════════════════════════
            // PASO 1: Navegar a la URL de entrada
            // ═══════════════════════════════════════════════════════════════
            _jobManager.UpdateJob(jobId, j =>
            {
                j.Status = JobStatus.Iniciando;
                j.Mensaje = $"[Lote {procesados}/{total}] [Paso 1/6] Iniciando '{subProceso.Nombre}'...";
                j.NombreProceso = $"Lote {app.Nombre}";
            });

            _logger.LogInformation("Paso 1: Navegando a {Url}", app.UrlEntrada);
            var response1 = await page.GotoAsync(app.UrlEntrada, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = app.TimeoutNavegacionMs
            });

            if (response1?.Status == 401)
                throw new UnauthorizedAccessException(
                    "Error 401: Credenciales rechazadas. Verifique 'Credenciales' en appsettings.json.");

            await Task.Delay(app.EsperaEntrePassosMs);

            // ═══════════════════════════════════════════════════════════════
            // PASO 2: Seleccionar Año
            // ═══════════════════════════════════════════════════════════════
            _jobManager.UpdateJob(jobId, j =>
            {
                j.Status = JobStatus.LanzandoReproceso;
                j.Mensaje = $"[Lote {procesados}/{total}] [Paso 2/6] Año {subProceso.Anio}...";
            });

            _logger.LogInformation("Paso 2: Seleccionando año {Anio}", subProceso.Anio);
            var dropdownAnio = page.Locator(app.SelectorAnio);
            await dropdownAnio.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = app.TimeoutNavegacionMs });
            await dropdownAnio.SelectOptionAsync(new SelectOptionValue { Value = subProceso.Anio.ToString() });
            
            await Task.Delay(app.EsperaEntrePassosMs);

            // ═══════════════════════════════════════════════════════════════
            // PASO 3: Seleccionar Mes
            // ═══════════════════════════════════════════════════════════════
            _jobManager.UpdateJob(jobId, j =>
            {
                j.Mensaje = $"[Lote {procesados}/{total}] [Paso 3/6] Mes {subProceso.Mes}...";
            });

            _logger.LogInformation("Paso 3: Seleccionando mes {Mes}", subProceso.Mes);
            var dropdownMes = page.Locator(app.SelectorMes);
            await dropdownMes.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = app.TimeoutNavegacionMs });
            await dropdownMes.SelectOptionAsync(new SelectOptionValue { Value = subProceso.Mes.ToString() });
            
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(app.EsperaEntrePassosMs);

            // ═══════════════════════════════════════════════════════════════
            // PASO 4: Hacer clic en el día del calendario
            // ═══════════════════════════════════════════════════════════════
            _jobManager.UpdateJob(jobId, j =>
            {
                j.Mensaje = $"[Lote {procesados}/{total}] [Paso 4/6] Día {subProceso.Dia}...";
            });

            _logger.LogInformation("Paso 4: Buscando día {Dia}", subProceso.Dia);
            var linkDia = page.Locator(app.SelectorDia).Filter(new LocatorFilterOptions { HasText = subProceso.Dia.ToString() }).First;
            await linkDia.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = app.TimeoutNavegacionMs });
            
            await linkDia.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(app.EsperaEntrePassosMs);

            // ═══════════════════════════════════════════════════════════════
            // PASO 5: Seleccionar proceso del dropdown
            // ═══════════════════════════════════════════════════════════════
            _jobManager.UpdateJob(jobId, j =>
            {
                j.Mensaje = $"[Lote {procesados}/{total}] [Paso 5/6] Seleccionando '{subProceso.Nombre}'...";
            });

            _logger.LogInformation("Paso 5: Seleccionando {Proceso} (Id: {Guid})", subProceso.Nombre, subProceso.ProcessId);
            var dropdownProceso = page.Locator(app.SelectorProceso);
            await dropdownProceso.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = app.TimeoutNavegacionMs });
            await dropdownProceso.SelectOptionAsync(new SelectOptionValue { Value = subProceso.ProcessId });
            
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(app.EsperaEntrePassosMs);

            // ═══════════════════════════════════════════════════════════════
            // PASO 6: Clic en "Iniciar Reproceso"
            // ═══════════════════════════════════════════════════════════════
            _jobManager.UpdateJob(jobId, j =>
            {
                j.Status = JobStatus.MonitoreandoTablero;
                j.Mensaje = $"[Lote {procesados}/{total}] [Paso 6/6] Confirmando inicio...";
            });

            _logger.LogInformation("Paso 6: Haciendo clic en btn Iniciar");
            var btnIniciar = page.Locator(app.SelectorBotonIniciar);
            await btnIniciar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = app.TimeoutNavegacionMs });
            await btnIniciar.ClickAsync();

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(app.EsperaEntrePassosMs * 2);

            _jobManager.UpdateJob(jobId, j =>
            {
                j.Detalle.Add($"✓ Procesado: {subProceso.Nombre} (Fecha ref: {subProceso.Dia}/{subProceso.Mes}/{subProceso.Anio})");
            });

            _logger.LogInformation("✅ Reproceso [{Idx}/{Total}] ENVIADO: '{Proceso}'", procesados, total, subProceso.Nombre);
        }

        // Al finalizar todos los sub-procesos del bucle
        _logger.LogInformation("Lote de {Total} procesos ejecutado satisfactoriamente.", total);

        _jobManager.UpdateJob(jobId, j =>
        {
            j.Status = JobStatus.Completado;
            j.Mensaje = $"Los {total} procesos de '{app.Nombre}' fueron iniciados exitosamente en lote.";
            j.FinalizadoEn = DateTime.Now;
        });
    }
}
