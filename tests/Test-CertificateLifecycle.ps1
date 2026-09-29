$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'build/Signing.ps1')
$rsa=[Security.Cryptography.RSA]::Create(2048)
$certificate=$null
try {
 $request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=DriverPilot lifecycle TEST ONLY',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
 $now=[DateTimeOffset]::UtcNow
 $certificate=$request.CreateSelfSigned($now.AddDays(-1),$now.AddDays(120))
 $rejected=$false
 try {Assert-DriverPilotCertificateValidity $certificate -Now $now.AddDays(121).UtcDateTime}catch{
  if($_.Exception.Message -notlike '*not currently valid*'){throw}
  $rejected=$true
 }
 if(-not $rejected){throw 'Expired signing certificate accepted.'}
 'PASS: Expired certificate rejected.'
 foreach($instant in @($now.AddDays(-2).UtcDateTime,$certificate.NotAfter.ToUniversalTime())){
  $rejected=$false
  try {Assert-DriverPilotCertificateValidity $certificate -Now $instant}catch{$rejected=$true}
  if(-not $rejected){throw 'Invalid certificate boundary accepted.'}
 }
 Assert-DriverPilotCertificateValidity $certificate -Now $certificate.NotBefore.ToUniversalTime()
 $warnings=@()
 Assert-DriverPilotCertificateValidity $certificate -Now $now.UtcDateTime -WarningVariable warnings
 if($warnings.Count){throw 'Early expiry warning.'}
 Assert-DriverPilotCertificateValidity $certificate -Now $now.AddDays(31).UtcDateTime -WarningVariable warnings -WarningAction SilentlyContinue
 if($warnings.Count -ne 1){throw 'Missing 90-day expiry warning.'}
 Assert-DriverPilotCertificateValidity $certificate -Now $certificate.NotAfter.ToUniversalTime().AddDays(-90) -WarningVariable warnings -WarningAction SilentlyContinue
 if($warnings.Count -ne 1){throw 'Missing warning at exact 90-day boundary.'}
 # Encrypted offline-backup rehearsal using ONLY this disposable test key.
 $base=if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}
 $scratch=Join-Path $base ('DriverPilot-lifecycle-'+[guid]::NewGuid().ToString('N'))
 [IO.Directory]::CreateDirectory($scratch) | Out-Null
 $pfx=$null;$restored=$null;$public=$null;$password=$null
 try {
  $password=[guid]::NewGuid().ToString('N')+[guid]::NewGuid().ToString('N')
  $pfx=$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$password)
  $backup=Join-Path $scratch 'test-only.pfx'
  [IO.File]::WriteAllBytes($backup,$pfx)
  $flags=[Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
  $wrongPasswordRejected=$false
  try {
   $bad=[Security.Cryptography.X509Certificates.X509Certificate2]::new($backup,'wrong-password',$flags)
   $bad.Dispose()
  }catch{$wrongPasswordRejected=$true}
  if(-not $wrongPasswordRejected){throw 'Unencrypted backup accepted wrong password.'}
  $restored=[Security.Cryptography.X509Certificates.X509Certificate2]::new($backup,$password,$flags)
  if(-not $restored.HasPrivateKey -or $restored.Thumbprint -ne $certificate.Thumbprint){throw 'Restore identity mismatch.'}
  $public=[Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
  if($public.HasPrivateKey){throw 'Public export includes private key.'}
  $publicPath=Join-Path $scratch 'test-only.cer'
  [IO.File]::WriteAllBytes($publicPath,$public.RawData)
  & (Join-Path $root 'build/Test-CertificateExpiry.ps1') -CertificatePath $publicPath -FailOnWarning
  $near=$request.CreateSelfSigned($now.AddDays(-1),$now.AddDays(30))
  try {[IO.File]::WriteAllBytes($publicPath,$near.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))}finally{$near.Dispose()}
  $blocked=$false
  try {& (Join-Path $root 'build/Test-CertificateExpiry.ps1') -CertificatePath $publicPath -FailOnWarning}catch{
   if($_.Exception.Message -notlike '*expires within 90 days*'){throw}
   $blocked=$true
  }
  if(-not $blocked){throw 'Expiry monitor did not fail inside warning window.'}
  function Get-DriverPilotCertificate {return $public}
  function Get-Item {param($LiteralPath) return $restored}
  $manifest=Join-Path $scratch 'package-manifest.json'
  [IO.File]::WriteAllText($manifest,'{"test":"isolated restore rehearsal"}')
  Sign-DriverPilotManifest $manifest
  $verify=[Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($certificate)
  try {
   if(-not $verify.VerifyData([IO.File]::ReadAllBytes($manifest),[IO.File]::ReadAllBytes((Join-Path $scratch 'package-manifest.sig')),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)){throw 'Restored key signature invalid.'}
   [IO.File]::AppendAllText($manifest,'tampered')
   if($verify.VerifyData([IO.File]::ReadAllBytes($manifest),[IO.File]::ReadAllBytes((Join-Path $scratch 'package-manifest.sig')),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)){throw 'Tampered restore probe accepted.'}
  }finally{$verify.Dispose()}
  'PASS: Encrypted PFX restore, wrong-password rejection, public-only export, manifest signing and expiry monitor.'
 } finally {
  if($restored){$restored.Dispose()}
  if($public){$public.Dispose()}
  if($pfx){[Array]::Clear($pfx,0,$pfx.Length)}
  $password=$null
  Remove-Item -LiteralPath $scratch -Recurse -Force
 }
 # Substitute only certificate lookup; invalid keys must fail before any signing/file IO.
 function Get-DriverPilotCertificate {return $script:testCertificate}
 function Get-Item {param($LiteralPath) return $script:testCertificate}
 function Get-DriverPilotSignTool {throw 'SignTool reached with invalid certificate.'}
 foreach($range in @(@(1,10),@(-10,-1))){
  $script:testCertificate=$request.CreateSelfSigned($now.AddDays($range[0]),$now.AddDays($range[1]))
  try {
   foreach($signer in @('Sign-DriverPilotArtifact','Sign-DriverPilotManifest')){
    $rejected=$false
    try {& $signer 'nonexistent-test-file'}catch{
     if($_.Exception.Message -notlike '*not currently valid*'){throw}
     $rejected=$true
    }
    if(-not $rejected){throw "$signer accepted invalid certificate."}
   }
  } finally {$script:testCertificate.Dispose()}
 }
 'PASS: Both signing paths reject expired and future keys before signing.'
} finally {
 if($certificate){$certificate.Dispose()}
 $rsa.Dispose()
}
