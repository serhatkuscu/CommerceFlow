using System.Xml.Linq;

namespace CommerceFlow.ArchitectureTests;

// Deterministic, .csproj-file-based checks -- NOT reflection/IL-based like NetArchTest. These
// catch a risk NetArchTest structurally cannot: a <ProjectReference> that exists in the file but
// is never actually used by any type, which NetArchTest's type-dependency walk would never flag
// (it inspects compiled type usage, not the project file). ControllerDependencyTests supplements
// these checks with an IL-based rule now that the API contains a real controller.
public class ProjectReferenceGraphTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Ordering_Domain_HasNoProjectReferences()
    {
        var references = GetProjectReferences(
            "src/Modules/Ordering/CommerceFlow.Modules.Ordering.Domain/CommerceFlow.Modules.Ordering.Domain.csproj");

        Assert.Empty(references);
    }

    [Fact]
    public void Ordering_Domain_HasNoPackageReferences()
    {
        var packages = GetPackageReferences(
            "src/Modules/Ordering/CommerceFlow.Modules.Ordering.Domain/CommerceFlow.Modules.Ordering.Domain.csproj");

        Assert.Empty(packages);
    }

    [Fact]
    public void Inventory_Domain_HasNoProjectReferences()
    {
        var references = GetProjectReferences(
            "src/Modules/Inventory/CommerceFlow.Modules.Inventory.Domain/CommerceFlow.Modules.Inventory.Domain.csproj");

        Assert.Empty(references);
    }

    [Fact]
    public void Inventory_Domain_HasNoPackageReferences()
    {
        var packages = GetPackageReferences(
            "src/Modules/Inventory/CommerceFlow.Modules.Inventory.Domain/CommerceFlow.Modules.Inventory.Domain.csproj");

        Assert.Empty(packages);
    }

    [Fact]
    public void Application_ReferencesOnlyDomain()
    {
        var references = GetProjectReferences(
            "src/Modules/Ordering/CommerceFlow.Modules.Ordering.Application/CommerceFlow.Modules.Ordering.Application.csproj");

        Assert.Equal(
            new[] { "CommerceFlow.Modules.Ordering.Domain" }.OrderBy(r => r),
            references.OrderBy(r => r));
    }

    [Fact]
    public void Infrastructure_ReferencesOnlyDomainAndApplication()
    {
        var references = GetProjectReferences(
            "src/Modules/Ordering/CommerceFlow.Modules.Ordering.Infrastructure/CommerceFlow.Modules.Ordering.Infrastructure.csproj");

        Assert.Equal(
            new[] { "CommerceFlow.Modules.Ordering.Application", "CommerceFlow.Modules.Ordering.Domain" }.OrderBy(r => r),
            references.OrderBy(r => r));
    }

    [Fact]
    public void Api_ReferencesOnlyOrderingApplicationAndInfrastructure()
    {
        var references = GetProjectReferences("src/CommerceFlow.Api/CommerceFlow.Api.csproj");

        Assert.Equal(
            new[] { "CommerceFlow.Modules.Ordering.Application", "CommerceFlow.Modules.Ordering.Infrastructure" }.OrderBy(r => r),
            references.OrderBy(r => r));
    }

    private static IReadOnlyList<string> GetProjectReferences(string relativeCsprojPath)
    {
        var document = XDocument.Load(Path.Combine(RepoRoot, relativeCsprojPath));
        return document.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value))
            .ToList();
    }

    private static IReadOnlyList<string> GetPackageReferences(string relativeCsprojPath)
    {
        var document = XDocument.Load(Path.Combine(RepoRoot, relativeCsprojPath));
        return document.Descendants("PackageReference")
            .Select(e => e.Attribute("Include")!.Value)
            .ToList();
    }

    // Walks up from the test assembly's own output directory until it finds CommerceFlow.sln --
    // reliable regardless of whether the test runner's working directory is the repo root, the
    // bin/ output folder, or something else (dotnet test, VS Test Explorer, and CI all differ).
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CommerceFlow.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate CommerceFlow.sln by walking up from AppContext.BaseDirectory.");
    }
}
