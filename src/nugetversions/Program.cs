


using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

var builder = Host.CreateApplicationBuilder(args);

// Keep stdout clean for MCP stdio JSON-RPC traffic.
builder.Logging.ClearProviders();

builder.Services
	.AddMcpServer()
	.WithStdioServerTransport()
	.WithToolsFromAssembly();

using var host = builder.Build();
await host.RunAsync();

// ============================================================================
// MCP Server Tool Definitions
// ============================================================================

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

internal static class NuGetPackageSupportService
{
    private static readonly IReadOnlyList<Lazy<INuGetResourceProvider>> Providers = Repository.Provider.GetCoreV3().ToList();

    public static async Task<PackageUpgradeRecommendationResult> FindRecommendedUpgradesAsync(
        string? workspaceDirectory,
        string? nugetConfigPath,
        IReadOnlyList<PackageVersionInput> packages,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        var settingsResult = ResolveSettingsInputs(workspaceDirectory, nugetConfigPath);
        if (settingsResult.Error is not null)
        {
            return new PackageUpgradeRecommendationResult(Array.Empty<PackageUpgradeRecommendation>(), settingsResult.Error);
        }

        var recommendations = new List<PackageUpgradeRecommendation>();

        var loadResult = LoadPackageSources(settingsResult.WorkspaceDirectory!, settingsResult.NuGetConfigPath);
        var sources = loadResult.Error is null ? loadResult.Sources : (IReadOnlyList<PackageSource>)Array.Empty<PackageSource>();

        foreach (var package in packages)
        {
            var currentVersion = package.CurrentVersion?.Trim() ?? string.Empty;
            var hasValidCurrentVersion = TryParseNuGetVersion(currentVersion, out var currentParsed);

            var minSupport = await FindMinimumSupportedVersionAsync(
                package.PackageId,
                settingsResult.WorkspaceDirectory!,
                settingsResult.NuGetConfigPath,
                includePrerelease,
                cancellationToken);

            var legacyFlags = new LegacyPackageFlags(false, false);
            if (hasValidCurrentVersion && sources.Count > 0)
            {
                legacyFlags = await CheckLegacyPackageFlagsAsync(
                    package.PackageId, currentParsed, sources, cancellationToken);
            }

            var needsUpgrade = false;
            string? reason = null;

            if (minSupport.MinimumVersion is not null)
            {
                if (!hasValidCurrentVersion)
                {
                    needsUpgrade = true;
                    reason = "Current version is missing or invalid; review and upgrade to at least the minimum supported version.";
                }
                else if (TryParseNuGetVersion(minSupport.MinimumVersion, out var minimumSupported) && currentParsed < minimumSupported)
                {
                    needsUpgrade = true;
                }
            }

            if (needsUpgrade || legacyFlags.HasLegacyContentFolder || legacyFlags.HasInstallScript)
            {
                recommendations.Add(new PackageUpgradeRecommendation(
                    package.PackageId,
                    package.CurrentVersion,
                    minSupport.MinimumVersion,
                    minSupport.Supports,
                    minSupport.SupportFamilies,
                    minSupport.Feed,
                    legacyFlags.HasLegacyContentFolder,
                    legacyFlags.HasInstallScript,
                    reason));
            }
        }

        return new PackageUpgradeRecommendationResult(recommendations, null);
    }

