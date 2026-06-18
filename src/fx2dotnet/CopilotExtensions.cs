using GitHub.Copilot;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

public static class CopilotExtensions
{
    public static void AddCopilot(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(sp => new CopilotClient(new()
        {
            Logger = sp.GetRequiredService<ILogger<CopilotClient>>(),
            LogLevel = CopilotLogLevel.Info,
        }));

        builder.Services.AddOptions<Fx2DotnetOptions>()
            .BindConfiguration("")
            .Configure<ILogger<Fx2DotnetOptions>>((options, logger) =>
            {
                if (string.IsNullOrEmpty(options.GitHubToken))
                {
                    if (GetGitHubToken(logger, CancellationToken.None).GetAwaiter().GetResult() is { } token)
                    {
                        options.GitHubToken = token;
                    }
                }
            });

        builder.Services.AddHostedService<CopilotClientStartup>();
        builder.Services.AddSingleton<CopilotRunner>();
        builder.Services.AddSingleton<IFx2Dotnet, CopilotFx2Dotnet>();
    }

    private sealed class CopilotClientStartup(CopilotClient client) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => client.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => client.StopAsync();
    }

    private static async Task<string?> GetGitHubToken(ILogger logger, CancellationToken token)
    {
        try
        {
            logger.LogInformation("Attempting to retrieve GitHub token from gh CLI...");

            using var process = Process.Start(new ProcessStartInfo()
            {
                Arguments = "auth token",
                RedirectStandardOutput = true,
                FileName = "gh",
            });

            if (process is { } && process.Start())
            {
                await process.WaitForExitAsync(token);
                var result = await process.StandardOutput.ReadToEndAsync(token);

                if (!string.IsNullOrEmpty(result))
                {
                    logger.LogInformation("Retrieved GitHub token from gh CLI.");
                    return result;
                }
            }

            logger.LogWarning("Could not retrieve GitHub token from gh CLI.");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to retrieve GitHub token from gh CLI.");
        }

        return null;
    }
}

public class Fx2DotnetOptions
{
    [Required]
    public string GitHubToken { get; set; } = null!;
}

public interface IFx2Dotnet
{
    Task<string> AnalyzeAsync(string solutionPath, CancellationToken cancellationToken);
}
