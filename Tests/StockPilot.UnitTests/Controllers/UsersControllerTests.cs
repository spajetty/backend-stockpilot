
using System.Security.Claims;
using backend_stockpilot.Controllers;
using backend_stockpilot.Data;
using backend_stockpilot.Dtos.Users;
using backend_stockpilot.Models;
using backend_stockpilot.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace StockPilot.UnitTests.Controllers;

public class UsersControllerTests
{
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<ICurrentStore> _currentStore = new();

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private UsersController CreateController(AppDbContext db)
    {
        var controller = new UsersController(
            db,
            _email.Object,
            _currentStore.Object,
            NullLogger<UsersController>.Instance);

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim("name", "Test Owner")
            },
            "TestAuthentication");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }

    private static Store CreateStore(bool isActive = true)
    {
        return new Store
        {
            Code = "STORE-001",
            Name = "Main Store",
            IsActive = isActive
        };
    }

    private static User CreateUser(
        string email,
        DateTime createdAt,
        Store? store = null)
    {
        return new User
        {
            Email = email,
            FirstName = "John",
            Surname = "Doe",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123"),
            Role = UserRole.Cashier,
            IsActive = true,
            IsFirstLogin = false,
            CreatedAt = createdAt,
            Store = store
        };
    }

    private static CreateUserRequest CreateRequest(
        string email = "employee@example.com",
        UserRole role = UserRole.Cashier,
        int storeId = 1)
    {
        return new CreateUserRequest
        {
            Email = email,
            Role = role,
            StoreId = storeId
        };
    }

    private void SetupEmailSuccess()
    {
        _email.Setup(x => x.SendInvitationAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
        .Callback<string, string, string, string, CancellationToken>(
            (email, role, password, invitedBy, ct) =>
                _capturedTemporaryPassword = password)
        .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task List_ReturnsUsersNewestFirst()
    {
        using var db = CreateDbContext();

        var store = CreateStore();
        db.Stores.Add(store);

        db.Users.AddRange(
            CreateUser("older@example.com",
                new DateTime(2026, 1, 1), store),
            CreateUser("newer@example.com",
                new DateTime(2026, 2, 1), store));

        await db.SaveChangesAsync();

        var controller = CreateController(db);

        var result = await controller.List(CancellationToken.None);

        var users = Assert.IsType<List<UserSummaryDto>>(result.Value);

        Assert.Equal(2, users.Count);
        Assert.Equal("newer@example.com", users[0].Email);
        Assert.Equal("older@example.com", users[1].Email);
        Assert.Equal("Main Store", users[0].StoreName);
    }

    [Fact]
    public async Task Create_ValidRequest_CreatesEmployeeAndSendsInvitation()
    {
        using var db = CreateDbContext();

        var store = CreateStore();
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        _currentStore.Setup(x => x.UserId).Returns(10);
        SetupEmailSuccess();

        var controller = CreateController(db);

        var request = CreateRequest();

        var result = await controller.Create(
            request, CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);

        var returnedUser = Assert.IsType<UserSummaryDto>(response.Value);

        Assert.Equal("employee@example.com", returnedUser.Email);
        Assert.Equal("Cashier", returnedUser.Role);
        Assert.True(returnedUser.IsFirstLogin);
        Assert.True(returnedUser.IsActive);
        Assert.Equal(store.Id, returnedUser.StoreId);
        Assert.Equal("Main Store", returnedUser.StoreName);

        var savedUser = await db.Users.FirstAsync();

        Assert.Equal("employee@example.com", savedUser.Email);
        Assert.Equal(UserRole.Cashier, savedUser.Role);
        Assert.True(savedUser.IsFirstLogin);
        Assert.Equal(10, savedUser.InvitedById);
        Assert.True(
            BCrypt.Net.BCrypt.Verify(
                GetTemporaryPasswordFromEmail(),
                savedUser.PasswordHash));

        _email.Verify(x => x.SendInvitationAsync(
                "employee@example.com",
                "Cashier",
                It.IsAny<string>(),
                "Test Owner",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_OwnerRole_ReturnsBadRequest()
    {
        using var db = CreateDbContext();

        var controller = CreateController(db);

        var result = await controller.Create(
            CreateRequest(role: UserRole.Owner),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.Users.ToListAsync());

        _email.Verify(x => x.SendInvitationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_InactiveStore_ReturnsBadRequest()
    {
        using var db = CreateDbContext();

        db.Stores.Add(CreateStore(isActive: false));
        await db.SaveChangesAsync();

        var controller = CreateController(db);

        var result = await controller.Create(
            CreateRequest(),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task Create_NonexistentStore_ReturnsBadRequest()
    {
        using var db = CreateDbContext();

        var controller = CreateController(db);

        var result = await controller.Create(
            CreateRequest(storeId: 999),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task Create_DuplicateEmail_ReturnsConflict()
    {
        using var db = CreateDbContext();

        var store = CreateStore();
        db.Stores.Add(store);

        db.Users.Add(CreateUser(
            "employee@example.com",
            DateTime.UtcNow,
            store));

        await db.SaveChangesAsync();

        var controller = CreateController(db);

        var result = await controller.Create(
            CreateRequest(),
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Single(await db.Users.ToListAsync());

        _email.Verify(x => x.SendInvitationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_EmailWithWhitespaceAndUppercase_NormalizesEmail()
    {
        using var db = CreateDbContext();

        var store = CreateStore();
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        _currentStore.Setup(x => x.UserId).Returns(10);
        SetupEmailSuccess();

        var controller = CreateController(db);

        await controller.Create(
            CreateRequest("  EMPLOYEE@EXAMPLE.COM  "),
            CancellationToken.None);

        var savedUser = await db.Users.FirstAsync();

        Assert.Equal("employee@example.com", savedUser.Email);

        _email.Verify(x => x.SendInvitationAsync(
                "employee@example.com",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_EmailFailure_RollsBackCreatedAccount()
    {
        using var db = CreateDbContext();

        var store = CreateStore();
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        _email.Setup(x => x.SendInvitationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Email service unavailable"));

        var controller = CreateController(db);

        var result = await controller.Create(
            CreateRequest(),
            CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result.Result);

        Assert.Equal(StatusCodes.Status502BadGateway, response.StatusCode);
        Assert.Empty(await db.Users.ToListAsync());
    }

    private string _capturedTemporaryPassword = string.Empty;

    private string GetTemporaryPasswordFromEmail()
    {
        return _capturedTemporaryPassword;
    }
}