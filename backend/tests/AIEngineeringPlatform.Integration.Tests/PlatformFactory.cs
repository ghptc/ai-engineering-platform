using AIEngineeringPlatform.Api;
using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Cryptography;
using System.Text;
namespace AIEngineeringPlatform.Integration.Tests;

public sealed class PlatformFactory : WebApplicationFactory<Program>
{
    public const string Password = "testing-owner-credential-2026";
    public string Database { get; } = "aep_test_" + Guid.NewGuid().ToString("N");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/AIEngineeringPlatform.Api")));
        builder.UseSetting("MONGODB_CONNECTION_STRING", Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028");
        builder.UseSetting("MONGODB_DATABASE", Database);
        var salt = Encoding.UTF8.GetBytes("test-only-salt-32-bytes-1234567890");
        var hash = "600000:" + Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(Password, salt, 600000, HashAlgorithmName.SHA256, 32));
        builder.UseSetting("APPLICATION_OWNER_CREDENTIAL_SECRET", hash);
        builder.UseSetting("OPENAI_DEFAULT_MODEL", "deterministic-test-model");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider, DeterministicModel>();
            services.RemoveAll<IExecutionSandbox>(); services.AddSingleton<IExecutionSandbox, DeterministicSandbox>();
            services.RemoveAll<ISourceControl>(); services.AddSingleton<ISourceControl, DeterministicSourceControl>();
        });
    }
}
