[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [string]$RevitVersion = "2025",
    [string]$AddinsRoot = ""
)

$ErrorActionPreference = "Stop"

if (Get-Process -Name "Revit" -ErrorAction SilentlyContinue) {
    Write-Host "Закройте Revit перед установкой VentCalc."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($AddinsRoot)) {
    $AddinsRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot "VentCalc.sln"
$publishSource = Join-Path $repoRoot "VentCalc.Revit\bin\$Platform\$Configuration\net8.0-windows"
$installDir = Join-Path $AddinsRoot "VentCalc"
$addinPath = Join-Path $AddinsRoot "VentCalc.addin"
$assemblyPath = Join-Path $installDir "VentCalc.Revit.dll"

function Get-MSBuildPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($path) {
            return $path
        }
    }

    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw "MSBuild.exe was not found. Install Visual Studio 2022 with MSBuild tools, or add MSBuild.exe to PATH."
}

$msBuildPath = Get-MSBuildPath
Write-Host "Building $solutionPath ($Configuration|$Platform)..."
& $msBuildPath $solutionPath /m /restore /p:Configuration=$Configuration /p:Platform=$Platform
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $publishSource)) {
    throw "Build output folder was not found: $publishSource"
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Write-Host "Cleaning $installDir..."
Remove-Item -Path (Join-Path $installDir "*") -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Copying add-in files to $installDir..."
Copy-Item -Path (Join-Path $publishSource "*") -Destination $installDir -Recurse -Force

if (-not (Test-Path $assemblyPath)) {
    throw "VentCalc.Revit.dll was not copied to: $assemblyPath"
}

New-Item -ItemType Directory -Path $AddinsRoot -Force | Out-Null
$addinXml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>VentCalc</Name>
    <Assembly>$assemblyPath</Assembly>
    <AddInId>4F16E999-51D5-4E0D-94C4-843134A41F2E</AddInId>
    <FullClassName>VentCalc.Revit.App</FullClassName>
    <VendorId>VCAL</VendorId>
    <VendorDescription>VentCalc ventilation engineering add-in</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Set-Content -Path $addinPath -Value $addinXml -Encoding UTF8

Write-Host "DLL folder: $installDir"
Write-Host "Add-in manifest: $addinPath"
Write-Host "Assembly path: $assemblyPath"
Write-Host "VentCalc установлен. Можно запускать Revit."
