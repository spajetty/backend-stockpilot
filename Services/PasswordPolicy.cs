using System.Security.Cryptography;

namespace backend_stockpilot.Services;

public static class PasswordPolicy
{
    public const int MinLength = 8;

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%*?";

    public static string GenerateTemporary(int length = 12)
    {
        var all = Upper + Lower + Digits + Symbols;
        var chars = new List<char> { Pick(Upper), Pick(Lower), Pick(Digits), Pick(Symbols) };
        while (chars.Count < length) chars.Add(Pick(all));

        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }

    public static string? Validate(string password)
    {
        if (password.Length < MinLength) return $"Password must be at least {MinLength} characters.";
        if (password.Length > 128) return "Password must be 128 characters or fewer.";
        if (!password.Any(char.IsLetter)) return "Password must include a letter.";
        if (!password.Any(char.IsDigit)) return "Password must include a number.";
        return null;
    }

    private static char Pick(string source) => source[RandomNumberGenerator.GetInt32(source.Length)];
}