using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class SecurityEndpointTests
{
    [Fact]
    public async Task Data_protection_keys_are_persisted_when_a_path_is_configured()
    {
        var keysDirectory = Path.Combine(
            Path.GetTempPath(), $"mnemora-keys-{Guid.NewGuid():N}");

        try
        {
            using var factory = CreateFactory(new Dictionary<string, string?>
            {
                ["Mnemora:DataProtectionKeysPath"] = keysDirectory
            });
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/api/auth/csrf");

            response.EnsureSuccessStatusCode();
            Assert.NotEmpty(Directory.EnumerateFiles(keysDirectory, "key-*.xml"));
        }
        finally
        {
            if (Directory.Exists(keysDirectory))
                Directory.Delete(keysDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Write_without_antiforgery_token_is_rejected_and_not_cached()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"csrf-{Guid.NewGuid():N}@test.local",
            password = "StrongTestPass123"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("Token de segurança inválido.", problem?["title"]?.ToString());
    }

    [Fact]
    public async Task Seed_does_not_promote_an_existing_account_with_a_different_password()
    {
        const string email = "reserved-admin@test.local";
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["ADMIN_EMAIL"] = email,
            ["ADMIN_PASSWORD"] = "Configured-Admin-Password-2026"
        });
        using var client = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var user = new IdentityUser<Guid> { UserName = email, Email = email };
        var created = await users.CreateAsync(user, "Attacker-Owned-Password-2026");
        Assert.True(created.Succeeded);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SeedData.RunAsync(
                scope.ServiceProvider,
                scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("SeedSecurityTest")));

        Assert.Contains("senha configurada não corresponde", error.Message);
        Assert.False(await users.IsInRoleAsync(user, "Admin"));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var dbFile = Path.Combine(
            Path.GetTempPath(), $"mnemora-security-{Guid.NewGuid():N}.db");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Development");
            if (settings is not null)
            {
                foreach (var (key, value) in settings)
                    if (value is not null) web.UseSetting(key, value);
            }
            web.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<MnemoraDbContext>>();
                services.RemoveAll<MnemoraDbContext>();
                services.AddDbContext<MnemoraDbContext>(options =>
                    options.UseSqlite($"Data Source={dbFile}"));
            });
        });
    }
}
