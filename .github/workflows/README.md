# GitHub CI

This folder includes one active workflow.

## Active workflow

- `ci.yml`: Builds and packs the MCP server project into a NuGet package.
  - Uses the SDK pinned in `global.json`.
  - Checks out full git history (`fetch-depth: 0`) so GitVersion can compute versions correctly.
  - Restores and builds in `Release`.
  - Runs `dotnet pack` on `src/fx2dotnet/fx2dotnet.csproj` with package ID `Swick.Mcp.Fx2dotnet`.
  - Uploads `.nupkg` as a workflow artifact named `fx2dotnet-nuget`.
  - Pushes the package to `https://nuget.pkg.github.com/${{ github.repository_owner }}/index.json` for non-PR runs using `secrets.GITHUB_TOKEN`.
