# Publishes Screencap for Windows as a single self-contained Screencap.exe and zips it.
# Run on Windows (embedding the exe icon and manifest only works there).
# Usage: pwsh scripts/package-windows.ps1 [-Runtime win-x64|win-arm64]
param([string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content "$root/Directory.Build.props")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$publish = "$root/artifacts/publish-$Runtime"
$out = "$root/artifacts/release"
New-Item -ItemType Directory -Force -Path $out | Out-Null
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

dotnet publish "$root/src/Screencap.App/Screencap.App.csproj" -c Release `
  -f net10.0-windows10.0.19041.0 -r $Runtime --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
# Native debug symbols from the Skia/HarfBuzz packages are not needed by users.
Get-ChildItem $publish -Filter *.pdb | Remove-Item

$label = if ($Runtime -eq "win-arm64") { "arm64" } else { "x64" }
$zip = "$out/Screencap-$version-windows-$label.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$publish/*" -DestinationPath $zip
Write-Output $zip
