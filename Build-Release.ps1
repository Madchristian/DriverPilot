param([string]$Compiler='')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
[xml]$project=Get-Content -LiteralPath (Join-Path $root 'DriverPilot.csproj')
$version=[string]$project.Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Releaseversion muss Major.Minor.Patch entsprechen.'}
if($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version"){throw 'Tag und Projektversion stimmen nicht überein.'}
$release=Join-Path $root "release-$version"
$manifest=Get-Content -LiteralPath (Join-Path $release 'package-manifest.json') -Raw | ConvertFrom-Json
if($manifest.Product -ne 'DriverPilot' -or $manifest.Version -ne $version){throw 'Unpassendes Buildmanifest.'}
$dist=Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$payload=Join-Path $dist ('payload-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $payload | Out-Null
foreach($entry in $manifest.Files.PSObject.Properties){
 if([IO.Path]::GetFileName($entry.Name) -ne $entry.Name -or $entry.Name -match '[:/\\]'){throw 'Ungültiger Paketpfad.'}
 $source=Join-Path $release $entry.Name
 if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.Value){throw "Builddatei verändert: $($entry.Name)"}
 Copy-Item -LiteralPath $source -Destination $payload
}
Copy-Item -LiteralPath (Join-Path $release 'package-manifest.json'),(Join-Path $root 'README.md') -Destination $payload
Compress-Archive -LiteralPath @(Get-ChildItem -LiteralPath $payload -File | Select-Object -ExpandProperty FullName) -DestinationPath (Join-Path $dist "DriverPilot-$version-win-x64.zip") -Force
if(-not $Compiler){$Compiler=Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'}
if(-not(Test-Path -LiteralPath $Compiler)){throw 'Inno Setup 6 fehlt. Auf GitHub windows-2022 ist es vorinstalliert.'}
& $Compiler "/DAppVersion=$version" "/DPayloadDir=$payload" (Join-Path $root 'Installer.iss')
if($LASTEXITCODE -ne 0){throw 'Installer-Build fehlgeschlagen.'}
$assets=@(Join-Path $dist "DriverPilot-$version-Setup-x64.exe";Join-Path $dist "DriverPilot-$version-win-x64.zip")
$checksums=@($assets | ForEach-Object {if(-not(Test-Path -LiteralPath $_)){throw 'Releaseartefakt fehlt.'};"$((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))"})
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'),$checksums,[Text.UTF8Encoding]::new($false))
Write-Output "Release $version erstellt."
