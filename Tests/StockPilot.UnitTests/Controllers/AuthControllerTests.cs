
using System.Security.Claims;
using backend_stockpilot.Controllers;
using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Auth;
using backend_stockpilot.Models;
using backend_stockpilot.Options;
using backend_stockpilot.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace StockPilot.UnitTests.Controllers;

public class AuthControllerTests
{
    private static (
        AppDbContext Db,
        AuthController Controller,
        Mock<ITokenService> Tokens
    ) CreateController()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options);

        var tokens = new Mock<ITokenService>();

        tokens.Setup(t => t.CreateToken(It.IsAny<User>()))
            .Returns(("test-jwt-token", DateTime.UtcNow.AddHours(1)));

        var google = Options.Create(new GoogleOptions
        {
            ClientId = "test-client-id"
        });

        var controller = new AuthController(
            db,
            tokens.Object,
            google
        );

        return (db, controller, tokens);
    }

    private static User CreateUser(
        string email = "owner@stockpilot.com",
        string password = "Password123!",
        bool isActive = true)
    {
        return new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            FirstName = "John",
            Surname = "Doe",
            Role = UserRole.Owner,
            IsActive = isActive,
            IsFirstLogin = false,
            StoreId = 1
        };
    }

    private static void SetUserClaim(
        AuthController controller,
        string subject)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        new[]
                        {
                            new Claim("sub", subject)
                        },
                        "TestAuthentication"))
            }
        };
    }

    // LOGIN TESTS

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOk()
    {
        var (db, controller, tokens) = CreateController();

        using (db)
        {
            var user = CreateUser();

            db.Users.Add(user);
            await db.SaveChangesAsync();

            var request = new LoginRequest
            {
                Email = "owner@stockpilot.com",
                Password = "Password123!"
            };

            var result = await controller.Login(request);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var response = Assert.IsType<LoginResponse>(ok.Value);

            Assert.Equal("test-jwt-token", response.Token);
            Assert.Equal(user.Id, response.User.Id);
            Assert.Equal("Owner", response.User.Role);

            tokens.Verify(
                t => t.CreateToken(It.Is<User>(u => u.Id == user.Id)),
                Times.Once);
        }
    }

    [Fact]
    public async Task Login_WithIncorrectPassword_ReturnsUnauthorized()
    {
        var (db, controller, _) = CreateController();

        using (db)
        {
            db.Users.Add(CreateUser());
            await db.SaveChangesAsync();

            var request = new LoginRequest
            {
                Email = "owner@stockpilot.com",
                Password = "WrongPassword!"
            };

            var result = await controller.Login(request);

            Assert.IsType<UnauthorizedObjectResult>(result.Result);
        }
    }

    [Fact]
    public async Task Login_WithNonexistentEmail_ReturnsUnauthorized()
    {
        var (db, controller, _) = CreateController();

        using (db)
        {
            var request = new LoginRequest
            {
                Email = "unknown@stockpilot.com",
                Password = "Password123!"
            };

            var result = await controller.Login(request);

            Assert.IsType<UnauthorizedObjectResult>(result.Result);
        }
    }

    [Fact]
    public async Task Login_WithInactiveAccount_ReturnsUnauthorized()
    {
        var (db, controller, _) = CreateController();

        using (db)
        {
            db.Users.Add(CreateUser(isActive: false));
            await db.SaveChangesAsync();

            var request = new LoginRequest
            {
                Email = "owner@stockpilot.com",
                Password = "Password123!"
            };

            var result = await controller.Login(request);

            Assert.IsType<UnauthorizedObjectResult>(result.Result);
        }
    }

    [Fact]
    public async Task Login_WithUppercaseAndWhitespaceEmail_ReturnsOk()
    {
        var (db, controller, _) = CreateController();

        using (db)
        {
            db.Users.Add(CreateUser());
            await db.SaveChangesAsync();

            var request = new LoginRequest
            {
                Email = "  OWNER@STOCKPILOT.COM  ",
                Password = "Password123!"
            };

            var result = await controller.Login(request);

            Assert.IsType<OkObjectResult>(result.Result);
        }
    }

    // CURRENT USER TESTS

    [Fact]
    public async Task Me_WithValidUserId_ReturnsUser()
    {
        var (db, controller, _) = CreateController();

        using (db)
        {
            var user = CreateUser();

            db.Users.Add(user);
            await db.SaveChangesAsync();

            SetUserClaim(controller, user.Id.ToString());

            var result = await controller.Me();

            var returnedUser = Assert.IsType<UserDto>(result.Value);

            Assert.Equal(user.Id, returnedUser.Id);
            Assert.Equal(user.Email, returnedUser.Email);
            Assert.Equal("Owner", returnedUser.Role);
        }
    }

    [Fact]
    public async Task Me_WithInvalidSubject_ReturnsUnauthorized()
    {
        var (db, controller, _) = CreateController();

        using (db)
        {
            SetUserClaim(controller, "invalid-id");

            var result = await controller.Me();

            Assert.IsType<UnauthorizedResult>(result.Result);
        }
    }

    [Fact]
    public async Task Me_WithNonexistentUser_ReturnsUnauthorized()
    {
        var (db, controller, _) = CreateController();

        using (db)
        {
            SetUserClaim(controller, "999");

            var result = await controller.Me();

            Assert.IsType<UnauthorizedResult>(result.Result);
        }
    }

    [Fact]
    public async Task Me_WithInactiveAccount_ReturnsUnauthorized()
    {
        var (db, controller, _) = CreateController();

        using (db)
        {
            var user = CreateUser(isActive: false);

            db.Users.Add(user);
            await db.SaveChangesAsync();

            SetUserClaim(controller, user.Id.ToString());

            var result = await controller.Me();

            Assert.IsType<UnauthorizedResult>(result.Result);
        }
    }
}
