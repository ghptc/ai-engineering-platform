using System.Text.RegularExpressions;
namespace AIEngineeringPlatform.Infrastructure;

public static partial class SecretRedactor
{
    public static string Redact(string value)
    {
        value = PrivateKey().Replace(value, "[REDACTED PRIVATE KEY]");
        value = SensitiveAssignment().Replace(value, "$1[REDACTED]");
        value = CredentialToken().Replace(value, "[REDACTED]");
        value = Authorization().Replace(value, "$1[REDACTED]");
        value = ConnectionCredentials().Replace(value, "$1[REDACTED]@");
        return UrlCredentials().Replace(value, "$1[REDACTED]@");
    }

    [GeneratedRegex(@"-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----[\s\S]*?-----END (?:[A-Z]+ )?PRIVATE KEY-----", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKey();
    [GeneratedRegex(@"(?i)((?:OPENAI_API_KEY|GITHUB_TOKEN|GH_TOKEN|AWS_SECRET_ACCESS_KEY|PASSWORD|CLIENT_SECRET|ACCESS_TOKEN|TOKEN)\s*[""']?\s*[=:]\s*[""']?)[^\s""',;}]+", RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveAssignment();
    [GeneratedRegex(@"(https?://)[^\s/@]+@", RegexOptions.CultureInvariant)]
    private static partial Regex UrlCredentials();
    [GeneratedRegex(@"\b(?:sk-[A-Za-z0-9_-]{16,}|gh[pousr]_[A-Za-z0-9_]{16,}|github_pat_[A-Za-z0-9_]{16,}|AKIA[A-Z0-9]{16})\b", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialToken();

    [GeneratedRegex(@"(?i)(authorization\s*[:=]\s*bearer\s+)[^\s""']+", RegexOptions.CultureInvariant)]
    private static partial Regex Authorization();

    [GeneratedRegex(@"(mongodb(?:\+srv)?://)[^\s/@:]+:[^\s/@]+@", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionCredentials();
}
