param([string]$Compiler='', [switch]$Sign)
$ErrorActionPreference='Stop'
if(-not $Sign){throw 'Unsigned builds are not release/update packages.'}
$root=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'src/DriverPilot/Security/PackageTrust.cs')
[xml]$project=Get-Content -LiteralPath (Join-Path $root 'src/DriverPilot/DriverPilot.csproj')
$version=[string]$project.Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Releaseversion muss Major.Minor.Patch entsprechen.'}
if($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version"){throw 'Tag und Projektversion stimmen nicht überein.'}
$release=Join-Path $root "artifacts/release-$version"
if($env:GITHUB_REF_TYPE -eq 'tag' -and -not $Sign){throw 'Release-Tags muessen signiert werden.'}
if($Sign){
 . (Join-Path $PSScriptRoot 'Signing.ps1')
 foreach($name in @('DriverPilot.exe','DriverPilot.dll','Setup.ps1')){Assert-DriverPilotSignature (Join-Path $release $name)}
}
$manifest=[DriverPilot.Security.PackageTrust]::Verify($release,(Join-Path $root 'assets/certificates/DriverPilot.cer'),'0.0.0',$false)
if($manifest.Product -ne 'DriverPilot' -or $manifest.Version -ne $version){throw 'Unpassendes Buildmanifest.'}
$dist=Join-Path $root 'artifacts/dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$payload=Join-Path $dist ('payload-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $payload | Out-Null
[DriverPilot.Security.PackageTrust]::CopyVerified($release,$payload,$manifest)
$verified=[DriverPilot.Security.PackageTrust]::Verify($payload,(Join-Path $root 'assets/certificates/DriverPilot.cer'),'0.0.0',$false)
[DriverPilot.Security.PackageTrust]::AssertApproval($verified,[DriverPilot.Security.PackageTrust]::ManifestHash($manifest))
if($Sign){
 Copy-Item -LiteralPath (Join-Path $root 'assets/certificates/DriverPilot.cer'),(Join-Path $root 'docs/security/ZERTIFIKAT-ANLEITUNG.txt') -Destination $dist

}
Compress-Archive -LiteralPath @(Get-ChildItem -LiteralPath $payload -File | Select-Object -ExpandProperty FullName) -DestinationPath (Join-Path $dist "DriverPilot-$version-win-x64.zip") -Force
if(-not $Compiler){$Compiler=Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'}
if(-not(Test-Path -LiteralPath $Compiler)){throw 'Inno Setup 6 fehlt. Auf GitHub windows-2022 ist es vorinstalliert.'}
& $Compiler "/DAppVersion=$version" "/DPayloadDir=$payload" "/O$dist" (Join-Path $root 'installer/Installer.iss')
if($LASTEXITCODE -ne 0){throw 'Installer-Build fehlgeschlagen.'}
if($Sign){Sign-DriverPilotArtifact (Join-Path $dist "DriverPilot-$version-Setup-x64.exe")}
$assets=@(Join-Path $dist "DriverPilot-$version-Setup-x64.exe";Join-Path $dist "DriverPilot-$version-win-x64.zip")
if($Sign){$assets+=@(Join-Path $dist 'DriverPilot.cer';Join-Path $dist 'ZERTIFIKAT-ANLEITUNG.txt')}
$checksums=@($assets | ForEach-Object {if(-not(Test-Path -LiteralPath $_)){throw 'Releaseartefakt fehlt.'};"$((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))"})
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'),$checksums,[Text.UTF8Encoding]::new($false))
Write-Output "Release $version erstellt."
