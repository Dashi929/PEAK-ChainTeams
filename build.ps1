# Builds ChainTeams and deploys it to BepInEx/plugins.
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1 [-Package]
param(
    [switch]$Package
)

$ErrorActionPreference = "Stop"
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$gameRoot = Resolve-Path (Join-Path $projectDir "..\..")

dotnet build $projectDir -c Release
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$dll = Join-Path $projectDir "bin\Release\ChainTeams.dll"
$plugins = Join-Path $gameRoot "BepInEx\plugins"
New-Item -ItemType Directory -Force -Path $plugins | Out-Null
Copy-Item $dll $plugins -Force
Write-Host "deployed -> $plugins\ChainTeams.dll"

if ($Package) {
    $out = Join-Path $projectDir "package"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    Copy-Item $dll $out
    Copy-Item (Join-Path $projectDir "README.md") $out -ErrorAction SilentlyContinue
    $zip = Join-Path $projectDir ("ChainTeams-" + (Get-Date -Format "yyyyMMdd-HHmm") + ".zip")
    Compress-Archive -Path "$out\*" -DestinationPath $zip
    Write-Host "packaged -> $zip"
}
