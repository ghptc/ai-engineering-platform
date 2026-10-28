using System.Text.RegularExpressions;
namespace AIEngineeringPlatform.Infrastructure;

public static partial class SecretRedactor
{
    public static string Redact(string value)
    {
        value = CredentialToken().Replace(value, "[REDACTED]");
        value = Authorization().Replace(value, "$1[REDACTED]");
        return ConnectionCredentials().Replace(value, "$1[REDACTED]@");
    }

    [GeneratedRegex(@"\b(?:sk-[A-Za-z0-9_-]{16,}|gh[pousr]_[A-Za-z0-9_]{16,}|github_pat_[A-Za-z0-9_]{16,}|AKIA[A-Z0-9]{16})\b", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialToken();

    [GeneratedRegex(@"(?i)(authorization\s*[:=]\s*bearer\s+)[^\s""']+", RegexOptions.CultureInvariant)]
    private static partial Regex Authorization();

    [GeneratedRegex(@"(mongodb(?:\+srv)?://)[^\s/@:]+:[^\s/@]+@", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionCredentials();
}
