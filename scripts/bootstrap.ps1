$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$toolsPath = Join-Path $projectRoot '.tools'
New-Item -ItemType Directory -Force -Path $toolsPath | Out-Null
$installer = Join-Path $toolsPath 'dotnet-install.ps1'
Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
& $installer -Version 10.0.400 -InstallDir (Join-Path $toolsPath 'dotnet') -NoPath
if ($LASTEXITCODE -ne 0) { throw '.NET SDK installation failed.' }
& (Join-Path $PSScriptRoot 'build.ps1') -Check -Package
