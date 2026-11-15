
using backend_stockpilot.Services;

namespace StockPilot.UnitTests.Services;

public class PasswordPolicyTests
{
    private const string AllowedCharacters =
        "ABCDEFGHJKLMNPQRSTUVWXYZ" +
        "abcdefghijkmnopqrstuvwxyz" +
        "23456789" +
        "!@#$%*?";

    // TEMPORARY PASSWORD GENERATION TESTS

    [Fact]
    public void GenerateTemporary_ShouldReturnNonEmptyPassword()
    {
        var password = PasswordPolicy.GenerateTemporary();

        Assert.False(string.IsNullOrWhiteSpace(password));
    }

    [Fact]
    public void GenerateTemporary_ShouldUseDefaultLength()
    {
        var password = PasswordPolicy.GenerateTemporary();

        Assert.Equal(12, password.Length);
    }

    [Fact]
    public void GenerateTemporary_ShouldRespectCustomLength()
    {
        var password = PasswordPolicy.GenerateTemporary(16);

        Assert.Equal(16, password.Length);
    }

    [Fact]
    public void GenerateTemporary_ShouldIncludeRequiredCharacterTypes()
    {
        var password = PasswordPolicy.GenerateTemporary();

        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, c => "!@#$%*?".Contains(c));
    }

    [Fact]
    public void GenerateTemporary_ShouldUseOnlyAllowedCharacters()
    {
        var password = PasswordPolicy.GenerateTemporary();

        Assert.All(password, character =>
            Assert.Contains(character, AllowedCharacters));
    }

    // PASSWORD VALIDATION TESTS

    [Fact]
    public void Validate_ShouldRejectPassword_WhenTooShort()
    {
        var password = "Abc123!";

        var result = PasswordPolicy.Validate(password);

        Assert.Equal(
            "Password must be at least 8 characters.",
            result);
    }

    [Fact]
    public void Validate_ShouldRejectPassword_WhenTooLong()
    {
        var password = new string('A', 129);

        var result = PasswordPolicy.Validate(password);

        Assert.Equal(
            "Password must be 128 characters or fewer.",
            result);
    }

    [Fact]
    public void Validate_ShouldRejectPassword_WhenNoLetter()
    {
        var password = "12345678";

        var result = PasswordPolicy.Validate(password);

        Assert.Equal(
            "Password must include a letter.",
            result);
    }

    [Fact]
    public void Validate_ShouldRejectPassword_WhenNoNumber()
    {
        var password = "PasswordOnly";

        var result = PasswordPolicy.Validate(password);

        Assert.Equal(
            "Password must include a number.",
            result);
    }

    [Fact]
    public void Validate_ShouldAcceptValidPassword()
    {
        var password = "StockPilot123";

        var result = PasswordPolicy.Validate(password);

        Assert.Null(result);
    }

    [Fact]
    public void Validate_ShouldAcceptPassword_AtMinimumLength()
    {
        var password = "Abcdef12";

        var result = PasswordPolicy.Validate(password);

        Assert.Null(result);
    }

    [Fact]
    public void Validate_ShouldAcceptPassword_AtMaximumLength()
    {
        var password = new string('A', 127) + "1";

        var result = PasswordPolicy.Validate(password);

        Assert.Null(result);
    }
}