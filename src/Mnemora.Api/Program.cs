using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mnemora.Api;
using Mnemora.Application;
using Mnemora.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=mnemora.db";
var webOrigin = builder.Configuration["Mnemora:WebOrigin"] ?? "http://localhost:3000";
var dataProtectionKeysPath = builder.Configuration["Mnemora:DataProtectionKeysPath"];

builder.Services.AddDbContext<MnemoraDbContext>(options => options.UseSqlite(connectionString));
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Mnemora");
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    var keysDirectory = new DirectoryInfo(Path.GetFullPath(dataProtectionKeysPath));
    keysDirectory.Create();
    dataProtection.PersistKeysToFileSystem(keysDirectory);
}
builder.Services.AddScoped<KnowledgeReader>();
builder.Services.AddScoped<BookConsistency>();
builder.Services.AddMemoryCache(options => options.SizeLimit = 1_000);
builder.Services.AddHttpClient<GoogleBooksProvider>(
    client => client.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddHttpClient<OpenLibraryProvider>(client =>
{
    client.BaseAddress = new Uri("https://openlibrary.org/");
    client.Timeout = TimeSpan.FromSeconds(8);
    var contact = builder.Configuration["OPEN_LIBRARY_CONTACT"]?.Trim();
    if (string.IsNullOrWhiteSpace(contact))
        contact = "https://github.com/DiogoJP202/Mnemora";
    client.DefaultRequestHeaders.UserAgent.ParseAdd($"Mnemora/1.0 ({contact})");
});
builder.Services.AddTransient<IBookMetadataSource>(services =>
    services.GetRequiredService<GoogleBooksProvider>());
builder.Services.AddTransient<IBookMetadataSource>(services =>
    services.GetRequiredService<OpenLibraryProvider>());
builder.Services.AddTransient<IBookMetadataProvider, CompositeBookMetadataProvider>();
builder.Services
    .AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<MnemoraDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "Mnemora.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "Mnemora.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddAuthorization(options =>
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin")));
builder.Services.AddCors(options => options.AddPolicy("web", policy =>
    policy.WithOrigins(webOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    foreach (var configured in builder.Configuration
                 .GetSection("Mnemora:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(configured, out var address))
            options.KnownProxies.Add(address);
    }
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("catalog-external", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("import-external", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("recall", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("memory-write", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
    await db.Database.MigrateAsync();
}
if (args.Contains("--seed", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.RunAsync(scope.ServiceProvider, builder.Configuration, app.Logger);
}

app.UseRouting();
app.UseCors("web");
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });
    }

    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        if (HttpMethods.IsPost(context.Request.Method)
            || HttpMethods.IsPut(context.Request.Method)
            || HttpMethods.IsPatch(context.Request.Method)
            || HttpMethods.IsDelete(context.Request.Method))
        {
            try
            {
                await context.RequestServices.GetRequiredService<IAntiforgery>()
                    .ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://httpstatuses.com/400",
                    title = "Token de segurança inválido.",
                    status = 400
                });
                return;
            }
        }
    }
    await next(context);
});

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapBooksEndpoints();
app.MapLibraryEndpoints();
app.MapKnowledgeEndpoints();
app.MapMemoryEndpoints();
app.MapAdminBookEndpoints();
app.MapAdminLoreEndpoints();
app.MapAdminRelationEndpoints();
app.Run();

public partial class Program;
