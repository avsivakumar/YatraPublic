using System.Xml.Linq;

namespace Yatra.Engine.Tests;

public sealed class EngineArchitectureTests
{
    [Fact]
    public void EngineProject_DoesNotReferenceConcreteOrchestrationProject()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Yatra.Engine",
            "Yatra.Engine.csproj");

        var project = XDocument.Load(projectPath);
        var projectReferences = project
            .Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!.Replace('\\', '/'))
            .ToList();

        Assert.DoesNotContain(
            projectReferences,
            include => include.Contains(
                "Yatra.Orchestration/Yatra.Orchestration.csproj",
                StringComparison.OrdinalIgnoreCase));
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Yatra.slnx")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Yatra repository root.");
    }
}
