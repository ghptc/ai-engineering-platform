using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
namespace AIEngineeringPlatform.Api;

public sealed class OwnerOptions { public string Mode { get; set; } = "Owner"; public string CredentialHash { get; set; } = ""; }
public static class OwnerCredentials
{
    public static bool ValidFormat(string hash)
    {
        try { var parts = hash.Split(':'); return parts.Length == 3 && int.TryParse(parts[0], out var iterations) && iterations is >= 600000 and <= 2000000 && Convert.FromBase64String(parts[1]).Length >= 16 && Convert.FromBase64String(parts[2]).Length == 32; } catch (FormatException) { return false; }
    }
    public static bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || password.Length is < 12 or > 1024 || !ValidFormat(hash)) return false;
        var parts = hash.Split(':');
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(parts[1]), int.Parse(parts[0]), HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(parts[2]));
    }
}
