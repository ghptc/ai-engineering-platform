using AIEngineeringPlatform.Integration.Tests;
using Microsoft.Extensions.DependencyInjection;
using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
var contentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/AIEngineeringPlatform.Api"));
Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_AIENGINEERINGPLATFORM_API", contentRoot);
Environment.SetEnvironmentVariable("ASPNETCORE_CONTENTROOT", contentRoot);
await using var factory = new PlatformFactory();
factory.UseKestrel(5088);
using var client = factory.CreateClient();
var store = factory.Services.GetRequiredService<IPlatformStore>();
for (var attempt = 0; attempt < 30; attempt++) { try { await store.InitializeAsync(default); break; } catch when (attempt < 29) { await Task.Delay(1000); } }
Console.WriteLine("E2E fixture ready on http://localhost:5088; adapters are deterministic test fixtures only.");
await Task.Delay(Timeout.InfiniteTimeSpan);
