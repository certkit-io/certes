param(
  # Path to the .nupkg file to publish
  [Parameter(Mandatory)]
  [string] $Package,

  # GitHub owner (user or org) for the NuGet feed
  [string] $Owner = "certkit-io",

  # API key (GitHub PAT with write:packages scope). Falls back to GITHUB_TOKEN env var.
  [string] $ApiKey
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# --- Resolve package path ---
if (-not [System.IO.Path]::IsPathRooted($Package)) {
  $Package = Join-Path (Get-Location).Path $Package
}
if (-not (Test-Path $Package)) {
  throw "Package not found: $Package"
}

# --- Resolve API key ---
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
  $ApiKey = $env:GITHUB_TOKEN
}
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
  throw "No API key provided. Pass -ApiKey or set the GITHUB_TOKEN environment variable."
}

$feedUrl = "https://nuget.pkg.github.com/$Owner/index.json"

Write-Host "Package : $Package"
Write-Host "Feed    : $feedUrl"
Write-Host ""

dotnet nuget push $Package --source $feedUrl --api-key $ApiKey --skip-duplicate

if ($LASTEXITCODE -ne 0) {
  throw "dotnet nuget push failed with exit code $LASTEXITCODE"
}

# --- Also push snupkg if it exists alongside the nupkg ---
$snupkg = [System.IO.Path]::ChangeExtension($Package, ".snupkg")
if (Test-Path $snupkg) {
  Write-Host ""
  Write-Host "Pushing symbols: $snupkg"
  dotnet nuget push $snupkg --source $feedUrl --api-key $ApiKey --skip-duplicate
}

Write-Host ""
Write-Host "Done."