    public static async Task<PackageSupportResult> FindMinimumSupportedVersionAsync(
        string packageId,
        string? workspaceDirectory,
        string? nugetConfigPath,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        var settingsResult = ResolveSettingsInputs(workspaceDirectory, nugetConfigPath);
        if (settingsResult.Error is not null)
        {
            return new PackageSupportResult(
                packageId,
                includePrerelease,
                null,
                Array.Empty<string>(),
                Array.Empty<string>(),
                null,
                settingsResult.Error);
        }

        var loadResult = LoadPackageSources(settingsResult.WorkspaceDirectory!, settingsResult.NuGetConfigPath);
        if (loadResult.Error is not null)
        {
            return new PackageSupportResult(
                packageId,
                includePrerelease,
                null,
                Array.Empty<string>(),
                Array.Empty<string>(),
                null,
                loadResult.Error);
        }

        var sources = loadResult.Sources;
        if (sources.Count == 0)
        {
            return new PackageSupportResult(
                packageId,
                includePrerelease,
                null,
                Array.Empty<string>(),
                Array.Empty<string>(),
                null,
                "No enabled NuGet package sources were found.");
        }

        SourceCandidate? bestCandidate = null;
        var hadPackageMetadata = false;
        var lastError = default(string);

        using var cache = new SourceCacheContext();

        foreach (var source in sources)
        {
            try
            {
                var repository = new SourceRepository(source, Providers);
                var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);
                var metadata = await metadataResource.GetMetadataAsync(
                    packageId,
                    includePrerelease,
                    includeUnlisted: false,
                    cache,
                    NullLogger.Instance,
                    cancellationToken);

                var ordered = metadata.OrderBy(m => m.Identity.Version).ToList();
                if (ordered.Count == 0)
                {
                    continue;
                }

                hadPackageMetadata = true;
                foreach (var item in ordered)
                {
                    var supported = GetSupportedFrameworks(item);
                    var families = supported
                        .Select(GetFrameworkFamily)
                        .Where(family => family is not null)
                        .Select(family => family!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(family => family, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    if (families.Length == 0)
                    {
                        continue;
                    }

                    var matchingTfms = supported
                        .Where(f => GetFrameworkFamily(f) is not null)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    var candidate = new SourceCandidate(item.Identity.Version, matchingTfms, families, source.Source);
                    if (bestCandidate is null || candidate.Version < bestCandidate.Version)
                    {
                        bestCandidate = candidate;
                    }

                    break;
                }
            }
            catch (Exception ex)
            {
                lastError = $"{source.Source}: {ex.Message}";
            }
        }

        if (bestCandidate is not null)
        {
            return new PackageSupportResult(
                packageId,
                includePrerelease,
                bestCandidate.Version.ToNormalizedString(),
                bestCandidate.Supports,
                bestCandidate.SupportFamilies,
                bestCandidate.Feed,
                null);
        }

        if (hadPackageMetadata)
        {
            return new PackageSupportResult(
                packageId,
                includePrerelease,
                null,
                Array.Empty<string>(),
                Array.Empty<string>(),
                null,
                "No version was found that supports .NET Core/.NET (netcoreapp/netX.Y) or .NET Standard.");
        }

        return new PackageSupportResult(
            packageId,
            includePrerelease,
            null,
            Array.Empty<string>(),
            Array.Empty<string>(),
            null,
            lastError is null ? $"Package '{packageId}' was not found in configured sources." : $"Package lookup failed. Last error: {lastError}");
    }

    private static PackageSourceLoadResult LoadPackageSources(string workspaceDirectory, string? nugetConfigPath)
    {
        ISettings settings;
        if (string.IsNullOrWhiteSpace(nugetConfigPath))
        {
            settings = Settings.LoadDefaultSettings(root: workspaceDirectory);
        }
        else
        {
            var configDirectory = Path.GetDirectoryName(nugetConfigPath);
            var configFileName = Path.GetFileName(nugetConfigPath);
            if (string.IsNullOrWhiteSpace(configDirectory) || string.IsNullOrWhiteSpace(configFileName))
            {
                return new PackageSourceLoadResult(Array.Empty<PackageSource>(), "nugetConfigPath is not a valid file path.");
            }

            settings = Settings.LoadSpecificSettings(configDirectory, configFileName);
        }

        var provider = new PackageSourceProvider(settings);
        var sources = provider
            .LoadPackageSources()
            .Where(s => s.IsEnabled)
            .ToList();

        if (sources.Count == 0)
        {
            sources.Add(new PackageSource("https://api.nuget.org/v3/index.json", "nuget.org"));
        }

        return new PackageSourceLoadResult(sources, null);
    }

    private static SettingsResolution ResolveSettingsInputs(string? workspaceDirectory, string? nugetConfigPath)
    {
        string? normalizedWorkspaceDirectory = null;
        if (!string.IsNullOrWhiteSpace(workspaceDirectory))
        {
            normalizedWorkspaceDirectory = Path.GetFullPath(workspaceDirectory);
            if (!Directory.Exists(normalizedWorkspaceDirectory))
            {
                return new SettingsResolution(null, null, $"workspaceDirectory does not exist: '{workspaceDirectory}'.");
            }
        }

        string? normalizedNuGetConfigPath = null;
        if (!string.IsNullOrWhiteSpace(nugetConfigPath))
        {
            normalizedNuGetConfigPath = Path.GetFullPath(nugetConfigPath);
            if (!File.Exists(normalizedNuGetConfigPath))
            {
                return new SettingsResolution(null, null, $"nugetConfigPath does not exist: '{nugetConfigPath}'.");
            }

            if (!string.Equals(Path.GetFileName(normalizedNuGetConfigPath), "nuget.config", StringComparison.OrdinalIgnoreCase))
            {
                return new SettingsResolution(null, null, $"nugetConfigPath must point to a nuget.config file: '{nugetConfigPath}'.");
            }

            if (normalizedWorkspaceDirectory is null)
            {
                var configDirectory = Path.GetDirectoryName(normalizedNuGetConfigPath);
                if (string.IsNullOrWhiteSpace(configDirectory))
                {
                    return new SettingsResolution(null, null, $"nugetConfigPath is not a valid file path: '{nugetConfigPath}'.");
                }

                normalizedWorkspaceDirectory = configDirectory;
            }
        }

        if (normalizedWorkspaceDirectory is null)
        {
            normalizedWorkspaceDirectory = Directory.GetCurrentDirectory();
        }

        return new SettingsResolution(normalizedWorkspaceDirectory, normalizedNuGetConfigPath, null);
    }

    private static string[] GetSupportedFrameworks(IPackageSearchMetadata metadata)
    {
        return metadata.DependencySets
            .Select(set => set.TargetFramework)
            .Where(framework => framework is not null && framework != NuGetFramework.AnyFramework)
            .Select(framework => framework!.GetShortFolderName())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? GetFrameworkFamily(string frameworkName)
    {
        var value = frameworkName.ToLowerInvariant();

        if (value.StartsWith("netstandard", StringComparison.Ordinal))
        {
            return "netstandard";
        }

        if (!value.StartsWith("net", StringComparison.Ordinal) || value.StartsWith("netstandard", StringComparison.Ordinal))
        {
            return null;
        }

        if (value.StartsWith("netcoreapp", StringComparison.Ordinal))
        {
            return "netcore";
        }

        // net5.0+ belongs to modern .NET family.
        return value.Length > 3 && char.IsDigit(value[3]) && value.Contains('.', StringComparison.Ordinal)
            ? "netcore"
            : null;
    }

    private sealed record SourceCandidate(NuGet.Versioning.NuGetVersion Version, string[] Supports, string[] SupportFamilies, string Feed);

    private sealed record SettingsResolution(string? WorkspaceDirectory, string? NuGetConfigPath, string? Error);

    private sealed record PackageSourceLoadResult(IReadOnlyList<PackageSource> Sources, string? Error);

    private static async Task<LegacyPackageFlags> CheckLegacyPackageFlagsAsync(
        string packageId,
        NuGet.Versioning.NuGetVersion version,
        IReadOnlyList<PackageSource> sources,
        CancellationToken cancellationToken)
    {
        using var cache = new SourceCacheContext();
        foreach (var source in sources)
        {
            try
            {
                var repository = new SourceRepository(source, Providers);
                var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                using var stream = new MemoryStream();
                if (!await resource.CopyNupkgToStreamAsync(packageId, version, stream, cache, NullLogger.Instance, cancellationToken))
                    continue;

                stream.Position = 0;
                using var reader = new PackageArchiveReader(stream);
                var files = (await reader.GetFilesAsync(cancellationToken)).ToList();

                var hasContentFolder = files.Any(f =>
                    f.StartsWith("content/", StringComparison.OrdinalIgnoreCase));

                var hasInstallScript = files.Any(f =>
                    string.Equals(f, "tools/install.ps1", StringComparison.OrdinalIgnoreCase));

                return new LegacyPackageFlags(hasContentFolder, hasInstallScript);
            }
            catch
            {
                continue;
            }
        }

        return new LegacyPackageFlags(false, false);
    }

    private sealed record LegacyPackageFlags(bool HasLegacyContentFolder, bool HasInstallScript);

    private static bool TryParseNuGetVersion(string? version, out NuGet.Versioning.NuGetVersion parsed)
    {
        parsed = new NuGet.Versioning.NuGetVersion(0, 0, 0);
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var isValid = NuGet.Versioning.NuGetVersion.TryParse(version, out var parsedVersion);
        if (!isValid || parsedVersion is null)
        {
            return false;
        }

        parsed = parsedVersion;
        return true;
    }
}

internal sealed record PackageVersionInput(string PackageId, string CurrentVersion);

internal sealed record PackageUpgradeRecommendation(
    string PackageId,
    string? CurrentVersion,
    string? MinimumSupportedVersion,
    IReadOnlyList<string> Supports,
    IReadOnlyList<string> SupportFamilies,
    string? Feed,
    bool HasLegacyContentFolder,
    bool HasInstallScript,
    string? Reason);

internal sealed record PackageUpgradeRecommendationResult(
    IReadOnlyList<PackageUpgradeRecommendation> Recommendations,
    string? Reason);

internal sealed record PackageSupportResult(
    string PackageId,
    bool IncludePrerelease,
    string? MinimumVersion,
    IReadOnlyList<string> Supports,
    IReadOnlyList<string> SupportFamilies,
    string? Feed,
    string? Reason);
