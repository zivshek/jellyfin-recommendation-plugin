Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "Jellyfin.Plugin.Recommendations\Jellyfin.Plugin.Recommendations.csproj"
$output = Join-Path $repoRoot "artifacts\Recommendations"

dotnet publish $project --configuration Release --output $output
Write-Host "Plugin package written to $output"
