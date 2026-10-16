using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class SeedDataTests
{
    [Theory]
    [InlineData("weak", "PasswordTooShort")]
    [InlineData("alllowercase123", "PasswordRequiresUpper")]
    [InlineData("ALLUPPERCASE123", "PasswordRequiresLower")]
    [InlineData("MixedCaseWithoutDigits", "PasswordRequiresDigit")]
    public async Task Invalid_password_reports_identity_codes_without_credentials(
        string password, string expectedCode)
    {
        const string email = "private-seed-admin@example.test";
        using var factory = new SeedFactory(email, password);
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunSeedAsync(scope.ServiceProvider));

        Assert.Contains(expectedCode, error.Message);
        Assert.DoesNotContain(email, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, error.Message, StringComparison.Ordinal);
        var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.Books.ToListAsync());
    }

    [Fact]
    public async Task Invalid_email_reports_identity_code_without_credential_values()
    {
        const string email = "private-email-without-at-sign";
        const string password = "ValidSeedPassword123";
        using var factory = new SeedFactory(email, password);
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunSeedAsync(scope.ServiceProvider));

        Assert.Contains("InvalidEmail", error.Message);
        Assert.DoesNotContain(email, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, error.Message, StringComparison.Ordinal);
        var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.Books.ToListAsync());
    }

    [Fact]
    public async Task Valid_seed_is_idempotent_and_creates_a_working_admin_login()
    {
        const string email = "valid-seed-admin@example.test";
        const string password = "ValidSeedPassword123";
        using var factory = new SeedFactory($"  {email}  ", password);
        using var client = factory.CreateClient();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await RunSeedAsync(scope.ServiceProvider);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
            var first = await users.FindByEmailAsync(email);
            Assert.NotNull(first);
            var userId = first.Id;
            var passwordHash = first.PasswordHash;

            await RunSeedAsync(scope.ServiceProvider);

            var admin = await users.FindByEmailAsync(email);
            Assert.NotNull(admin);
            Assert.Equal(userId, admin.Id);
            Assert.Equal(passwordHash, admin.PasswordHash);
            Assert.True(await users.IsInRoleAsync(admin, "Admin"));
            var db = scope.ServiceProvider.GetRequiredService<MnemoraDbContext>();
            Assert.Equal(1, await db.Users.CountAsync());
            Assert.Equal(1, await db.Roles.CountAsync());
            Assert.Equal(1, await db.Books.CountAsync(book =>
                book.ExternalProvider == "MnemoraSeed" && book.ExternalId == "mist-archive-v1"));
        }

        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var user = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.True(user.GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task Seed_does_not_overwrite_an_existing_admin_password()
    {
        const string email = "existing-seed-admin@example.test";
        const string originalPassword = "OriginalSeedPassword123";
        const string changedEnvironmentPassword = "ChangedEnvironmentPassword456";
        using var factory = new SeedFactory(email, originalPassword);
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        await RunSeedAsync(scope.ServiceProvider);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var original = await users.FindByEmailAsync(email);
        Assert.NotNull(original);
        var originalHash = original.PasswordHash;
        var changedConfiguration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ADMIN_EMAIL"] = email,
                ["ADMIN_PASSWORD"] = changedEnvironmentPassword
            }).Build();

        await RunSeedAsync(scope.ServiceProvider, changedConfiguration);

        var admin = await users.FindByEmailAsync(email);
        Assert.NotNull(admin);
        Assert.Equal(originalHash, admin.PasswordHash);
        Assert.True(await users.CheckPasswordAsync(admin, originalPassword));
        Assert.False(await users.CheckPasswordAsync(admin, changedEnvironmentPassword));
        Assert.True(await users.IsInRoleAsync(admin, "Admin"));
    }

    private static Task RunSeedAsync(IServiceProvider services, IConfiguration? configuration = null) =>
        SeedData.RunAsync(services,
            configuration ?? services.GetRequiredService<IConfiguration>(),
            services.GetRequiredService<ILoggerFactory>().CreateLogger("SeedDataTest"));

    private sealed class SeedFactory(string email, string password) : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = OpenConnection();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ADMIN_EMAIL", email);
            builder.UseSetting("ADMIN_PASSWORD", password);
            builder.UseSetting("MemoryPackGeneration:Enabled", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<MnemoraDbContext>>();
                services.RemoveAll<MnemoraDbContext>();
                services.AddDbContext<MnemoraDbContext>(options => options.UseSqlite(_connection));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }

        private static SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            return connection;
        }
    }
}
