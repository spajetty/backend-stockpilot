
using System.Security.Claims;
using backend_stockpilot.Controllers;
using backend_stockpilot.Data;
using backend_stockpilot.DTOs.Onboarding;
using backend_stockpilot.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace StockPilot.UnitTests.Controllers;

public class OnboardingControllerTests
{
    private static (AppDbContext Db, OnboardingController Controller)
        CreateController()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options);
        var controller = new OnboardingController(db);

        return (db, controller);
    }

    private static User CreateUser(
        bool isActive = true,
        bool isFirstLogin = true,
        string password = "Temporary123")
    {
        return new User
        {
            Email = "employee@stockpilot.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = UserRole.Manager,
            IsActive = isActive,
            IsFirstLogin = isFirstLogin
        };
    }

    private static CompleteSetupRequest CreateRequest()
    {
        return new CompleteSetupRequest
        {
            FirstName = "Jane",
            MiddleName = "Marie",
            Surname = "Doe",
            Birthday = new DateOnly(2000, 5, 15),
            Gender = Gender.Female,
            NewPassword = "NewSecurePass123!"
        };
    }

    private static void SetUserClaim(
        OnboardingController controller,
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

    // AUTHENTICATION TESTS

    [Fact]
    public async Task Complete_WithInvalidSubject_ReturnsUnauthorized()
    {
        var (db, controller) = CreateController();

        using (db)
        {
            SetUserClaim(controller, "invalid-id");

            var result = await controller.Complete(CreateRequest());

            Assert.IsType<UnauthorizedResult>(result.Result);
        }
    }

    [Fact]
    public async Task Complete_WithNonexistentUser_ReturnsUnauthorized()
    {
        var (db, controller) = CreateController();

        using (db)
        {
            SetUserClaim(controller, "999");

            var result = await controller.Complete(CreateRequest());

            Assert.IsType<UnauthorizedResult>(result.Result);
        }
    }

    [Fact]
    public async Task Complete_WithInactiveUser_ReturnsUnauthorized()
    {
        var (db, controller) = CreateController();

        using (db)
        {
            var user = CreateUser(isActive: false);

            db.Users.Add(user);
            await db.SaveChangesAsync();

            SetUserClaim(controller, user.Id.ToString());

            var result = await controller.Complete(CreateRequest());

            Assert.IsType<UnauthorizedResult>(result.Result);
        }
    }

    // SETUP STATUS TESTS

    [Fact]
    public async Task Complete_WhenSetupAlreadyCompleted_ReturnsConflict()
    {
        var (db, controller) = CreateController();

        using (db)
        {
            var user = CreateUser(isFirstLogin: false);

            db.Users.Add(user);
            await db.SaveChangesAsync();

            SetUserClaim(controller, user.Id.ToString());

            var result = await controller.Complete(CreateRequest());

            Assert.IsType<ConflictObjectResult>(result.Result);
        }
    }

    // BIRTHDAY VALIDATION TESTS

    [Fact]
    public async Task Complete_WithFutureBirthday_ReturnsBadRequest()
    {
        var (db, controller) = CreateController();

        using (db)
        {
            var user = CreateUser();

            db.Users.Add(user);
            await db.SaveChangesAsync();

            SetUserClaim(controller, user.Id.ToString());

            var request = CreateRequest();
            request.Birthday = DateOnly.FromDateTime(DateTime.UtcNow)
                .AddDays(1);

            var result = await controller.Complete(request);

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
    }

    [Fact]
    public async Task Complete_WithBirthdayOlderThan120Years_ReturnsBadRequest()
    {
        var (db, controller) = CreateController();

        using (db)
        {
            var user = CreateUser();

            db.Users.Add(user);
            await db.SaveChangesAsync();

            SetUserClaim(controller, user.Id.ToString());

            var request = CreateRequest();
            request.Birthday = DateOnly.FromDateTime(DateTime.UtcNow)
                .AddYears(-121);

            var result = await controller.Complete(request);

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
    }

    // PASSWORD VALIDATION TESTS

    [Fact]
    public async Task Complete_WithWeakPassword_ReturnsBadRequest()
    {
        var (db, controller) = CreateController();

        using (db)
        {
            var user = CreateUser();

            db.Users.Add(user);
            await db.SaveChangesAsync();

            SetUserClaim(controller, user.Id.ToString());

            var request = CreateRequest();
            request.NewPassword = "12345678";

            var result = await controller.Complete(request);

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
    }

    [Fact]
    public async Task Complete_WithTemporaryPassword_ReturnsBadRequest()
    {
        var (db, controller) = CreateController();

        using (db)
        {
            var user = CreateUser(password: "Temporary123");

            db.Users.Add(user);
            await db.SaveChangesAsync();

            SetUserClaim(controller, user.Id.ToString());

            var request = CreateRequest();
            request.NewPassword = "Temporary123";

            var result = await controller.Complete(request);

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
    }

    // SUCCESSFUL SETUP TESTS

    [Fact]
    public async Task Complete_WithValidRequest_UpdatesUserSuccessfully()
    {
        var (db, controller) = CreateController();

        using (db)
        {
            var user = CreateUser();

            db.Users.Add(user);
            await db.SaveChangesAsync();

            SetUserClaim(controller, user.Id.ToString());

            var request = CreateRequest();

            var result = await controller.Complete(request);

            var returnedUser = Assert.IsType<backend_stockpilot.Dtos.Auth.UserDto>(
                result.Value);

            Assert.Equal("Jane Doe", returnedUser.FullName);

            var updatedUser = await db.Users.FindAsync(user.Id);

            Assert.NotNull(updatedUser);
            Assert.Equal("Jane", updatedUser.FirstName);
            Assert.Equal("Marie", updatedUser.MiddleName);
            Assert.Equal("Doe", updatedUser.Surname);
            Assert.Equal(new DateOnly(2000, 5, 15), updatedUser.Birthday);
            Assert.Equal(Gender.Female, updatedUser.Gender);

            Assert.False(updatedUser.IsFirstLogin);
            Assert.NotNull(updatedUser.SetupCompletedAt);

            Assert.True(
                BCrypt.Net.BCrypt.Verify(
                    request.NewPassword,
                    updatedUser.PasswordHash));

            Assert.False(
                BCrypt.Net.BCrypt.Verify(
                    "Temporary123",
                    updatedUser.PasswordHash));
        }
    }

    [Fact]
    public async Task Complete_WithEmptyMiddleName_SavesNullMiddleName()
    {
        var (db, controller) = CreateController();

        using (db)
        {
            var user = CreateUser();

            db.Users.Add(user);
            await db.SaveChangesAsync();

            SetUserClaim(controller, user.Id.ToString());

            var request = CreateRequest();
            request.MiddleName = "   ";

            var result = await controller.Complete(request);

            Assert.NotNull(result.Value);

            var updatedUser = await db.Users.FindAsync(user.Id);

            Assert.NotNull(updatedUser);
            Assert.Null(updatedUser.MiddleName);
        }
    }
}