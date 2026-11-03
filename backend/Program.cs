using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Threading.RateLimiting;
using LojaVirtual.Services;

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

builder.Services.AddDbContext<AppDbContext>(options =>
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

builder.Services.AddRateLimiter(options =>
{
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
});

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
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
