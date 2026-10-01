$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
function Test-DriverPilotCISigningAuthorization([Collections.IDictionary]$Environment,[string]$CheckedOutSha){
 if($Environment.GITHUB_ACTIONS -cne 'true' -or $Environment.RUNNER_ENVIRONMENT -cne 'github-hosted'){return $false}
 $releaseTag=$Environment.GITHUB_EVENT_NAME -ceq 'push' -and $Environment.GITHUB_REF -clike 'refs/tags/v*'
 $manualMain=$Environment.GITHUB_EVENT_NAME -ceq 'workflow_dispatch' -and $Environment.GITHUB_REF -ceq 'refs/heads/main' -and $Environment.DRIVERPILOT_SIGN_RELEASE -ceq 'true'
 if($releaseTag -or $manualMain){return $true}
 # This gate binds a manually approved workflow run, not a security boundary against
 # malicious workflow edits. Protect its environment and review the exact SHA first.
 return ($Environment.GITHUB_EVENT_NAME -ceq 'workflow_dispatch' -and
  $Environment.GITHUB_REF -clike 'refs/heads/*' -and
  $Environment.GITHUB_REPOSITORY -ceq 'Madchristian/DriverPilot' -and
  $Environment.DRIVERPILOT_SIGN_UNINSTALL_TEST -ceq 'true' -and
  $Environment.DRIVERPILOT_SIGN_RELEASE -cne 'true' -and
  $Environment.GITHUB_SHA -cmatch '\A[0-9a-f]{40}\z' -and
  $Environment.DRIVERPILOT_REVIEWED_SHA -ceq $Environment.GITHUB_SHA -and
  $CheckedOutSha -ceq $Environment.GITHUB_SHA -and
  $Environment.GITHUB_WORKFLOW_REF -ceq ($Environment.GITHUB_REPOSITORY+'/.github/workflows/uninstall-validation.yml@'+$Environment.GITHUB_REF))
}
$authorization=@{}
foreach($name in @('GITHUB_ACTIONS','RUNNER_ENVIRONMENT','GITHUB_EVENT_NAME','GITHUB_REF','GITHUB_REPOSITORY','GITHUB_SHA','GITHUB_WORKFLOW_REF','DRIVERPILOT_SIGN_UNINSTALL_TEST','DRIVERPILOT_REVIEWED_SHA','DRIVERPILOT_SIGN_RELEASE')){
 $authorization[$name]=[Environment]::GetEnvironmentVariable($name)
}
$checkedOutSha=''
if($authorization.DRIVERPILOT_SIGN_UNINSTALL_TEST -ceq 'true'){
 $checkedOutSha=(& git -C $root rev-parse HEAD | Out-String).Trim()
 if($LASTEXITCODE -ne 0){throw 'Reviewed checkout identity unavailable; no signing key imported.'}
}
if(-not (Test-DriverPilotCISigningAuthorization $authorization $checkedOutSha)){
 throw 'Schluesselimport nur fuer Release-Tags, explizite main-Testbuilds oder SHA-gebundene manuelle Deinstallationstests auf GitHub-Runnern erlaubt.'
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
