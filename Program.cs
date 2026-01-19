using System.IO;
using BackendRequisicionPersonal.Models.Settings;
using BackendRequisicionPersonal.Services;
using BackendRequisicionPersonal.Services.Auth;
using BackendRequisicionPersonal.Services.Email;
using Microsoft.Extensions.FileProviders;
using Serilog;
using DinkToPdf;
using DinkToPdf.Contracts;
using BackendRequisicionPersonal.Utilities;

CustomWkhtmlLoader.LoadWkhtmltox();

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

// Servicios de negocio principales
builder.Services.AddScoped<SolicitudesPersonalService>();
builder.Services.AddScoped<AuthService>();

// Servicios especializados de requisiciones
builder.Services.AddScoped<AprobacionService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var logger = sp.GetRequiredService<ILogger<AprobacionService>>();
    
    var connectionString = env.IsProduction()
        ? config.GetConnectionString("InformixConnectionProduction")
        : config.GetConnectionString("InformixConnection");
    
    return new AprobacionService(config, logger, connectionString);
});

builder.Services.AddScoped<ConsultasPersonalService>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<ConsultasPersonalService>>();
    
    var connectionString = env.IsProduction()
        ? config.GetConnectionString("InformixConnectionProduction")
        : config.GetConnectionString("InformixConnection");
    
    return new ConsultasPersonalService(logger, connectionString);
});

builder.Services.AddScoped<ReportesService>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<ReportesService>>();
    
    var connectionString = env.IsProduction()
        ? config.GetConnectionString("InformixConnectionProduction")
        : config.GetConnectionString("InformixConnection");
    
    return new ReportesService(logger, connectionString);
});

// Servicios de Email
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddSingleton<EmailTemplateService>();

// PDF
builder.Services.AddSingleton(typeof(IConverter), new SynchronizedConverter(new PdfTools()));
builder.Services.AddTransient<PdfService>();

// SMTP --> IOptions<SmtpSettings>
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

/* CORS */
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("AllowSpecificOrigins", p =>
        p.WithOrigins(
              "http://192.168.20.30:8092",
              "http://localhost:3000",
              "https://requisicionpersonal.recamier.com:8093"
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
