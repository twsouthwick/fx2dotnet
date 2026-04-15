using fx2dotnet;
using GitHub.Copilot.SDK;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text;

var refreshPlan = args.Any(static a => string.Equals(a, "--refresh", StringComparison.OrdinalIgnoreCase)
    || string.Equals(a, "--refresh-plan", StringComparison.OrdinalIgnoreCase));
var slnPath = args.FirstOrDefault(static a => !a.StartsWith("--", StringComparison.Ordinal));

if (string.IsNullOrWhiteSpace(slnPath) || !File.Exists(slnPath))
{
    Console.WriteLine("Usage: fx2dotnet [path to solution] [--refresh]");
    return;
}

var solutionFullPath = Path.GetFullPath(slnPath);
var solutionDirectory = Path.GetDirectoryName(solutionFullPath)!;
var planPath = Path.Combine(solutionDirectory, "modernization-plan.md");

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

var mcpTools = new Dictionary<string, object>()
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
};

var commonTools = Tools.GetCommon(planPath).ToArray();

var backlogAgent = Agents.GetCustomAgentConfig("github-modernization-backlog.agent.md", [], commonTools);
var ghAgent = client.AsAIAgent(new SessionConfig()
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
    Agent = backlogAgent.Name,
    CustomAgents = [backlogAgent],
    Tools = [.. commonTools.Select(t => t.Function)],
    McpServers = new()
    {
        ["github"] = new McpRemoteServerConfig()
        {
            Type = "http",
            Url = "https://api.githubcopilot.com/mcp/",
            Headers = new()
            {
                ["Authorization"] = "Bearer ${TOKEN}"
            },
            Tools = ["*"],
        }
    }
}, name: "GitHub Issue Manager");

var fxdotnet = Agents.GetCustomAgentConfig("dotnet-fx-to-modern-dotnet.md", mcpTools, commonTools);
var planAgent = client.AsAIAgent(new SessionConfig
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
    SessionId = sessionId,
    Model = "claude-opus-4.6",
    SkillDirectories = [Path.Combine(AppContext.BaseDirectory, "Resources", "skills")],
    //Model = "claude-sonnet-4.6",
    //ReasoningEffort = "xhigh",
    Agent = fxdotnet.Name,
    Tools = [
        .. commonTools.Select(c => c.Function),
        AIFunctionFactory.Create(NuGetTools.FindRecommendedPackageUpgrades),
     ],
    McpServers = mcpTools,
    CustomAgents = [
        fxdotnet,
        Agents.GetCustomAgentConfig("assessment.agent.md", mcpTools, commonTools),
        Agents.GetCustomAgentConfig("package-compat-core.agent.md", mcpTools, commonTools),
        Agents.GetCustomAgentConfig("plan.agent.md", mcpTools, commonTools),
        Agents.GetCustomAgentConfig("webapp-project-detector.agent.md", mcpTools, commonTools),
        Agents.GetCustomAgentConfig("github-modernization-backlog.agent.md", mcpTools, commonTools),
    ]
}, name: ".NET Upgrade Planner");

var session = await planAgent.CreateSessionAsync();

var agent1 = (planAgent, $"""
    If a saved modernization plan already exists for {solutionFullPath}, use that plan as-is and do not refresh or regenerate it unless the caller explicitly requested --refresh.
    Otherwise, create the complete modernization plan for {solutionFullPath}.
    Save the working plan with WritePlan whenever you have a usable draft, and make sure the final plan is persisted.
    Return the full plan content in your final response.
    Do not ask whether to proceed, execute, or adjust priorities.
    """);
var agent2 = (ghAgent, $"Verify issues exist for the upgrade using the saved plan for {solutionFullPath}. If any do not exist, create them and if they do exist make sure they're up-to-date. BE VERY VERBOSE with what you're doing.");

foreach (var agent in new[] { agent1, agent2 })
{
    Console.WriteLine();
    Console.WriteLine($"--------------------- {agent.Item1.Name} ----------------");
    Console.WriteLine();

    await foreach (var result in agent.Item1.RunStreamingAsync(agent.Item2, session))
    {
        if (!string.IsNullOrEmpty(result.Text))
        {
            Console.Write(result.Text);
        }
    }
}

public interface ICommonTool
{
    AIFunction Function { get; }

    string Prompt { get; }
}

class Agents
{
    public static IFileProvider AgentProvider { get; } = new EmbeddedFileProvider(typeof(Program).Assembly, "fx2dotnet.Resources.agents");

