
using fx2dotnet;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel;

internal class CopilotRunner(CopilotClient client, IOptions<Fx2DotnetOptions> options, ILogger<CopilotRunner> logger)
{
    public async Task<string> RunAsync(string systemPrompt, string prompt, string agent, McpBuilder mcpTools, IList<CustomAgentConfig> customAgents, CancellationToken cancellationToken)
    {

        var token = options.Value.GitHubToken;

        //var githubMcp = new McpHttpServerConfig()
        //{
        //    Url = "https://api.githubcopilot.com/mcp/",
        //    Headers = new Dictionary<string, string>()
        //    {
        //        ["Authorization"] = $"Bearer {token}"
        //    },
        //    Tools = [
        //        "get_file_contents",
        //        "list_commits",
        //        "search_commits",
        //        "get_commit",
        //        "issue_read",
        //        "search_issues",
        //        "get_label",
        //        "list_pull_requests",
        //        "search_pull_requests",
        //        "pull_request_read",
        //        "request_copilot_review",
        //        "search_code",
        //        "search_repositories"
        //        ]
        //};

        await using var session = await client.CreateSessionAsync(new()
        {
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Customize,
                Sections = new Dictionary<SystemMessageSection, SectionOverride>()
                {
                    [SystemMessageSection.Identity] = new()
                    {
                        Action = SectionOverrideAction.Replace,
                        Content = systemPrompt
                    }
                }
            },
            SkillDirectories = [Path.Combine(AppContext.BaseDirectory, "Resources", "skills")],
            Hooks = new()
            {
                OnPreMcpToolCall = (input, invocation) =>
                {
                    logger.LogInformation("Invoking tool: {ServerName}/{ToolName}", input.ServerName, input.ToolName);
                    return Task.FromResult<PreMcpToolCallHookOutput?>(null);
                },
                OnErrorOccurred = (input, invocation) =>
                {
                    logger.LogError("Unexpected Error: {ErrorMessage}", input.Error);
                    return Task.FromResult<ErrorOccurredHookOutput?>(null);
                },
                OnPostToolUseFailure = (input, invocation) =>
                {
                    logger.LogError("Tool use failed: {ToolName} - {ErrorMessage}", input.ToolName, input.Error);
                    return Task.FromResult<PostToolUseFailureHookOutput?>(null);
                }
            },
            AvailableTools = new ToolSet().AddBuiltIn(BuiltInTools.Isolated).AddCustom("*").AddMcp("*"),
            OnPermissionRequest = PermissionHandler.ApproveAll,
            McpServers = mcpTools,
            GitHubToken = token,
            Tools = [
                CopilotTool.DefineTool(LogAnalysisProgress),
                CopilotTool.DefineTool(NuGetTools.FindRecommendedPackageUpgrades),
                ],
            Agent = agent,
            CustomAgents = customAgents,

        }, cancellationToken);

        session.On<SessionEvent>(e =>
        {
            if (e is SessionMcpServersLoadedEvent m)
            {
                foreach (var server in m.Data.Servers)
                {
                    if (!string.IsNullOrEmpty(server.Error))
                    {
                        logger.LogError("MCP Server Loaded: {ServerName}: {Status} [{Error}]", server.Name, server.Status, server.Error);
                    }
                    else
                    {
                        logger.LogInformation("MCP Server Loaded: {ServerName}: {Status}", server.Name, server.Status);
                    }
                }
            }
            else if (e is SessionMcpServerStatusChangedEvent m2)
            {
                if (!string.IsNullOrEmpty(m2.Data.Error))
                {
                    logger.LogInformation("MCP Server Status Changed: {ServerName}: {Status} [{Error}]", m2.Data.ServerName, m2.Data.Status, m2.Data.Error);
                }
                else
                {
                    logger.LogInformation("MCP Server Status Changed: {ServerName}: {Status}", m2.Data.ServerName, m2.Data.Status);
                }
            }
            else
            {
                logger.LogTrace("Session Event: {EventName}", e.Type);
            }
        });

        var result = await session.SendAndWaitAsync(prompt, cancellationToken: cancellationToken);

        return result?.Data.Content ?? "No Content";
    }

    [Description("Log progress milestones and significant findings during session")]
    private string LogAnalysisProgress(
        [Description("Brief description of the milestone or finding (concise, under 15 words)")]
            string message)
    {
        logger.LogInformation("{Message}", message);
        return "Message logged";
    }
}
