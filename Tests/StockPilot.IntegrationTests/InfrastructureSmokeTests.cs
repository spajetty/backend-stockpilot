using System.Net;

namespace StockPilot.IntegrationTests;

[Collection("Integration Tests")]
public class InfrastructureSmokeTests
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