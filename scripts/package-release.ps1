param(
    [string]$Version = "",
    [string]$Tag = "",
    [string]$Repository = "zivshek/jellyfin-recommendation-plugin",
    [string]$OutputRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot "artifacts"
}

$project = Join-Path $repoRoot "Jellyfin.Plugin.Recommendations\Jellyfin.Plugin.Recommendations.csproj"
$metaPath = Join-Path $repoRoot "Jellyfin.Plugin.Recommendations\meta.json"
$packageDir = Join-Path $OutputRoot "Recommendations"
$repositoryDir = Join-Path $OutputRoot "repository"

if (-not $Tag) {
    $Tag = if ($env:GITHUB_REF_NAME) { $env:GITHUB_REF_NAME } else { "v0.1.0" }
}

if (-not $Version) {
    $Version = $Tag.TrimStart("v", "V")
}

$versionParts = $Version.Split(".", [System.StringSplitOptions]::RemoveEmptyEntries)
while ($versionParts.Count -lt 4) {
    $versionParts += "0"
}

$manifestVersion = [string]::Join(".", $versionParts[0..3])
$zipName = "Jellyfin.Plugin.Recommendations_$manifestVersion.zip"
$zipPath = Join-Path $repositoryDir $zipName
$manifestPath = Join-Path $repositoryDir "manifest.json"

New-Item -ItemType Directory -Force -Path $packageDir, $repositoryDir | Out-Null

dotnet publish $project --configuration Release --output $packageDir

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath
}

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$meta = Get-Content -LiteralPath $metaPath -Raw | ConvertFrom-Json
$checksum = (Get-FileHash -LiteralPath $zipPath -Algorithm MD5).Hash.ToLowerInvariant()
$sourceUrl = "https://github.com/$Repository/releases/download/$Tag/$zipName"
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", [System.Globalization.CultureInfo]::InvariantCulture)

$manifest = @(
    [ordered]@{
        category = $meta.category
        guid = $meta.guid
        name = $meta.name
        description = $meta.description
        owner = $meta.owner
        overview = $meta.overview
        versions = @(
            [ordered]@{
                checksum = $checksum
                changelog = $meta.changelog
                targetAbi = $meta.targetAbi
                sourceUrl = $sourceUrl
                timestamp = $timestamp
                version = $manifestVersion
            }
        )
    }
)

ConvertTo-Json -InputObject $manifest -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

[pscustomobject]@{
    PackageDirectory = $packageDir
    ZipPath = $zipPath
    ManifestPath = $manifestPath
    SourceUrl = $sourceUrl
    Checksum = $checksum
    Version = $manifestVersion
}
