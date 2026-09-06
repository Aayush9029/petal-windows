param([switch]$Package, [switch]$Check, [string]$CertificateThumbprint, [string]$SignToolPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
$sdk = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools'
Push-Location $projectRoot
try {
    & $sdk restore Petal.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked dependency restore failed.' }
    & $sdk build src/Petal.Windows -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if ($Check) {
        & $sdk run --project src/Petal.Checks -c Release --no-restore -- checks test-data
        if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
    }
    if ($Package) {
        & $sdk publish src/Petal.Windows -c Release -r win-x64 --self-contained true -o artifacts/Petal-win-x64 -p:DebugType=None -p:DebugSymbols=false -p:RestoreLockedMode=true
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        if ($CertificateThumbprint) {
            & (Join-Path $PSScriptRoot 'sign.ps1') -CertificateThumbprint $CertificateThumbprint -SignToolPath $SignToolPath
        }
        Copy-Item -LiteralPath LICENSE, THIRD-PARTY-NOTICES.md, README.md -Destination artifacts/Petal-win-x64
        Copy-Item -LiteralPath third-party -Destination artifacts/Petal-win-x64 -Recurse -Force
        Compress-Archive -Path artifacts/Petal-win-x64 -DestinationPath artifacts/Petal-win-x64.zip -Force
        $hash = (Get-FileHash artifacts/Petal-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  Petal-win-x64.zip" | Set-Content artifacts/SHA256SUMS.txt -Encoding ascii
        Get-Content artifacts/SHA256SUMS.txt
    }
} finally { Pop-Location }
