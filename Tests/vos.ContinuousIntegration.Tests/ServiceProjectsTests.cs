using System;
using System.IO;
using System.Linq;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

public class ServiceProjectsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "vos-service-projects-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void AServiceHoldingSource_IsCounted()
    {
        GivenFile("vos.Service.Forage/Program.cs", "// the service");

        Assert.Equal(["vos.Service.Forage"], ServiceProjects.NamesUnder(_root));
    }

    [Fact]
    public void AServiceWrittenInAnotherLanguage_IsCounted()
    {
        GivenFile("vos.Service.Go.Echo/main.go", "package main");

        Assert.Equal(["vos.Service.Go.Echo"], ServiceProjects.NamesUnder(_root));
    }

    [Fact]
    public void ADirectoryHoldingOnlyBuildOutput_IsNotAService()
    {
        GivenFile("vos.Service.Confluence/obj/project.assets.json", "{}");
        GivenFile("vos.Service.Confluence/bin/Debug/net10.0/apphost", "");

        Assert.Empty(ServiceProjects.NamesUnder(_root));
    }

    [Fact]
    public void AServiceThatStillHasBuildOutputBesideItsSource_IsCounted()
    {
        GivenFile("vos.Service.Forage/Program.cs", "// the service");
        GivenFile("vos.Service.Forage/obj/project.assets.json", "{}");

        Assert.Equal(["vos.Service.Forage"], ServiceProjects.NamesUnder(_root));
    }

    [Fact]
    public void ADirectoryHoldingOnlyToolingState_IsNotAService()
    {
        GivenFile("vos.Service.Gone/.vs/settings.json", "{}");

        Assert.Empty(ServiceProjects.NamesUnder(_root));
    }

    [Fact]
    public void ADirectoryThatIsNotAService_IsIgnored()
    {
        GivenFile("vos.Taproot/Program.cs", "// the command line");

        Assert.Empty(ServiceProjects.NamesUnder(_root));
    }

    private void GivenFile(string relativePath, string contents)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    // Every guard over the services rests on this, so a rule that quietly matched nothing would let
    // all of them pass while examining an empty set.
    [Fact]
    public void TheCheckoutItself_HoldsServices()
    {
        var services = ServiceProjects.NamesUnder(RepositoryRoot.Find()).ToList();

        Assert.NotEmpty(services);
        Assert.Contains("vos.Service.Shared", services);
    }
}
