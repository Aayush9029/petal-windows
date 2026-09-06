param(
    [Parameter(Mandatory=$true)][string]$CertificateThumbprint,
    [Parameter(Mandatory=$true)][string]$SignToolPath,
    [string]$Directory = (Join-Path $PSScriptRoot '../artifacts/Petal-win-x64'),
    [string]$TimestampUrl = 'https://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) { throw 'Provide the Windows SDK signtool.exe path.' }
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
if (!$certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date)) { throw 'A valid code-signing certificate with its private key is required.' }
if ('1.3.6.1.5.5.7.3.3' -notin $certificate.EnhancedKeyUsageList.ObjectId) { throw 'Certificate is not approved for code signing.' }
foreach ($name in @('Petal.exe','Petal.dll','Petal.Core.dll')) {
    $file = Join-Path $Directory $name
    & $SignToolPath sign /sha1 $CertificateThumbprint /s My /fd SHA256 /tr $TimestampUrl /td SHA256 $file
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $name" }
    & $SignToolPath verify /pa /all $file
    if ($LASTEXITCODE -ne 0) { throw "Trusted signature verification failed: $name" }
    if ((Get-AuthenticodeSignature -LiteralPath $file).Status -ne 'Valid') { throw "Signature is not trusted: $name" }
}
