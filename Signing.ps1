$ErrorActionPreference='Stop'

function Get-DriverPilotCertificate {
 $publicPath=Join-Path $PSScriptRoot 'certificates\DriverPilot.cer'
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
 if(-not $private.HasPrivateKey -or $private.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow -or $private.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow){throw 'Manifest signing key is not currently valid.'}
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
 if(-not $private.HasPrivateKey -or $private.NotAfter -le (Get-Date)){throw 'Signierschluessel fehlt oder ist abgelaufen.'}
 & (Get-DriverPilotSignTool) sign /sha1 $expected.Thumbprint /s My /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $Path
 if($LASTEXITCODE -ne 0){throw "Signierung fehlgeschlagen: $Path"}
 Assert-DriverPilotSignature $Path
}
