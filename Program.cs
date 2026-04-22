using QAAutomation.Api.Services;
using Microsoft.Playwright;

var builder = WebApplication.CreateBuilder(args);

// --- Servicios ---
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// Configuración de Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<JobManager>();
builder.Services.AddTransient<PlaywrightService>();
builder.Services.AddTransient<ExcelService>();

// --- Puerto para Docker ---
builder.WebHost.UseUrls("http://0.0.0.0:8080");

var app = builder.Build();

// --- Middleware ---
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
