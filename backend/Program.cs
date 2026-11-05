using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using LojaVirtual.Services;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// O Render fornece PORT em runtime. Localmente, sem PORT, o Kestrel mantém
// as URLs padrão do perfil de desenvolvimento ou da configuração do host.
var renderPort = Environment.GetEnvironmentVariable("PORT");
var hasAspNetCoreUrlOverride = !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
    || !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"));

if (!hasAspNetCoreUrlOverride
    && int.TryParse(renderPort, out var parsedRenderPort)
    && parsedRenderPort is > 0 and <= 65535)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{parsedRenderPort}");
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

/*builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")));*/

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseMySql(
        connectionString,
        ServerVersion.Parse("8.0.0-mysql")
    ));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager<SignInManager<ApplicationUser>>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    })
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "Angel.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAuthenticatedUser().RequireRole("Admin"));
});

// Add services to the container.
builder.Services.AddControllersWithViews();
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("AngelAcessorios")
    .PersistKeysToDbContext<AppDbContext>();
if (!builder.Environment.IsDevelopment())
{
    var certificate = LoadDataProtectionCertificate(builder.Configuration);
    dataProtectionBuilder.ProtectKeysWithCertificate(certificate);
}
builder.Services.Configure<CloudinaryOptions>(options =>
{
    options.CloudName = builder.Configuration["Cloudinary:CloudName"] ?? string.Empty;
    options.ApiKey = builder.Configuration["Cloudinary:ApiKey"] ?? string.Empty;
    options.ApiSecret = builder.Configuration["Cloudinary:ApiSecret"] ?? string.Empty;
});
builder.Services.AddScoped<IImageStorage, CloudinaryImageStorage>();
builder.Services.AddScoped<ProductImageService>();

var allowedCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .GetChildren()
    .Select(section => section.Value)
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin!.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedCorsOrigins.Length > 0)
        {
            policy
                .WithOrigins(allowedCorsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

builder.Services.AddHealthChecks();

builder.Services.AddHttpClient<
    IMercadoPagoService,
    MercadoPagoService>();
builder.Services.Configure<ShippingOptions>(builder.Configuration.GetSection("Shipping"));
builder.Services.AddHttpClient<IShippingQuoteService, MelhorEnvioQuoteService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddScoped<IMelhorEnvioTokenStore, MelhorEnvioTokenStore>();
builder.Services.AddScoped<IMelhorEnvioOAuthService, MelhorEnvioOAuthService>();
builder.Services.Configure<ResendOptions>(builder.Configuration.GetSection("Resend"));
builder.Services.AddHttpClient<ITransactionalEmailSender, ResendEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<AdminProvisioningService>();
builder.Services.AddScoped<IOrderAccessCredentialService, OrderAccessCredentialService>();
builder.Services.AddScoped<OrderPaymentService>();
builder.Services.AddScoped<IOrderRecoveryService, OrderRecoveryService>();
builder.Services.AddSingleton<OrderRecoveryRateLimiter>();
builder.Services.AddSingleton<Microsoft.Extensions.Caching.Distributed.IDistributedCache, MySqlDistributedCache>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("order-tracking", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("order-create", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}|{context.Session.Id}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");

    app.UseHsts();
}

var cultureInfo = new CultureInfo("pt-BR");

CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

//app.UseHttpsRedirection();

app.UseSession();

app.UseStaticFiles();

// Compatibilidade com uploads antigos gravados no wwwroot legado do content root.
var legacyStaticRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(legacyStaticRoot)
    && !string.Equals(legacyStaticRoot, app.Environment.WebRootPath, StringComparison.OrdinalIgnoreCase))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(legacyStaticRoot)
    });
}

app.UseRouting();
app.UseRateLimiter();
app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Redirect("/Admin/Index"));
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

if (args.Any(argument => string.Equals(argument, "--provision-admin", StringComparison.OrdinalIgnoreCase)))
{
    await using var scope = app.Services.CreateAsyncScope();
    var provisioner = scope.ServiceProvider.GetRequiredService<AdminProvisioningService>();
    await provisioner.RunInteractiveAsync();
    return;
}

app.Run();

static X509Certificate2 LoadDataProtectionCertificate(IConfiguration configuration)
{
    var certificateBase64 = configuration["DataProtection:CertificateBase64"];
    var certificatePassword = configuration["DataProtection:CertificatePassword"];

    if (!string.IsNullOrWhiteSpace(certificateBase64))
    {
        if (string.IsNullOrWhiteSpace(certificatePassword))
        {
            throw new InvalidOperationException(
                "Data Protection não conseguiu carregar o certificado Base64: "
                + "DataProtection__CertificatePassword não foi configurada.");
        }

        byte[] certificateBytes;
        try
        {
            certificateBytes = Convert.FromBase64String(certificateBase64);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Data Protection não conseguiu carregar o certificado Base64: "
                + "DataProtection__CertificateBase64 não contém Base64 válido.");
        }

        return CreateValidatedDataProtectionCertificate(
            () => new X509Certificate2(
                certificateBytes,
                certificatePassword,
                X509KeyStorageFlags.EphemeralKeySet),
            "DataProtection__CertificateBase64");
    }

    var certificatePath = configuration["DataProtection:CertificatePath"];
    if (string.IsNullOrWhiteSpace(certificatePath))
    {
        throw new InvalidOperationException(
            "Data Protection exige DataProtection__CertificateBase64 ou "
            + "DataProtection__CertificatePath em Staging/Production.");
    }

    if (!File.Exists(certificatePath))
    {
        throw new InvalidOperationException(
            "Data Protection não encontrou o certificado em "
            + "DataProtection__CertificatePath.");
    }

    if (string.IsNullOrWhiteSpace(certificatePassword))
    {
        throw new InvalidOperationException(
            "Data Protection não conseguiu carregar o certificado do caminho configurado: "
            + "DataProtection__CertificatePassword não foi configurada.");
    }

    return CreateValidatedDataProtectionCertificate(
        () => new X509Certificate2(
            certificatePath,
            certificatePassword,
            X509KeyStorageFlags.EphemeralKeySet),
        "DataProtection__CertificatePath");
}

static X509Certificate2 CreateValidatedDataProtectionCertificate(
    Func<X509Certificate2> certificateFactory,
    string certificateSource)
{
    X509Certificate2 certificate;
    try
    {
        certificate = certificateFactory();
    }
    catch (CryptographicException)
    {
        throw new InvalidOperationException(
            $"Data Protection não conseguiu abrir o certificado configurado em {certificateSource}. "
            + "Verifique se o conteúdo é um PFX válido e se a senha está correta.");
    }

    var now = DateTime.UtcNow;
    if (!certificate.HasPrivateKey)
    {
        certificate.Dispose();
        throw new InvalidOperationException(
            $"Data Protection exige um certificado privado em {certificateSource}; "
            + "o certificado configurado não contém chave privada.");
    }

    if (now < certificate.NotBefore || now > certificate.NotAfter)
    {
        certificate.Dispose();
        throw new InvalidOperationException(
            $"Data Protection não pode usar o certificado em {certificateSource}: "
            + "o certificado está fora do período de validade.");
    }

    return certificate;
}
