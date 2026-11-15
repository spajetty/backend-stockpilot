
using backend_stockpilot.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace StockPilot.IntegrationTests;

public class CustomWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;

    private const string TestJwtKey =
        "StockPilotIntegrationTestKey_2026_SecureSigningKey";

    public CustomWebApplicationFactory()
    {
        _postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("stockpilot_test")
            .WithUsername("postgres")
            .WithPassword("stockpilot_test_password")
            .Build();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    _postgres.GetConnectionString(),

                ["Jwt:Issuer"] = "StockPilot.IntegrationTests",
                ["Jwt:Audience"] = "StockPilot.IntegrationTests",
                ["Jwt:Key"] = TestJwtKey,
                ["Jwt:ExpiryMinutes"] = "60"
            };

            config.AddInMemoryCollection(settings);
        });
    }

    public async Task InitializeAsync()
    {
        // Start PostgreSQL container.
        await _postgres.StartAsync();

        // Apply existing EF Core migrations.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        // Start the application.
        // DbSeeder will execute against the test database.
        using var client = CreateClient();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}