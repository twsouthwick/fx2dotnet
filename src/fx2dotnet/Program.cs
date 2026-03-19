using fx2dotnet;
using GitHub.Copilot.SDK;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Debug);
    builder.AddConsole();
});

await using var client = new CopilotClient(new()
{
    //LogLevel = "debug",
    //Logger = loggerFactory.CreateLogger<CopilotClient>(),
});

var sessionId = Guid.NewGuid().ToString("N");

await client.StartAsync();

var agent = client.AsAIAgent(new SessionConfig
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
    McpServers = new()
    {
        ["appmod"] = new McpLocalServerConfig
        {
            Type = "stdio",
            Command = "dnx",
            Args = [
              "Microsoft.GitHubCopilot.Modernization.Mcp",
              "--prerelease",
              "--yes",
              "--add-source",
              "https://api.nuget.org/v3/index.json",
              "--ignore-failed-sources",
            ],
            Env = new()
            {
                ["APPMOD_CALLER_TYPE"] = "copilot-cli"
            },
            Tools = [
                "get_projects_in_topological_order",
                "get_project_dependencies",
                "generate_dotnet_upgrade_assessment",
                "query_dotnet_assessment",
                "convert_project_to_sdk_style",
                "authenticate_nuget_feed",
            ],
        },
        // Remote HTTP server
        ["microsoft-learn"] = new McpRemoteServerConfig
        {
            Type = "http",
            Url = "https://learn.microsoft.com/api/mcp",
            Tools = ["*"],
        }
    },
    SessionId = sessionId,
    Model = "claude-sonnet-4.6",
    //ReasoningEffort = "xhigh",
    SystemMessage = new()
    {
        Content = """
                  You are an agent that helps modernize applications. Always focus on the least disruptive change and keep any changes scoped to the required asks.
    
                  Ask for clarification anytime for more details.
                  """,
        Mode = SystemMessageMode.Append,
    },
    Tools = [
        AIFunctionFactory.Create(Tools.Agent),
     ]
});

var session = await agent.CreateSessionAsync();

var plan = agent.CreateAgent(Agents.AgentProvider.GetFileInfo("plan.agent.md"));
var packageCompat = agent.CreateAgent(Agents.AgentProvider.GetFileInfo("package-compat-core.agent.md"));
var buildFix = agent.CreateAgent(Agents.AgentProvider.GetFileInfo("build-fix.agent.md"));

var startExecutor = new ChatForwardingExecutor("Start");

var workflow = new WorkflowBuilder(startExecutor)
    .AddEdge(startExecutor, plan)
    .AddEdge(plan, packageCompat)
    .Build();

var run = await InProcessExecution.Default.RunStreamingAsync(workflow, args[0]);

await foreach (var @event in run.WatchStreamAsync())
{
    Console.WriteLine(@event.ToString());
}

class Agents
{
    public static IFileProvider AgentProvider { get; } = new EmbeddedFileProvider(typeof(Program).Assembly, "fx2dotnet.Resources.agents");
}

class Tools
{
    [Description("Runs a task as a subagent")]
    public static async Task<string> Agent(
        AIAgent agent,
        [Description("Name of the agent")] string agentName,
        [Description("Instructions for the subagent")] string instructions)
    {
        return "Not implemented yet!";
    }

    [Description("Ask questions for clarification")]
    public static async Task<string> AskQuestions(
        [Description("List of questions")] List<string> questions)
    {
        return questions[0];
    }
}