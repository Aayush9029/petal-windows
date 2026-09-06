param([string]$PackageDirectory = (Join-Path $PSScriptRoot '../artifacts/Petal-win-x64'), [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/sandbox'), [switch]$Launch)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$results = Join-Path $output 'results'
New-Item -ItemType Directory -Force -Path $results | Out-Null
$escape = { param($s) [Security.SecurityElement]::Escape($s) }
$xml = @"
<Configuration>
  <Networking>Disable</Networking>
  <vGPU>Disable</vGPU>
  <AudioInput>Disable</AudioInput>
  <VideoInput>Disable</VideoInput>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <PrinterRedirection>Disable</PrinterRedirection>
  <ProtectedClient>Enable</ProtectedClient>
  <MemoryInMB>8192</MemoryInMB>
  <MappedFolders>
    <MappedFolder><HostFolder>$(& $escape $package)</HostFolder><SandboxFolder>C:\Petal</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$(& $escape $results)</HostFolder><SandboxFolder>C:\PetalResults</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>C:\Petal\Petal.exe --ui-smoke C:\PetalData C:\PetalResults Dark</Command></LogonCommand>
</Configuration>
"@
$file = Join-Path $output 'Petal.wsb'
$xml | Set-Content -LiteralPath $file -Encoding UTF8
[xml](Get-Content -LiteralPath $file -Raw) | Out-Null
Write-Output $file
if($Launch) { Start-Process -FilePath $file }
