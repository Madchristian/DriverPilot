param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$base=if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}
$fixture=Join-Path $base ('DriverPilot-security-'+[guid]::NewGuid().ToString('N'))
$old=$env:DRIVERPILOT_TEST_EXPORT
try{
 $env:DRIVERPILOT_TEST_EXPORT=$fixture
 & $Dotnet run --project (Join-Path $PSScriptRoot 'PackageSecurity/PackageSecurity.csproj')
 if($LASTEXITCODE -ne 0){throw 'C# package tests failed.'}
 Add-Type -Path (Join-Path $root 'src/DriverPilot/Security/PackageTrust.cs')
 # Reflection stays in tests. Production entry points cannot choose a fixture key.
 $verify=[DriverPilot.Security.PackageTrust].GetMethod('VerifyCore',[Reflection.BindingFlags]'Static,NonPublic')
 $cert=[Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $fixture 'test-signer.cer'))
 $package=Join-Path $fixture 'package'
 function VerifyFixture([string]$Path=$package){return $verify.Invoke($null,@($Path,$cert,'0.3.1',$true,[DateTime]::UtcNow))}
 function Reject([scriptblock]$Action,[string]$Label){try{& $Action}catch{Write-Output "PASS $Label";return};throw "SECURITY: accepted $Label"}
 $valid=VerifyFixture
 $ErrorActionPreference='Continue'
 try{$portableOutput=& "$PSHOME/powershell.exe" -NoProfile -File (Join-Path $root 'installer/Setup.ps1') -PackagePath $package -ApprovedManifestHash ('0'*64) 2>&1;$portableExit=$LASTEXITCODE}
 finally{$ErrorActionPreference='Stop'}
 if($portableExit -eq 0 -or ($portableOutput|Out-String) -notmatch 'Portable update elevation is not supported'){throw 'Portable Setup entrypoint failed to reject before loading candidate code'}
 Write-Output 'PASS real Setup entrypoint refuses portable update before staging or candidate execution'
 Write-Output 'PASS Windows PowerShell real RSA signature and payload verification'
 Reject {[DriverPilot.Security.PackageTrust]::Verify($package,(Join-Path $fixture 'test-signer.cer'),'0.3.1',$true)} 'swapped public certificate rejected by production pin'
 $signature=Join-Path $package 'package-manifest.sig';$saved=[IO.File]::ReadAllBytes($signature)
 Remove-Item -LiteralPath $signature
 Reject {VerifyFixture} 'missing detached signature in updater runtime'
 [IO.File]::WriteAllBytes($signature,$saved)
 $stage=Join-Path $fixture 'stage';[void][IO.Directory]::CreateDirectory($stage)
 [DriverPilot.Security.PackageTrust]::CopyVerified($package,$stage,$valid)
 [IO.File]::AppendAllText((Join-Path $stage 'Setup.ps1'),'tamper during staging')
 Reject {VerifyFixture $stage} 'staged payload mutation'
 Remove-Item -LiteralPath $stage -Recurse -Force
 [void][IO.Directory]::CreateDirectory($stage)
 $manifest=Join-Path $package 'package-manifest.json';$savedManifest=[IO.File]::ReadAllBytes($manifest)
 [IO.File]::WriteAllText($manifest,'{"Product":"attacker"}')
 [DriverPilot.Security.PackageTrust]::CopyVerified($package,$stage,$valid)
 $staged=VerifyFixture $stage
 [DriverPilot.Security.PackageTrust]::AssertApproval($staged,[DriverPilot.Security.PackageTrust]::ManifestHash($valid))
 Write-Output 'PASS staging retains authenticated metadata despite source replacement'
 [IO.File]::WriteAllBytes($manifest,$savedManifest)
 $junction=Join-Path $fixture 'junction'
 New-Item -ItemType Junction -Path $junction -Target $package | Out-Null
 try{
  Reject {VerifyFixture $junction} 'junction package root'
  Reject {[DriverPilot.Security.PackageTrust]::NoReparse((Join-Path $junction 'Setup.ps1'))} 'junction ancestor'
 }finally{[IO.Directory]::Delete($junction)}
 # Exercise the real signing helper with an in-memory fixture; the store lookup alone is mocked.
 . (Join-Path $root 'build/Signing.ps1')
 $testRsa=[Security.Cryptography.RSACng]::new(2048)
 $request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Ephemeral PowerShell signing fixture',$testRsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
 $testPrivate=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1),[DateTimeOffset]::UtcNow.AddDays(1))
 $testPublic=[Security.Cryptography.X509Certificates.X509Certificate2]::new($testPrivate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
 function Get-DriverPilotCertificate {return [Security.Cryptography.X509Certificates.X509Certificate2]::new($testPublic.RawData)}
 function Get-Item {param($LiteralPath,$ErrorAction) if($LiteralPath -ne "Cert:\CurrentUser\My\$($testPublic.Thumbprint)"){throw 'Unexpected fixture store lookup'};return $testPrivate}
 try{
  Sign-DriverPilotManifest $manifest
  [void]$verify.Invoke($null,@($package.PSObject.BaseObject,$testPublic.PSObject.BaseObject,'0.3.1',$true,[DateTime]::UtcNow))
  Write-Output 'PASS production manifest signing helper with ephemeral key only'
 }finally{Remove-Item Function:Get-Item;$testPrivate.Dispose();$testPublic.Dispose();$testRsa.Dispose()}
 & (Join-Path $PSScriptRoot 'Test-Updater.ps1')
 & (Join-Path $PSScriptRoot 'Test-UnsignedRelease.ps1')
 Write-Output 'PASS both runtimes; no installation/private production key/certificate-store changes'
}finally{
 $env:DRIVERPILOT_TEST_EXPORT=$old
 if($cert){$cert.Dispose()}
 if(Test-Path -LiteralPath $fixture){Remove-Item -LiteralPath $fixture -Recurse -Force}
}
