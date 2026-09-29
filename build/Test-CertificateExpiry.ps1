param(
 [string]$CertificatePath=(Join-Path (Split-Path $PSScriptRoot -Parent) 'assets/certificates/DriverPilot.cer'),
 [switch]$FailOnWarning
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Signing.ps1')
$certificate=[Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $CertificatePath).Path)
try {
 if($certificate.HasPrivateKey){throw 'Only a public certificate is permitted.'}
 if($FailOnWarning){Assert-DriverPilotCertificateValidity $certificate -WarningAction Stop}
 else {Assert-DriverPilotCertificateValidity $certificate}
 Write-Output "PASS: Public signing certificate valid until $($certificate.NotAfter.ToUniversalTime().ToString('u'))."
} finally {$certificate.Dispose()}
