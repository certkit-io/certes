param(
  # The project to pack (relative to this script)
  [string] $Project = ".\src\Certes\Certes.csproj",

  # Base version (prefix) for packages: "3.0.0" => "3.0.0-certkit.<N>"
  [string] $BaseVersion = "3.0.0",

  # Suffix label to use
  [string] $SuffixLabel = "certkit",

  # Destination folder feed (relative to this script)
  [string] $FeedDir = ".\.nuget-local",

  # Configuration
  [ValidateSet("Debug","Release")]
  [string] $Configuration = "Release",

  # Should Sign?
  [switch] $Sign,

  # If set, uses exactly this version (no auto-increment)
  [string] $Version,

  # If set, don't copy packages into the feed folder
  [switch] $NoCopy,

  # Additional output directory for the nupkg (relative or absolute)
  [string] $OutputDir,

  # Max packages to keep per package ID in the feed folder (oldest pruned first)
  [int] $MaxFeedVersions = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-HerePath([string] $p) {
  if ([System.IO.Path]::IsPathRooted($p)) { return $p }
  return (Resolve-Path (Join-Path $PSScriptRoot $p)).Path
}

function Ensure-Dir([string] $p) {
  if (-not (Test-Path $p)) { New-Item -ItemType Directory -Path $p | Out-Null }
}

function Get-NextBuildNumber([string] $feedDir, [string] $packageId, [string] $prefix) {
  # Finds existing packages like: <packageId>.<prefix>-certkit.<N>.nupkg
  # Returns next N (starting at 1)
  if (-not (Test-Path $feedDir)) { return 1 }

  $pattern = [regex]::Escape("$packageId.$prefix-$SuffixLabel.") + "(\d+)\.nupkg$"
  $max = 0

  Get-ChildItem -Path $feedDir -Filter "$packageId.$prefix-$SuffixLabel.*.nupkg" -File -ErrorAction SilentlyContinue |
    ForEach-Object {
      $m = [regex]::Match($_.Name, $pattern)
      if ($m.Success) {
        $n = [int]$m.Groups[1].Value
        if ($n -gt $max) { $max = $n }
      }
    }

  return ($max + 1)
}

# --- Resolve paths ---
$projPath = Resolve-HerePath $Project
if (-not (Test-Path $projPath)) {
  throw "Project not found: $projPath"
}

$feedPath = Resolve-HerePath $FeedDir
if (-not $NoCopy) { Ensure-Dir $feedPath }

$outputPath = $null
if (-not [string]::IsNullOrWhiteSpace($OutputDir)) {
  if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    $outputPath = $OutputDir
  } else {
    $outputPath = (Join-Path (Get-Location).Path $OutputDir)
  }
  Ensure-Dir $outputPath
}

# --- Determine PackageId from the csproj (simple XML read) ---
[xml]$csproj = Get-Content -Raw -Path $projPath

function Get-XmlFirstText([xml] $doc, [string] $xpath) {
  $n = $doc.SelectSingleNode($xpath)
  if ($null -eq $n) { return $null }
  $t = $n.InnerText
  if ([string]::IsNullOrWhiteSpace($t)) { return $null }
  return $t.Trim()
}

$packageId = Get-XmlFirstText $csproj "//Project/PropertyGroup/PackageId"
if (-not $packageId) {
  # NuGet default is AssemblyName (or project file name), so we fall back similarly.
  $packageId = Get-XmlFirstText $csproj "//Project/PropertyGroup/AssemblyName"
}
if (-not $packageId) {
  $packageId = [System.IO.Path]::GetFileNameWithoutExtension($projPath)
}

Write-Host "PackageId    : $packageId"

# --- Choose version ---
if ([string]::IsNullOrWhiteSpace($Version)) {
  $next = Get-NextBuildNumber -feedDir $feedPath -packageId $packageId -prefix $BaseVersion
  $Version = "$BaseVersion-$SuffixLabel.$next"
}

Write-Host "Project      : $projPath"
Write-Host "PackageId    : $packageId"
Write-Host "Configuration: $Configuration"
Write-Host "Version      : $Version"
Write-Host "FeedDir      : $feedPath"
Write-Host "Sign         : $Sign"
Write-Host ""

# --- Pack ---
$env:CERTES_PACKAGE_VERSION = $Version

$packArgs = @(
  "pack", $projPath,
  "-c", $Configuration,
  "-p:ContinuousIntegrationBuild=true",
  "-p:IncludeSymbols=true",
  "-p:SymbolPackageFormat=snupkg"
)

if (-not $Sign) {
  $packArgs += "-p:SkipSigning=true"
}

Write-Host "Running: dotnet $($packArgs -join ' ')"
dotnet @packArgs

# --- Find produced packages ---
$pkgOutDir = Join-Path (Split-Path $projPath -Parent) ("bin\" + $Configuration)
if (-not (Test-Path $pkgOutDir)) {
  throw "Expected output folder not found: $pkgOutDir"
}

# Look recursively because TFMs may introduce subfolders depending on pack settings
$nupkg = Get-ChildItem -Path $pkgOutDir -Recurse -File -Filter "$packageId.$Version.nupkg" | Select-Object -First 1
$snupkg = Get-ChildItem -Path $pkgOutDir -Recurse -File -Filter "$packageId.$Version.snupkg" | Select-Object -First 1

if (-not $nupkg) { throw "Did not find nupkg: $packageId.$Version.nupkg under $pkgOutDir" }
Write-Host "Built .nupkg : $($nupkg.FullName)"
if ($snupkg) { Write-Host "Built .snupkg: $($snupkg.FullName)" }

# --- Copy to feed ---
if (-not $NoCopy) {
  Copy-Item -Force $nupkg.FullName -Destination $feedPath
  if ($snupkg) { Copy-Item -Force $snupkg.FullName -Destination $feedPath }

  Write-Host ""
  Write-Host "Copied package(s) to feed:"
  Write-Host "  $feedPath"

  # --- Prune old versions in feed ---
  $feedPkgs = Get-ChildItem -Path $feedPath -Filter "$packageId.*.nupkg" -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notlike "*.symbols.nupkg" } |
    Sort-Object -Property LastWriteTime -Descending

  if ($feedPkgs.Count -gt $MaxFeedVersions) {
    $toRemove = $feedPkgs | Select-Object -Skip $MaxFeedVersions
    foreach ($old in $toRemove) {
      $oldSnupkg = Join-Path $feedPath ([System.IO.Path]::ChangeExtension($old.Name, ".snupkg"))
      Remove-Item -Force $old.FullName
      if (Test-Path $oldSnupkg) { Remove-Item -Force $oldSnupkg }
      Write-Host "  Pruned: $($old.Name)"
    }
  }

  Write-Host ""
  Write-Host "Consume with:"
  Write-Host "  <PackageReference Include=""$packageId"" Version=""$Version"" />"
} else {
  Write-Host ""
  Write-Host "NoCopy set; not copying into feed."
}

# --- Copy to OutputDir ---
if ($outputPath) {
  Copy-Item -Force $nupkg.FullName -Destination $outputPath
  if ($snupkg) { Copy-Item -Force $snupkg.FullName -Destination $outputPath }

  Write-Host ""
  Write-Host "Copied package(s) to output dir:"
  Write-Host "  $outputPath"
}