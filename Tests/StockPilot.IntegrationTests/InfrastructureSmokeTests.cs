using System.Net;

namespace StockPilot.IntegrationTests;

public class InfrastructureSmokeTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public InfrastructureSmokeTests(
        CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetCurrentUser_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}