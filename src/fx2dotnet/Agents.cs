using fx2dotnet;
using GitHub.Copilot;
using Microsoft.Extensions.FileProviders;

class Agents
{
    public static IFileProvider AgentProvider { get; } = new EmbeddedFileProvider(typeof(Program).Assembly, "fx2dotnet.Resources.agents");

    public static string GetAgent(string name)
    {
        using var stream = AgentProvider.GetFileInfo(name).CreateReadStream();
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    public static CustomAgentConfig GetCustomAgentConfig(string name, Dictionary<string, McpServerConfig> mcpTools, IEnumerable<ICommonTool> tools)
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
