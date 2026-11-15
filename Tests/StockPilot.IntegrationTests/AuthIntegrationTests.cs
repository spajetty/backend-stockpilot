
using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Auth;
using backend_stockpilot.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace StockPilot.IntegrationTests;

[Collection("Integration Tests")]
public class AuthIntegrationTests
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public AuthIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenAndUser()
    {
        var request = new LoginRequest
        {
            Email = "owner@stockpilot.ph",
            Password = "demo1234"
        };

        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", request);

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal("owner@stockpilot.ph", result.User.Email);
        Assert.Equal("Owner", result.User.Role);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", result.Token);

        var meResponse = await _client.GetAsync("/api/auth/me");

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
        var request = new LoginRequest
        {
            Email = "owner@stockpilot.ph",
            Password = "wrongpassword"
        };

        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithNonexistentEmail_ReturnsUnauthorized()
    {
        var request = new LoginRequest
        {
            Email = "nonexistent@stockpilot.ph",
            Password = "demo1234"
        };

        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithInactiveUser_ReturnsUnauthorized()
    {
        // Arrange
        var email = $"inactive-{Guid.NewGuid():N}@stockpilot.ph";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = new User
            {
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("demo1234"),
                FirstName = "Inactive",
                Surname = "Tester",
                Role = UserRole.Cashier,
                IsActive = false
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var request = new LoginRequest
        {
            Email = email,
            Password = "demo1234"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUppercaseEmail_ReturnsOk()
    {
        var request = new LoginRequest
        {
            Email = "OWNER@STOCKPILOT.PH",
            Password = "demo1234"
        };

        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithEmailSurroundedByWhitespace_ReturnsOk()
    {
        var request = new LoginRequest
        {
            Email = "  owner@stockpilot.ph  ",
            Password = "demo1234"
        };

        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithMissingEmail_ReturnsBadRequest()
    {
        var request = new { Password = "demo1234" };

        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithMissingPassword_ReturnsBadRequest()
    {
        var request = new { Email = "owner@stockpilot.ph" };

        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithInvalidEmailFormat_ReturnsBadRequest()
    {
        var request = new
        {
            Email = "not-an-email",
            Password = "demo1234"
        };

        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}