$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$releaseTag=$env:GITHUB_EVENT_NAME -eq 'push' -and $env:GITHUB_REF -like 'refs/tags/v*'
$manualMain=$env:GITHUB_EVENT_NAME -eq 'workflow_dispatch' -and $env:GITHUB_REF -eq 'refs/heads/main' -and $env:DRIVERPILOT_SIGN_RELEASE -eq 'true'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or -not ($releaseTag -or $manualMain)){
 throw 'Schluesselimport nur fuer Release-Tags oder explizite main-Testbuilds auf GitHub-Runnern erlaubt.'
}
. (Join-Path $PSScriptRoot 'Signing.ps1')
$expected=Get-DriverPilotCertificate
Assert-DriverPilotCertificateValidity $expected
if(-not $env:DRIVERPILOT_SIGNING_PFX -or -not $env:DRIVERPILOT_SIGNING_PASSWORD){throw 'GitHub-Signiergeheimnisse fehlen.'}
$bytes=[Convert]::FromBase64String($env:DRIVERPILOT_SIGNING_PFX)
try {
 $flags=[Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet -bor [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet
 $certificate=[Security.Cryptography.X509Certificates.X509Certificate2]::new($bytes,$env:DRIVERPILOT_SIGNING_PASSWORD,$flags)
 if($certificate.Thumbprint -ne $expected.Thumbprint -or -not $certificate.HasPrivateKey){throw 'Falsches Signierzertifikat.'}
 Assert-DriverPilotCertificateValidity $certificate
 $store=[Security.Cryptography.X509Certificates.X509Store]::new('My','CurrentUser')
 try{$store.Open('ReadWrite');$store.Add($certificate)}finally{$store.Close()}
 # Trust only on the disposable runner, to verify the self-signed artifacts.
 Import-Certificate -FilePath (Join-Path $root 'assets/certificates/DriverPilot.cer') -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
} finally {
 [Array]::Clear($bytes,0,$bytes.Length)
 Remove-Item Env:\DRIVERPILOT_SIGNING_PFX,Env:\DRIVERPILOT_SIGNING_PASSWORD -ErrorAction SilentlyContinue
}
