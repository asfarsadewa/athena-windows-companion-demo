[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$InnoSetupPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "AthenaCompanion\AthenaCompanion.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\$Runtime"
$installerDir = Join-Path $repoRoot "artifacts\installer"
$installerScript = Join-Path $repoRoot "installer\athena-companion.iss"
$iconPath = Join-Path $repoRoot "AthenaCompanion\Assets\Icons\athena.ico"

if ([string]::IsNullOrWhiteSpace($InnoSetupPath)) {
    $command = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($command) {
        $InnoSetupPath = $command.Source
    }
}

if ([string]::IsNullOrWhiteSpace($InnoSetupPath)) {
    $programFilesX86 = ${env:ProgramFiles(x86)}
    $candidate = Join-Path $programFilesX86 "Inno Setup 6\ISCC.exe"
    if (Test-Path -LiteralPath $candidate) {
        $InnoSetupPath = $candidate
    }
}

if ([string]::IsNullOrWhiteSpace($InnoSetupPath) -or -not (Test-Path -LiteralPath $InnoSetupPath)) {
    throw "Inno Setup compiler not found. Install Inno Setup 6 or pass -InnoSetupPath."
}

Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publishDir, $installerDir -Force | Out-Null

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDir `
    /p:Version=$Version `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true

$isccArgs = @(
    "/DMyAppVersion=$Version",
    "/DSourceDir=$publishDir",
    "/DOutputDir=$installerDir",
    "/DIconFile=$iconPath",
    $installerScript
)

& $InnoSetupPath @isccArgs
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installer = Join-Path $installerDir "AthenaCompanionSetup-$Version.exe"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Expected installer was not created: $installer"
}

Write-Host "Installer created: $installer"
