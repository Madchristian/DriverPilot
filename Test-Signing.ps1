param([switch]$ApplicationOnly)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Signing.ps1')
[xml]$project=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'DriverPilot.csproj')
$version=[string]$project.Project.PropertyGroup.Version
$release=Join-Path $PSScriptRoot "release-$version"
Add-Type -Path (Join-Path $PSScriptRoot 'PackageTrust.cs')
$package=[DriverPilot.Security.PackageTrust]::Verify($release,(Join-Path $PSScriptRoot 'certificates\DriverPilot.cer'),'0.0.0',$false)
$names=@('DriverPilot.exe','DriverPilot.dll','Setup.ps1')
foreach($name in $names){Assert-DriverPilotSignature (Join-Path $release $name)}
$public=Get-DriverPilotCertificate
if($public.HasPrivateKey){throw 'Oeffentliches Zertifikat enthaelt einen privaten Schluessel.'}

# Corrupt the signed program and script in temporary copies, never the release.
foreach($name in @('DriverPilot.exe','Setup.ps1')){
 $copy=Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N')+[IO.Path]::GetExtension($name))
 try {
  $bytes=[IO.File]::ReadAllBytes((Join-Path $release $name))
  $bytes[512]=$bytes[512] -bxor 1
  [IO.File]::WriteAllBytes($copy,$bytes)
  $rejected=$false
  try{Assert-DriverPilotSignature $copy}catch{$rejected=$true}
  if(-not $rejected){throw "Veraenderte Datei akzeptiert: $name"}
 } finally {if(Test-Path -LiteralPath $copy){Remove-Item -LiteralPath $copy}}
}
if(-not $ApplicationOnly){
 $dist=Join-Path $PSScriptRoot 'dist'
 Assert-DriverPilotSignature (Join-Path $dist "DriverPilot-$version-Setup-x64.exe")
 if((Get-FileHash (Join-Path $dist 'DriverPilot.cer')).Hash -ne (Get-FileHash (Join-Path $PSScriptRoot 'certificates\DriverPilot.cer')).Hash){throw 'Falsches Releasezertifikat.'}
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $dist "DriverPilot-$version-win-x64.zip"))
 try {
  if(@($zip.Entries | Where-Object {$_.FullName -match '\.(pfx|p12|key)$'}).Count){throw 'Privater Schluessel im ZIP.'}
  $inventory=@($package.Files.Keys)+@('package-manifest.json','package-manifest.sig')
  if($zip.Entries.Count -ne $inventory.Count){throw 'ZIP inventory mismatch.'}
  $seen=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
  foreach($item in $zip.Entries){if(-not $seen.Add($item.FullName) -or $item.FullName -cnotin $inventory){throw 'Unlisted or duplicate ZIP entry.'}}
  foreach($name in $inventory){
   $entry=$zip.GetEntry($name)
   if(-not $entry){throw "Datei fehlt im ZIP: $name"}
   $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
   try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
   if($hash -ne (Get-FileHash (Join-Path $release $name)).Hash){throw "ZIP enthaelt andere Datei: $name"}
  }
 } finally {$zip.Dispose()}
}
'PASS: Expected signer, trusted signatures, timestamps, EXE/script tamper rejection and public-only certificate.'
