using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace fx2dotnet;

[McpServerToolType]
internal static class Tools
{
    [McpServerTool]
    [Description("Takes a list of NuGet packages with current versions and returns the subset recommended for upgrade to meet minimum .NET Core/.NET or .NET Standard support.")]
    public static async Task<string> FindRecommendedPackageUpgrades(
        [Description("Optional workspace root directory used for default NuGet configuration resolution when nugetConfigPath is not provided.")]
        string? workspaceDirectory,
        [Description("Optional full path to a specific nuget.config file. If null or empty, default NuGet config resolution is used from workspaceDirectory.")]
        string? nugetConfigPath,
        [Description("Packages to evaluate. Each item should include packageId and currentVersion.")]
        IReadOnlyList<PackageVersionInput> packages,
        [Description("When true, prerelease versions are included while searching for the minimum supported version.")]
        bool includePrerelease = false)
    {
        if (packages is null || packages.Count == 0)
        {
            return JsonSerializer.Serialize(new PackageUpgradeRecommendationResult(
                Array.Empty<PackageUpgradeRecommendation>(),
                "packages is required and must contain at least one item."));
        }

        if (packages.Any(p => string.IsNullOrWhiteSpace(p.PackageId)))
        {
            return JsonSerializer.Serialize(new PackageUpgradeRecommendationResult(
                Array.Empty<PackageUpgradeRecommendation>(),
                "Each package item must include a non-empty packageId."));
        }

        var result = await NuGetPackageSupportService.FindRecommendedUpgradesAsync(
            workspaceDirectory,
            nugetConfigPath,
            packages,
            includePrerelease);

        return JsonSerializer.Serialize(result);
    }
}