    public static string GetAgent(string name)
    {
        using var stream = AgentProvider.GetFileInfo(name).CreateReadStream();
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    public static CustomAgentConfig GetCustomAgentConfig(string name, Dictionary<string, object> mcpTools, IEnumerable<ICommonTool> tools)
    {
        var text = GetAgent(name);
        var parsed = AgentPromptParser.Parse(text);

        var prompt = string.Join(Environment.NewLine, [.. tools.Select(t => t.Prompt), parsed.Body]);

        return new()
        {
            Description = parsed.FrontMatter.Description,
            DisplayName = parsed.FrontMatter.DisplayName,
            Name = parsed.FrontMatter.Name ?? name,
            Prompt = prompt,
            Tools = [.. parsed.FrontMatter.Tools, .. tools.Select(t => t.Function.Name)],
            McpServers = parsed.FrontMatter.McpTools.ToDictionary(m => m, m => mcpTools[m]),
        };
    }
}

class Tools
{
    public static IEnumerable<ICommonTool> GetCommon(string planPath) =>
    [
        new StatusImpl(),
        new QuestionImpl(),
        new ReadImpl(),
        new PlanExistsImpl(planPath),
        new WritePlanImpl(planPath),
        new GetPlanImpl(planPath),
    ];

    private class ReadImpl : ICommonTool
    {
        public AIFunction Function => AIFunctionFactory.Create(ReadFile);

        public string Prompt => string.Empty;

        public string ReadFile(
            [Description("Full path to file that needs to be read")] string path)
        {
            try
            {
                var contents = File.ReadAllText(path);
                Console.WriteLine($"Reading file: {path}");
                return $"""
                    File contents:

                    {contents}
                    """;
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"Could not file file: {path}");
                return "Could not find file";
            }
        }
    }

    private sealed class WritePlanImpl(string planPath) : ICommonTool
    {
        public string Prompt => $"""
            Use the WritePlan tool whenever you have a usable modernization plan draft so the plan is persisted to {planPath}.
            Use the GetPlan tool to retrieve the current saved plan before revising it.
            """;

        public AIFunction Function => AIFunctionFactory.Create(WritePlan);

        [Description("Writes the current modernization plan markdown to the shared plan file")]
        public string WritePlan([Description("The full markdown content of the modernization plan")] string markdown)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
            File.WriteAllText(planPath, markdown);
            Console.WriteLine($"Plan written to: {planPath}");
            return $"Plan saved to {planPath}";
        }
    }

    private sealed class PlanExistsImpl(string planPath) : ICommonTool
    {
        public string Prompt => """
            If you need to know if the plan is available use CheckIfPlanExists.
            """;

        public AIFunction Function => AIFunctionFactory.Create(CheckIfPlanExists);

        [Description("Checks if the plan exists")]
        public string CheckIfPlanExists()
        {
            return File.Exists(planPath) ? "Plan exists" : "Plan does not exist";
        }
    }


    private sealed class GetPlanImpl(string planPath) : ICommonTool
    {
        public string Prompt => """
            If GetPlan returns an existing plan and the user did not ask to refresh or revise it, do not echo, summarize, or restate the plan contents. Report only that a saved plan already exists.
            """;

        public AIFunction Function => AIFunctionFactory.Create(GetPlan);

        [Description("Gets the current contents of the saved modernization plan file")]
        public string GetPlan()
        {
            if (!File.Exists(planPath))
            {
                Console.WriteLine($"No plan file exists yet at: {planPath}");
                return "No plan has been written yet.";
            }

            Console.WriteLine($"Reading plan from: {planPath}");
            return File.ReadAllText(planPath);
        }
    }

    private class StatusImpl : ICommonTool
    {
        public string Prompt => """
            Make sure to surface status message through the status tool regularly to let the user know what you're doing and why.
            """;

        public AIFunction Function { get; } = AIFunctionFactory.Create(Status);

        [Description("Surfaces status messages")]
        public static void Status([Description("The status message to display")] string message)
        {
            Console.WriteLine(message);
        }
    }

    private class QuestionImpl : ICommonTool
    {
        public string Prompt => """
            If there is uncertainty about something make sure to call the AskQuestions tools.
            """;

        public AIFunction Function { get; } = AIFunctionFactory.Create(AskQuestions);

        [Description("Allows to ask a question")]
        public static string AskQuestions(
            [Description("The question that needs to be figured out")] string question,
            [Description("Possible answers. Ensure it's sorted by most likely and the first will be default")] string[] answers)
        {
            Console.WriteLine($"[QUESTION]: {question}");

            foreach (var a in answers)
            {
                Console.WriteLine($" - {a}");
            }

            return answers[0];
        }
    }
}