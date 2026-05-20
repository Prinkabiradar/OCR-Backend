using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Http.Features;
using OCR_BACKEND.BackgroundServices;
using OCR_BACKEND.Queue;
using OCR_BACKEND.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ── Services ────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<SqlDBHelper>();
builder.Services.AddSingleton<JwtHelper>();

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<UserDBHelper>();

builder.Services.AddScoped<IUserAddService, UserAddService>();
builder.Services.AddScoped<UserAddDBHelper>();

builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<MenuDBHelper>();

builder.Services.AddScoped<IDocumentTypeService, DocumentTypeService>();
builder.Services.AddScoped<DocumentTypeDBHelper>();

builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<DocumentDBHelper>();

builder.Services.AddScoped<IDocumentPageService, DocumentPageService>();
builder.Services.AddScoped<DocumentPageDBHelper>();

builder.Services.AddScoped<IUtilityService, UtilityService>();
builder.Services.AddScoped<UtilityDBHelper>();

builder.Services.AddHttpClient();
builder.Services.AddScoped<AgentDBHelper>();
builder.Services.AddScoped<IAgentService, AgentService>();

builder.Services.AddScoped<DashboardDBHelper>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

builder.Services.AddScoped<ISuggestionService, SuggestionService>();
builder.Services.AddScoped<SuggestionDBHelper>();

builder.Services.AddScoped<IEmailService, EmailService>();

builder.Services.AddHttpClient<GeminiService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});

builder.Services.AddSingleton<OcrJobQueue>();
builder.Services.AddSingleton<OcrJobCancellationRegistry>(); // registered once (removed duplicate)
builder.Services.AddSingleton<OcrJobDBHelper>();
builder.Services.AddScoped<IOcrJobService, OcrJobService>();
builder.Services.AddHostedService<OcrWorkerService>();
builder.Services.AddScoped<IFileConversionService, FileConversionService>();
builder.Services.AddSingleton<IPdfToImageService, PdfToImageService>();

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 500 * 1024 * 1024;
});
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 500 * 1024 * 1024;
});

builder.Services.AddScoped<IRoleAccessService, RoleAccessService>();
builder.Services.AddScoped<RoleAccessDBHelper>();

// ── Storage (DigitalOcean or Local) ─────────────────────────────────────────
var useDigitalOcean = !string.IsNullOrWhiteSpace(
    Environment.GetEnvironmentVariable("DIGITALOCEAN_SPACES_ACCESS_KEY"));

if (useDigitalOcean)
    builder.Services.AddScoped<IStorageService, DigitalOceanStorageService>();
else
    builder.Services.AddScoped<IStorageService, LocalFileStorageService>();

// ── JWT Authentication ───────────────────────────────────────────────────────
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
        };
    });

// ── CORS ─────────────────────────────────────────────────────────────────────
const string FrontendCorsPolicy = "FrontendCorsPolicy";

var allowedOrigins = new[]
{
    "https://tts.sharpflux.com",
    "https://ocr.sharpflux.com"
};

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin)) return false;
                if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;

                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
                return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                       || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
            })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders("Content-Disposition");
    });
});

// ════════════════════════════════════════════════════════════════════════════
var app = builder.Build();
// ════════════════════════════════════════════════════════════════════════════

var logger = app.Services.GetRequiredService<ILogger<Program>>();

if (useDigitalOcean)
{
    logger.LogInformation("Storage Mode: Digital Ocean Spaces");
}
else
{
    logger.LogWarning("Storage Mode: Local Filesystem (Development Only)");
    logger.LogWarning("Set DIGITALOCEAN_SPACES_ACCESS_KEY etc. to switch to Spaces.");
}

// ── Static files ─────────────────────────────────────────────────────────────
app.UseStaticFiles(); // wwwroot

var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
try
{
    Directory.CreateDirectory(uploadsRoot);
}
catch (UnauthorizedAccessException ex)
{
    var fallback = Path.Combine(Path.GetTempPath(), "ocr-uploads");
    Directory.CreateDirectory(fallback);
    uploadsRoot = fallback;
    logger.LogWarning(ex, "Primary uploads path not writable. Falling back to: {Path}", fallback);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath  = "/uploads"
});

// ── Swagger (dev only) ────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ── Middleware pipeline — ORDER IS CRITICAL ───────────────────────────────────
// Keep HTTPS redirection in non-development environments.
// In local development we allow HTTP too, so frontend fallback
// (http://localhost:5247) can work when HTTPS transport fails.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();           // 1. Routing first

app.UseCors(FrontendCorsPolicy);  // 2. CORS immediately after routing
                                  //    (must be BEFORE auth & controllers)

app.UseAuthentication();   // 3. Auth
app.UseAuthorization();    // 4. Authz

app.MapControllers();      // 5. Endpoints last

app.Run();
