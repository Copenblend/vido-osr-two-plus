# package.ps1 — Build and package the OSR2+ Plugin for Vido
# Produces: vido-osr2-plus-<version>.zip in the local plugin registry packages folder.

$ErrorActionPreference = 'Stop'

$version     = '1.0.0'
$pluginId    = 'com.vido.osr2-plus'
$projectDir  = Join-Path $PSScriptRoot 'src\Osr2PlusPlugin'
$publishDir  = Join-Path $PSScriptRoot 'publish'
$stageDir    = Join-Path $PSScriptRoot "stage\$pluginId"
$zipName     = "vido-osr2-plus-$version.zip"
$registryDir = 'C:\source\testRegistry'
$packagesDir = Join-Path $registryDir 'packages'
$zipPath     = Join-Path $packagesDir $zipName

Write-Host "=== Building OSR2+ Plugin v$version ===" -ForegroundColor Cyan

# 1. Clean previous artifacts
foreach ($dir in @($publishDir, (Join-Path $PSScriptRoot 'stage'))) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# 2. Publish the project
Write-Host 'Publishing...'
dotnet publish "$projectDir\Osr2PlusPlugin.csproj" -c Release -o $publishDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

# 3. Stage: copy published output into a folder named after the plugin id
Write-Host 'Staging...'
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

# Copy all published files EXCEPT Vido.Core.dll (host already has it)
Get-ChildItem "$publishDir\*" -Recurse |
    Where-Object { $_.Name -ne 'Vido.Core.dll' } |
    ForEach-Object {
        $rel = $_.FullName.Substring($publishDir.Length + 1)
        $dest = Join-Path $stageDir $rel
        $destDir = Split-Path $dest -Parent
        if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
        Copy-Item $_.FullName $dest
    }

# Copy plugin manifest
Copy-Item "$projectDir\plugin.json" $stageDir

# Copy Assets folder (icons, etc.)
$assetsDir = Join-Path $PSScriptRoot 'Assets'
if (Test-Path $assetsDir) {
    Copy-Item $assetsDir "$stageDir\Assets" -Recurse
}

# Copy docs if present
foreach ($file in @('README.md', 'CHANGELOG.md')) {
    $src = Join-Path $PSScriptRoot $file
    if (Test-Path $src) { Copy-Item $src $stageDir }
}

# 4. Create zip package in the local registry packages folder
Write-Host "Creating $zipName..."
if (-not (Test-Path $packagesDir)) { New-Item $packagesDir -ItemType Directory -Force | Out-Null }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $PSScriptRoot 'stage\*') -DestinationPath $zipPath -Force

# 5. Cleanup staging
Remove-Item (Join-Path $PSScriptRoot 'stage') -Recurse -Force

Write-Host ''
Write-Host "Package: $zipPath" -ForegroundColor Green
Write-Host 'Install via Vido > Plugins using the local registry.' -ForegroundColor Cyan
