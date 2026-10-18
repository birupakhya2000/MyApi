using System.Text.RegularExpressions;

namespace MyApi.Api.Auth;

public static class RequestValidation
{
    private static readonly Regex UsernamePattern = new(
        "^[a-z0-9._-]+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public static Dictionary<string, string[]> Validate(RegisterRequest req)
    {
        var errors = new Dictionary<string, string[]>();

        var username = req.Username?.Trim() ?? "";
        if (username.Length is < 3 or > 64 || !UsernamePattern.IsMatch(username))
        {
            errors["username"] = ["Username must be 3-64 characters: letters, digits, '.', '_' or '-'."];
        }

        // The upper bound matters: hashing a huge password is expensive, so an unbounded one is a DoS vector
        if (req.Password is null || req.Password.Length is < 8 or > 128)
        {
            errors["password"] = ["Password must be 8-128 characters."];
        }

        return errors;
    }
}