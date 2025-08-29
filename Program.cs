using System.IO;
using BackendRequisicionPersonal.Models.Settings;
using BackendRequisicionPersonal.Services;
using BackendRequisicionPersonal.Services.Auth;
using Microsoft.Extensions.FileProviders;
using Serilog;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});

/* logs */
var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
Directory.CreateDirectory(logDirectory);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 10)
    .CreateLogger();

builder.Host.UseSerilog();

/* dependencias */
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
builder.Services.AddScoped<SolicitudesPersonalService>();
builder.Services.AddScoped<AuthService>();

// SMTP --> IOptions<SmtpSettings>
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

/* CORS */
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("AllowSpecificOrigins", p =>
        p.WithOrigins(
              "http://192.168.20.30:8090",
              "http://localhost:3000",
              "https://reqordendecompra.recamier.com:8091"
          )
         .AllowAnyHeader()
         .AllowAnyMethod());

    opt.AddPolicy("AllowAllOrigins", p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

/* archivos estáticos */
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
        Console.WriteLine($"Sirviendo archivo estático: {ctx.File.PhysicalPath}")
});

/* /CotizacionesAdjuntas como estático */
var cotDir = Path.Combine(Directory.GetCurrentDirectory(), "CotizacionesAdjuntas");
Directory.CreateDirectory(cotDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(cotDir),
    RequestPath = "/CotizacionesAdjuntas",
    ServeUnknownFileTypes = true
});

/* swagger */
app.UseSwagger();
if (app.Environment.IsProduction())
{
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "API Solicitudes Personal v1");
        c.RoutePrefix = "swagger";
    });
}
else
{
    app.UseSwaggerUI();
}

/* CORS por entorno */
app.UseCors(app.Environment.IsProduction() ? "AllowSpecificOrigins" : "AllowAllOrigins");

app.UseAuthorization();
app.MapControllers();

/* SPA fallback */
app.MapFallbackToFile("index.html");

app.Run();
