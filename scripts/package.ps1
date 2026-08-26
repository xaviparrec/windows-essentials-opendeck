$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$pluginFolder = Join-Path $projectRoot 'net.parrec.deck.windows-essentials.sdPlugin'
$releaseFolder = Join-Path $projectRoot 'release'
$zipPath = Join-Path $releaseFolder 'Windows-Essentials-0.16.3.zip'
$packagePath = Join-Path $releaseFolder 'Windows-Essentials-0.16.3.streamDeckPlugin'

New-Item -ItemType Directory -Force -Path $releaseFolder | Out-Null
Remove-Item -Force -ErrorAction SilentlyContinue $zipPath, $packagePath
Compress-Archive -Path $pluginFolder -DestinationPath $zipPath -Force
Move-Item -Path $zipPath -Destination $packagePath
Write-Host "Created $packagePath"
