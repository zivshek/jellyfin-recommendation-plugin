Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Load-LocalEnv.ps1")

if (-not $env:JELLYFIN_BASE_URL) {
    throw "JELLYFIN_BASE_URL is not set. Copy .env.example to .env.local and fill in local values."
}

$headers = @{}
if ($env:JELLYFIN_API_KEY) {
    $headers["X-Emby-Token"] = $env:JELLYFIN_API_KEY
}

$baseUrl = $env:JELLYFIN_BASE_URL.Trim()
while ($baseUrl -match '^(https?://)(https?://)(.+)$') {
    $baseUrl = "$($Matches[1])$($Matches[3])"
}

$baseUrl = $baseUrl.TrimEnd("/")
try {
    $systemInfo = Invoke-RestMethod -Method Get -Uri "$baseUrl/System/Info/Public" -Headers $headers
}
catch {
    [Console]::Error.WriteLine("Could not reach Jellyfin at the configured JELLYFIN_BASE_URL. Check that Jellyfin is running and that .env.local contains the correct URL.")
    exit 1
}

[pscustomobject]@{
    ServerName = $systemInfo.ServerName
    Version = $systemInfo.Version
    Id = $systemInfo.Id
}
