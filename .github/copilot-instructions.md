# Project Guidelines

## What This Is

GitHub Copilot agent plugin that orchestrates .NET Framework → modern .NET migrations via a multi-phase, multi-agent workflow. The plugin itself is not a .NET application to build — it's a set of agent definitions, skills, and MCP server integrations that operate on *external* user solutions.

## Architecture

- **Orchestrator**: `agents/dotnet-fx-to-modern-dotnet.agent.md` drives the 7-phase migration flow
- **Phase agents** (in `agents/`): Assessment → Planning → SDK Conversion → Package Compat → Multitarget → Web Migration, with Build Fix called throughout
- **Skills** (in `skills/`): Domain policies (EF6 retention, System.Web adapters, Windows Service migration) — these override default behavior in specific migration domains
- **MCP servers** (configured in `.mcp.json`): `Microsoft.GitHubCopilot.AppModernization.Mcp` for project analysis/SDK conversion, `Swick.Mcp.Fx2dotnet` for NuGet package compatibility data
- **Source code** (`src/fx2dotnet/`): The NuGet versions MCP server — the only buildable project in this repo

See [README.md](../README.md) for the full phase diagram and traversal order.

## Build and Test

```sh
dotnet build fx2dotnet.slnx       # builds the fx2dotnet MCP server
```

Pinned SDK: .NET 10 preview (see `global.json`). Output goes to `artifacts/` via `Directory.Build.props`.

## Conventions

### Agent files (`agents/*.agent.md`)

- YAML frontmatter: `name`, `description`, `argument-hint`, `tools`, `agents`, `handoffs`
- `description` is the discovery surface — agents are matched by description keywords
- Agents that modify code offer a "Commit Changes" handoff for user review
- All terminal commands (`dotnet build`, `dotnet restore`, etc.) must run via **subagent**, never directly in the terminal
- State persisted in `.fx2dotnet/{ProjectName}.md` markdown files relative to the solution being migrated

### Skills (`skills/*/SKILL.md`)

- Encode migration policies that constrain agent behavior (e.g., "retain EF6, don't swap to EF Core")
- Referenced by agents via skill description matching — trigger keywords must appear in the `description` field

### Commits

- Use conventional commits
- Keep commit messages simple and concise — include only the required details
- Each migration step produces a small, reviewable diff — commit granularity matters

### Code changes

- Smallest possible change per step — no drive-by refactors
- Never add NuGet dependencies without user confirmation
- Batch identical fixes (e.g., same `using` across files) as one logical change
