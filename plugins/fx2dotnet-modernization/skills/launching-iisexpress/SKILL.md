---
name: launching-iisexpress
description: "Launch a .NET Framework ASP.NET project using IIS Express from the command line, replicating how Visual Studio would run it. Use when the user wants to start, run, or test the web app locally during migration. Do NOT use for .NET Core or modern .NET apps — use dotnet run instead."
---

# Launch ASP.NET Web App with IIS Express

Start an ASP.NET project (Web API, MVC, Web Forms, etc.) using IIS Express from the terminal. This skill provides a template PowerShell script and a workflow for discovering project settings, creating a launch script, and running it.

## Prerequisites

- .NET Framework targeting pack installed (version matching the project's `TargetFrameworkVersion`)
- IIS Express installed (ships with Visual Studio, typically at `C:\Program Files\IIS Express\iisexpress.exe`)
- NuGet packages restored: `msbuild <solution-path> /t:Restore /p:RestorePackagesConfig=true`
- Solution built: `msbuild <solution-path> /p:Configuration=Debug`

## Workflow

### 1. Check for an existing launch script

Look for a `Start-IISExpress.ps1` (or similar) in the solution root or a `scripts/` folder. If one already exists, skip to **Step 4 — Run**.

### 2. Discover project settings

Read the target project's `.csproj` to find:
- **Site name**: The project name (e.g. `MyApp.WebApi`)
- **Port**: Look in `ProjectExtensions > VisualStudio > FlavorProperties > WebProjectProperties > IISUrl` for the localhost URL and port
- **Physical path**: The absolute path to the project directory containing `Web.config`
- **Solution root**: The absolute path to the folder containing the `.sln` file

### 3. Create the launch script from template

Read the template at `references/Start-IISExpress.template.ps1` (relative to this skill). Copy it to the solution root as `Start-IISExpress.ps1` and replace the placeholder variables:

| Placeholder | Replace with |
|---|---|
| `{{SITE_NAME}}` | The project name from step 2 |
| `{{SITE_PORT}}` | The port number from step 2 |
| `{{WEB_PROJECT_PATH}}` | The absolute path to the project folder |
| `{{SOLUTION_ROOT}}` | The absolute path to the solution root |

### 4. Run

Build and launch:

```powershell
msbuild <solution-path> /p:Configuration=Debug
.\Start-IISExpress.ps1
```

The script launches IIS Express as a detached process and returns immediately after confirming the site is listening.

### 5. Verify

The script performs a basic readiness check automatically. To further verify, use `curl` in the same terminal:

```powershell
curl http://localhost:<port>/
```

Use the default route or landing page configured in the project (e.g. the IISUrl from the `.csproj`).

### 6. Stop IIS Express

```powershell
.\Start-IISExpress.ps1 -Stop
```

Or directly:

```powershell
Get-Process iisexpress -ErrorAction SilentlyContinue | Stop-Process -Force
```

## Template

The template script is at [`references/Start-IISExpress.template.ps1`](references/Start-IISExpress.template.ps1). It handles:

- Stopping any existing IIS Express process
- Copying the IIS Express `applicationhost.config` template from `C:\Program Files\IIS Express\config\templates\PersonalWebServer\applicationhost.config`
- Patching the default `WebSite1` site entry with the target project's name, physical path, port, and `Clr4IntegratedAppPool`
- Writing the config to `.vs\config\applicationhost.config` relative to the solution root
- Launching IIS Express as a detached process (returns immediately)
- Polling until the site is accepting connections

## Troubleshooting

- **Port conflict**: If the port is in use, check `.vs/config/applicationhost.config` for the binding or pick a different port
- **Missing packages**: Run `msbuild <solution-path> /t:Restore /p:RestorePackagesConfig=true` first
- **Missing IIS Express**: Install Visual Studio or the standalone IIS Express download
