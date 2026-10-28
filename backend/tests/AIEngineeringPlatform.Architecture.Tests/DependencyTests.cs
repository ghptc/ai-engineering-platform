using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Application;
using Xunit;
namespace AIEngineeringPlatform.Architecture.Tests;

public class DependencyTests
{
    [Fact]
    public void DomainHasNoOuterDependencies()
    {
        var references = typeof(Project).Assembly.GetReferencedAssemblies().Select(x => x.Name!).ToArray();
        Assert.DoesNotContain(references, x => x.Contains("Mongo") || x.Contains("OpenAI") || x.Contains("AspNet") || x.StartsWith("AIEngineeringPlatform."));
    }
    [Fact]
    public void ApplicationDoesNotReferenceInfrastructureOrApi()
    {
        Assert.DoesNotContain(typeof(PlatformService).Assembly.GetReferencedAssemblies(), x => x.Name!.Contains("Infrastructure") || x.Name!.EndsWith(".Api") || x.Name!.Contains("MongoDB") || x.Name == "OpenAI");
    }
}
