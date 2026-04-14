using fx2dotnet;
using GitHub.Copilot.SDK;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

if (!(args is [{ } slnPath] && File.Exists(slnPath)))
{
    Console.WriteLine("Usage: fx2dotnet [path to solution]");
    return;
}

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

var agent = client.AsAIAgent(new SessionConfig
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
    SessionId = sessionId,
    Model = "claude-opus-4.6",
    //Model = "claude-sonnet-4.6",
    //ReasoningEffort = "xhigh",
    SystemMessage = new()
    {
        Content = Agents.GetAgent("dotnet-fx-to-modern-dotnet.md"),
        Mode = SystemMessageMode.Append,
    },
    Tools = [
        .. Tools.Common.Select(c=>c.Function),
        AIFunctionFactory.Create(NuGetTools.FindRecommendedPackageUpgrades),
     ],
    McpServers = mcpTools,
    CustomAgents = [
        Agents.GetCustomAgentConfig("assessment.agent.md", mcpTools, Tools.Common),
        Agents.GetCustomAgentConfig("package-compat-core.agent.md", mcpTools, Tools.Common),
        Agents.GetCustomAgentConfig("plan.agent.md", mcpTools, Tools.Common),
        Agents.GetCustomAgentConfig("webapp-project-detector.agent.md", mcpTools, Tools.Common),
        ]

});

var session = await agent.CreateSessionAsync();

var messages = $"""
    Create a plan for {slnPath}
    """;


await foreach (var result in agent.RunStreamingAsync(messages, session))
{
    if (!string.IsNullOrEmpty(result.Text))
    {
        Console.Write(result.Text);
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
    public static IEnumerable<ICommonTool> Common =>
    [
        new StatusImpl(),
        new QuestionImpl(),
        new ReadImpl(),
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