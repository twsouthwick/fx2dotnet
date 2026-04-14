using System.Text;
using YamlDotNet.Serialization;

namespace fx2dotnet;

internal static class AgentPromptParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public static ParsedAgentPrompt Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ParsedAgentPrompt(new AgentFrontMatter(), string.Empty);
        }

        using var reader = new StringReader(content);
        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
        {
            return new ParsedAgentPrompt(new AgentFrontMatter(), content);
        }

        var yaml = new StringBuilder();
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                var frontMatterText = yaml.ToString();
                var frontMatter = string.IsNullOrWhiteSpace(frontMatterText)
                    ? new AgentFrontMatter()
                    : Deserializer.Deserialize<AgentFrontMatter>(frontMatterText) ?? new AgentFrontMatter();

                return new ParsedAgentPrompt(frontMatter, reader.ReadToEnd().TrimStart('\r', '\n'));
            }

            yaml.AppendLine(line);
        }

        return new ParsedAgentPrompt(new AgentFrontMatter(), content);
    }
}

internal sealed record ParsedAgentPrompt(AgentFrontMatter FrontMatter, string Body);

internal sealed class AgentFrontMatter
{
    public string? DisplayName { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }

    [YamlMember(Alias = "argument-hint")]
    public string? ArgumentHint { get; init; }

    public List<string> Tools { get; init; } = [];
    public List<string> Agents { get; init; } = [];
    public List<string> McpTools { get; init; } = [];
}
