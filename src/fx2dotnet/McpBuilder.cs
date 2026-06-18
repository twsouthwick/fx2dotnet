using GitHub.Copilot;
using GitHub.Copilot.SDK;
using System;
using System.Collections.Generic;
using System.Text;

namespace fx2dotnet;

internal class McpBuilder : Dictionary<string, McpServerConfig>
{
    // Remote HTTP server
    private static McpHttpServerConfig mslearn = new()
    {
        Url = "https://learn.microsoft.com/api/mcp",
        Tools = ["*"],
    };

    private McpHttpServerConfig github(string githubToken) => new()
    {
        Url = "https://api.githubcopilot.com/mcp/",
        Headers = new Dictionary<string, string>()
        {
            ["Authorization"] = "Bearer ${TOKEN}"
        },
        Tools = ["*"],
    };

    private static McpServerConfig appmod = new McpStdioServerConfig
    {
        Command = "dnx",
        Args = [
          "Microsoft.GitHubCopilot.Modernization.Mcp",
              "--prerelease",
              "--yes",
              "--add-source",
              "https://api.nuget.org/v3/index.json",
              "--ignore-failed-sources",
            ],
        Env = new Dictionary<string, string>()
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
    };

    public static McpBuilder Create() => new McpBuilder();

    public McpBuilder WithMsLearn()
    {
        Add("mslearn", mslearn);
        return this;
    }

    public McpBuilder WithGitHub(string githubToken)
    {
        Add("github", github(githubToken));
        return this;
    }

    public McpBuilder WithAppMod()
    {
        Add("appmod", appmod);
        return this;
    }
}
