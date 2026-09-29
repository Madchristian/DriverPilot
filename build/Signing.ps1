$ErrorActionPreference='Stop'
$script:DriverPilotSigningRoot=Split-Path $PSScriptRoot -Parent

function Assert-DriverPilotCertificateValidity {
 param(
  [Parameter(Mandatory=$true)][Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
  [DateTime]$Now=[DateTime]::UtcNow
 )
 $utc=$Now.ToUniversalTime()
 if($Certificate.NotBefore.ToUniversalTime() -gt $utc -or $Certificate.NotAfter.ToUniversalTime() -le $utc){
  throw 'Signing certificate is not currently valid.'
 }
 if(($Certificate.NotAfter.ToUniversalTime()-$utc).TotalDays -le 90){
  Write-Warning "Signing certificate expires within 90 days ($($Certificate.NotAfter.ToUniversalTime().ToString('u'))). Follow docs/security/SIGNING-LIFECYCLE.md."
 }
}

function Get-DriverPilotCertificate {
 $publicPath=Join-Path $script:DriverPilotSigningRoot 'assets/certificates/DriverPilot.cer'
 return [Security.Cryptography.X509Certificates.X509Certificate2]::new($publicPath)
}

function Get-DriverPilotSignTool {
 $sdkBin=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
 $tool=Get-ChildItem -Path "$sdkBin\*\x64\signtool.exe" | Sort-Object FullName -Descending | Select-Object -First 1
 if(-not $tool){throw 'Windows SDK SignTool fehlt.'}
 return $tool.FullName
}

function Assert-DriverPilotSignature([string]$Path) {
 $expected=Get-DriverPilotCertificate
 $signature=Get-AuthenticodeSignature -LiteralPath $Path
 if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $expected.Thumbprint){
  throw "Ungueltige DriverPilot-Signatur: $Path ($($signature.Status))"
 }
 if(-not $signature.TimeStamperCertificate){throw "Signatur ohne Zeitstempel: $Path"}
 & (Get-DriverPilotSignTool) verify /pa /all /tw $Path
 if($LASTEXITCODE -ne 0){throw "SignTool-Pruefung fehlgeschlagen: $Path"}
}

function Sign-DriverPilotManifest([string]$Path) {
 $expected=Get-DriverPilotCertificate
 $private=Get-Item -LiteralPath "Cert:\CurrentUser\My\$($expected.Thumbprint)" -ErrorAction Stop
 Assert-DriverPilotCertificateValidity $private
 if(-not $private.HasPrivateKey){throw 'Manifest signing key is missing.'}
 $rsa=[Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($private)
 try{
  if($rsa.KeySize -lt 2048){throw 'Manifest signing key too small.'}
  $signature=$rsa.SignData([IO.File]::ReadAllBytes($Path),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
  [IO.File]::WriteAllBytes(([IO.Path]::ChangeExtension($Path,'sig')),$signature)
 }finally{$rsa.Dispose();$private.Dispose();$expected.Dispose()}
}

function Sign-DriverPilotArtifact([string]$Path) {
 $expected=Get-DriverPilotCertificate
 $private=Get-Item -LiteralPath "Cert:\CurrentUser\My\$($expected.Thumbprint)" -ErrorAction Stop
 Assert-DriverPilotCertificateValidity $private
 if(-not $private.HasPrivateKey){throw 'Signierschluessel fehlt.'}
 & (Get-DriverPilotSignTool) sign /sha1 $expected.Thumbprint /s My /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $Path
 if($LASTEXITCODE -ne 0){throw "Signierung fehlgeschlagen: $Path"}
 Assert-DriverPilotSignature $Path
}
