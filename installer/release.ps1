# release.ps1
# Builds every release asset on your own PC, the same way the GitHub release workflow does:
#   publish\portable\SpaceSharp.exe        one-file portable exe
#   Releases\SpaceSharp-win-Setup.exe      Velopack installer
#   Releases\SpaceSharp-win-x64.msi        Windows Installer package, branded
#   Releases\SpaceSharp-win-Portable.zip   auto-updating portable
#   Releases\*.nupkg, releases.win.json    update packages
#
# Needs the .NET 10 SDK and vpk (dotnet tool install -g vpk). Run from anywhere:
#
#   .\installer\release.ps1              # build everything
#   .\installer\release.ps1 -SkipDelta   # first release, or offline: no delta package
#   .\installer\release.ps1 -Upload      # also upload to a GitHub release (needs $env:GITHUB_TOKEN)

param(
    [switch]$SkipDelta,
    [switch]$Upload
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$repo = "https://github.com/ClearanceClarence/SpaceSharp"
$csproj = Get-Content .\SpaceSharp\SpaceSharp.csproj -Raw
if ($csproj -notmatch '<Version>([^<]+)</Version>') { throw "No <Version> in SpaceSharp.csproj" }
$version = $Matches[1]
"SpaceSharp $version"

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk is not installed. Run: dotnet tool install -g vpk"
}

"== Tests"
dotnet test --configuration Release
if ($LASTEXITCODE) { throw "Tests failed" }

"== Portable exe"
dotnet publish .\SpaceSharp\SpaceSharp.csproj -p:PublishProfile=Portable
if ($LASTEXITCODE) { throw "Portable publish failed" }

"== Velopack publish folder"
dotnet publish .\SpaceSharp\SpaceSharp.csproj -p:PublishProfile=Velopack
if ($LASTEXITCODE) { throw "Velopack publish failed" }

if (-not $SkipDelta) {
    "== Previous release (for the delta package)"
    vpk download github --repoUrl $repo
    if ($LASTEXITCODE) { Write-Warning "Could not download the previous release; building without a delta package." }
}

"== vpk pack"
vpk pack `
    --packId SpaceSharp `
    --packVersion $version `
    --packDir .\publish\velopack `
    --mainExe SpaceSharp.exe `
    --packTitle SpaceSharp `
    --packAuthors ClearanceClarence `
    --icon .\SpaceSharp\Assets\SpaceSharp.ico `
    --splashImage .\SpaceSharp\Assets\SpaceSharp-256.png `
    --msi `
    --instLocation Either `
    --instWelcome .\installer\welcome.md `
    --instLicense .\installer\license.txt `
    --instConclusion .\installer\conclusion.md
if ($LASTEXITCODE) { throw "vpk pack failed" }

"== Branding the MSI"
& (Join-Path $PSScriptRoot "brand-msi.ps1")

if ($Upload) {
    if (-not $env:GITHUB_TOKEN) { throw "Set `$env:GITHUB_TOKEN to a token with repo access before using -Upload." }
    "== Uploading to GitHub"
    vpk upload github --repoUrl $repo --token $env:GITHUB_TOKEN --publish --releaseName "SpaceSharp $version"
    if ($LASTEXITCODE) { throw "Upload failed" }
}

""
"Release assets:"
Get-ChildItem .\Releases | ForEach-Object { "  Releases\$($_.Name)" }
"  publish\portable\SpaceSharp.exe"
if (-not $Upload) { "Attach all of them to the GitHub release, or rerun with -Upload." }
