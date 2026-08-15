param(
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "Jellyfin.Plugin.Recommendations\Jellyfin.Plugin.Recommendations.csproj"
$output = Join-Path $repoRoot "artifacts\Recommendations"

$projectXml = [xml](Get-Content -LiteralPath $project -Raw)
if (-not $Version) {
    $Version = $projectXml.Project.PropertyGroup.Version
}

$versionParts = $Version.Split(".", [System.StringSplitOptions]::RemoveEmptyEntries)
while ($versionParts.Count -lt 4) {
    $versionParts += "0"
}

if ($versionParts[0..3] | Where-Object { $_ -notmatch "^\d+$" }) {
    throw "Version '$Version' must resolve to four numeric version parts."
}

$packageVersion = [string]::Join(".", $versionParts[0..3])
$publishProperties = @(
    "/p:Version=$packageVersion",
    "/p:PackageVersion=$packageVersion",
    "/p:AssemblyVersion=$packageVersion",
    "/p:FileVersion=$packageVersion",
    "/p:InformationalVersion=$packageVersion"
)

dotnet publish $project --configuration Release --output $output @publishProperties

$packagedMetaPath = Join-Path $output "meta.json"
$meta = Get-Content -LiteralPath $packagedMetaPath -Raw | ConvertFrom-Json
$meta.version = $packageVersion
$meta.timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", [System.Globalization.CultureInfo]::InvariantCulture)
$meta | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $packagedMetaPath -Encoding UTF8

Write-Host "Plugin package written to $output"
