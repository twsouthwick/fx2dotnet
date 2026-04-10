<#
.SYNOPSIS
    Sets up and launches IIS Express for an ASP.NET Web API or MVC project.
.DESCRIPTION
    Copies the IIS Express template applicationhost.config, patches the site
    configuration to point at the target project on the discovered port, stops
    any IIS Express instance running with the same config, and launches the new one.
.PARAMETER Stop
    If specified, stops the IIS Express process for this site and exits.
.NOTES
    This script is generated from a template. Customize the variables below
    for the target project before running.
#>
param(
    [switch]$Stop
)

$ErrorActionPreference = 'Stop'

# ============================================================
# CUSTOMIZE THESE VARIABLES FOR THE TARGET PROJECT
# ============================================================
$siteName       = "{{SITE_NAME}}"           # e.g. "MyApp.WebApi"
$sitePort       = {{SITE_PORT}}             # e.g. 6001
$webProjectPath = "{{WEB_PROJECT_PATH}}"    # absolute path to the folder containing Web.config
$solutionRoot   = "{{SOLUTION_ROOT}}"       # absolute path to the solution root
# ============================================================

# Resolve paths (needed before stop check)
$configDir      = Join-Path $solutionRoot ".vs\config"
$configPath     = Join-Path $configDir "applicationhost.config"

# Stop only the IIS Express instance running with this config
$existing = Get-CimInstance Win32_Process -Filter "Name = 'iisexpress.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -like "*/config:*$configPath*" }
if ($existing) {
    Write-Host "Stopping IIS Express for site '$siteName'..."
    $existing | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    Start-Sleep -Seconds 1
}

if ($Stop) {
    Write-Host "IIS Express stopped."
    return
}

$iisExpressExe  = "C:\Program Files\IIS Express\iisexpress.exe"
$templatePath   = "C:\Program Files\IIS Express\config\templates\PersonalWebServer\applicationhost.config"

# Validate
if (-not (Test-Path $webProjectPath)) {
    throw "Web project not found at: $webProjectPath"
}
if (-not (Test-Path $iisExpressExe)) {
    throw "IIS Express not found at: $iisExpressExe"
}
if (-not (Test-Path $templatePath)) {
    throw "IIS Express template not found at: $templatePath"
}

# Copy template and patch site config via XML
Write-Host "Preparing applicationhost.config..."
New-Item -ItemType Directory -Path $configDir -Force | Out-Null
Copy-Item $templatePath $configPath -Force

$xml = [xml](Get-Content $configPath -Raw)
$site = $xml.SelectSingleNode("//site[@name='WebSite1']")
if (-not $site) {
    throw "Could not find the default 'WebSite1' site in the IIS Express template config."
}

$site.SetAttribute("name", $siteName)
$app = $site.SelectSingleNode("application")
$app.SetAttribute("applicationPool", "Clr4IntegratedAppPool")
$app.SelectSingleNode("virtualDirectory").SetAttribute("physicalPath", $webProjectPath)
$site.SelectSingleNode("bindings/binding").SetAttribute("bindingInformation", "*:${sitePort}:localhost")

$xml.Save($configPath)

Write-Host "Config written to: $configPath"
Write-Host "Physical path:     $webProjectPath"
Write-Host "URL:               http://localhost:$sitePort/"
Write-Host ""
Write-Host "Launching IIS Express..."

# Launch detached so the script returns immediately
$proc = Start-Process -FilePath $iisExpressExe `
    -ArgumentList "/config:`"$configPath`" /site:`"$siteName`"" `
    -PassThru

Write-Host "IIS Express started (PID $($proc.Id)). Waiting for it to accept connections..."

# Give IIS Express a moment to start listening
$ready = $false
for ($i = 0; $i -lt 10; $i++) {
    Start-Sleep -Milliseconds 500
    try {
        $null = Invoke-WebRequest -Uri "http://localhost:$sitePort/" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
        $ready = $true
        break
    } catch {
        # not ready yet
    }
}

if ($ready) {
    Write-Host "IIS Express is listening on http://localhost:$sitePort/"
} else {
    Write-Host "WARNING: IIS Express may not be ready yet — check http://localhost:$sitePort/ manually."
}
