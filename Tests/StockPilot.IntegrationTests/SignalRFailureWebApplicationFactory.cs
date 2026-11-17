using backend_stockpilot.Data;
using backend_stockpilot.Hubs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace StockPilot.IntegrationTests;

public class SignalRFailureWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private readonly IHubContext<StockHub> _hubContext;

    public SignalRFailureWebApplicationFactory(
        IHubContext<StockHub> hubContext)
    {
        _hubContext = hubContext;

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
                ["Jwt:Key"] =
                    "StockPilotIntegrationTestKey_2026_SecureSigningKey",
                ["Jwt:ExpiryMinutes"] = "60"
            };

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHubContext<StockHub>>();
            services.AddSingleton<IHubContext<StockHub>>(_hubContext);
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        using var client = CreateClient();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}