
using backend_stockpilot.Dtos.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace StockPilot.IntegrationTests;

[Collection("Integration Tests")]
public class AuthIntegrationTests
{
    private readonly HttpClient _client;

    public AuthIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenAndUser()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "owner@stockpilot.ph",
            Password = "demo1234"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", request);

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal("owner@stockpilot.ph", result.User.Email);
        Assert.Equal("Owner", result.User.Role);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);

        // Verify that the returned JWT actually works.
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", result.Token);

        var meResponse = await _client.GetAsync("/api/auth/me");

        // Diagnostic information
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(result.Token);

        var claims = string.Join(
            ", ",
            jwt.Claims.Select(c => $"{c.Type}={c.Value}")
        );

        var authHeader = meResponse.Headers.WwwAuthenticate.ToString();
        var body = await meResponse.Content.ReadAsStringAsync();

        Assert.True(
            meResponse.StatusCode == HttpStatusCode.OK,
            $"Expected OK but received {meResponse.StatusCode}.\n" +
            $"Token claims: {claims}\n" +
            $"WWW-Authenticate: {authHeader}\n" +
            $"Response body: {body}"
        );

        var currentUser =
            await meResponse.Content.ReadFromJsonAsync<UserDto>();

        Assert.NotNull(currentUser);
        Assert.Equal("owner@stockpilot.ph", currentUser.Email);
    }

    [Fact]
    public async Task Login_WithIncorrectPassword_ReturnsUnauthorized()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "owner@stockpilot.ph",
            Password = "wrongpassword"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithNonexistentEmail_ReturnsUnauthorized()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "nonexistent@stockpilot.ph",
            Password = "demo1234"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}