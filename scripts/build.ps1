Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "Jellyfin.Plugin.Recommendations.sln"

dotnet build $solution --configuration Release
