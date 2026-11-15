
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using backend_stockpilot.Models;
using backend_stockpilot.Options;
using backend_stockpilot.Services;

namespace StockPilot.UnitTests.Services;

public class TokenServiceTests
{
    private readonly TokenService _tokenService;

    private const string TestKey =
        "StockPilot_Test_Secret_Key_At_Least_32_Characters!";

    private const string TestIssuer = "StockPilot.Tests";
    private const string TestAudience = "StockPilot.TestClient";

    public TokenServiceTests()
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            Audience = TestAudience,
            Key = TestKey,
            ExpiryMinutes = 60
        });

        _tokenService = new TokenService(jwtOptions);
    }

    private static User CreateTestUser()
    {
        return new User
        {
            Id = 1,
            Email = "test@stockpilot.com",
            FirstName = "John",
            Surname = "Doe",
            Role = UserRole.Manager,
            IsFirstLogin = true,
            StoreId = 10
        };
    }

    private static JsonWebToken ReadToken(string token)
    {
        return new JsonWebTokenHandler().ReadJsonWebToken(token);
    }

    private async Task<TokenValidationResult> ValidateTokenAsync(
        string token,
        string? key = null,
        string? issuer = null,
        string? audience = null)
    {
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(key ?? TestKey)
            ),

            ValidateIssuer = true,
            ValidIssuer = issuer ?? TestIssuer,

            ValidateAudience = true,
            ValidAudience = audience ?? TestAudience,

            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero
        };

        return await new JsonWebTokenHandler()
            .ValidateTokenAsync(token, validationParameters);
    }

    // ==========================================
    // BATCH 1: TOKEN GENERATION AND CLAIMS
    // ==========================================

    [Fact]
    public void CreateToken_ShouldReturnNonEmptyToken()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        var (token, _) = _tokenService.CreateToken(user);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public void CreateToken_ShouldIncludeCorrectSubjectClaim()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        var (token, _) = _tokenService.CreateToken(user);
        var jwt = ReadToken(token);

        // Assert
        Assert.Equal(user.Id.ToString(), jwt.Subject);
    }

    [Fact]
    public void CreateToken_ShouldIncludeCorrectEmailClaim()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        var (token, _) = _tokenService.CreateToken(user);
        var jwt = ReadToken(token);

        // Assert
        Assert.Equal(
            user.Email,
            jwt.Claims.First(c => c.Type == "email").Value
        );
    }

    [Fact]
    public void CreateToken_ShouldIncludeCorrectNameClaim()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        var (token, _) = _tokenService.CreateToken(user);
        var jwt = ReadToken(token);

        // Assert
        Assert.Equal(
            user.DisplayName,
            jwt.Claims.First(c => c.Type == "name").Value
        );
    }

    [Fact]
    public void CreateToken_ShouldIncludeCorrectRoleClaim()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        var (token, _) = _tokenService.CreateToken(user);
        var jwt = ReadToken(token);

        // Assert
        Assert.Equal(
            UserRole.Manager.ToString(),
            jwt.Claims.First(c => c.Type == "role").Value
        );
    }

    [Fact]
    public void CreateToken_ShouldIncludeSetupClaim_WhenFirstLogin()
    {
        // Arrange
        var user = CreateTestUser();
        user.IsFirstLogin = true;

        // Act
        var (token, _) = _tokenService.CreateToken(user);
        var jwt = ReadToken(token);

        // Assert
        Assert.Contains(jwt.Claims, c =>
            c.Type == "setup" && c.Value == "pending");
    }

    [Fact]
    public void CreateToken_ShouldNotIncludeSetupClaim_WhenNotFirstLogin()
    {
        // Arrange
        var user = CreateTestUser();
        user.IsFirstLogin = false;

        // Act
        var (token, _) = _tokenService.CreateToken(user);
        var jwt = ReadToken(token);

        // Assert
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "setup");
    }

    [Fact]
    public void CreateToken_ShouldIncludeStoreIdClaim_WhenUserHasStore()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        var (token, _) = _tokenService.CreateToken(user);
        var jwt = ReadToken(token);

        // Assert
        Assert.Equal(
            user.StoreId.ToString(),
            jwt.Claims.First(c => c.Type == "store_id").Value
        );
    }

    [Fact]
    public void CreateToken_ShouldNotIncludeStoreIdClaim_WhenUserHasNoStore()
    {
        // Arrange
        var user = CreateTestUser();
        user.StoreId = null;

        // Act
        var (token, _) = _tokenService.CreateToken(user);
        var jwt = ReadToken(token);

        // Assert
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "store_id");
    }

    // ==========================================
    // BATCH 2: JWT METADATA AND EXPIRATION
    // ==========================================

    [Fact]
    public void CreateToken_ShouldIncludeCorrectIssuer()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        var (token, _) = _tokenService.CreateToken(user);
        var jwt = ReadToken(token);

        // Assert
        Assert.Equal(TestIssuer, jwt.Issuer);
    }

    [Fact]
    public void CreateToken_ShouldIncludeCorrectAudience()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        var (token, _) = _tokenService.CreateToken(user);
        var jwt = ReadToken(token);

        // Assert
        Assert.Contains(TestAudience, jwt.Audiences);
    }

    [Fact]
    public void CreateToken_ShouldUseHmacSha256Algorithm()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        var (token, _) = _tokenService.CreateToken(user);
        var jwt = ReadToken(token);

        // Assert
        Assert.Equal("HS256", jwt.Alg);
    }

    [Fact]
    public void CreateToken_ShouldReturnExpirationMatchingJwtExpiration()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        var (token, expiresAt) = _tokenService.CreateToken(user);
        var jwt = ReadToken(token);

        // Assert
        var jwtExpiration =
            new DateTimeOffset(jwt.ValidTo).ToUnixTimeSeconds();

        var returnedExpiration =
            new DateTimeOffset(expiresAt).ToUnixTimeSeconds();

        Assert.Equal(returnedExpiration, jwtExpiration);
    }

    [Fact]
    public void CreateToken_ShouldRespectConfiguredExpiryMinutes()
    {
        // Arrange
        var options = Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            Audience = TestAudience,
            Key = TestKey,
            ExpiryMinutes = 15
        });

        var tokenService = new TokenService(options);
        var user = CreateTestUser();

        var beforeCreation = DateTime.UtcNow;

        // Act
        var (_, expiresAt) = tokenService.CreateToken(user);

        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.InRange(
            expiresAt,
            beforeCreation.AddMinutes(15),
            afterCreation.AddMinutes(15)
        );
    }

    // ==========================================
    // BATCH 3: JWT SECURITY VALIDATION
    // ==========================================

    [Fact]
    public async Task ValidateToken_ShouldAcceptToken_WhenSignatureIsValid()
    {
        // Arrange
        var user = CreateTestUser();
        var (token, _) = _tokenService.CreateToken(user);

        // Act
        var result = await ValidateTokenAsync(token);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateToken_ShouldRejectToken_WhenSignatureIsInvalid()
    {
        // Arrange
        var user = CreateTestUser();
        var (token, _) = _tokenService.CreateToken(user);

        const string incorrectKey =
            "This_Is_A_Different_Secret_Key_At_Least_32_Characters!";

        // Act
        var result = await ValidateTokenAsync(
            token,
            key: incorrectKey
        );

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateToken_ShouldRejectToken_WhenIssuerIsInvalid()
    {
        // Arrange
        var user = CreateTestUser();
        var (token, _) = _tokenService.CreateToken(user);

        // Act
        var result = await ValidateTokenAsync(
            token,
            issuer: "Unauthorized.Issuer"
        );

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateToken_ShouldRejectToken_WhenAudienceIsInvalid()
    {
        // Arrange
        var user = CreateTestUser();
        var (token, _) = _tokenService.CreateToken(user);

        // Act
        var result = await ValidateTokenAsync(
            token,
            audience: "Unauthorized.Client"
        );

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateToken_ShouldRejectToken_WhenTokenIsExpired()
    {
        // Arrange
        var expiredOptions = Options.Create(new JwtOptions
        {
            Issuer = TestIssuer,
            Audience = TestAudience,
            Key = TestKey,
            ExpiryMinutes = -5
        });

        var expiredTokenService = new TokenService(expiredOptions);
        var user = CreateTestUser();

        var (token, _) = expiredTokenService.CreateToken(user);

        // Act
        var result = await ValidateTokenAsync(token);

        // Assert
        Assert.False(result.IsValid);
    }
}