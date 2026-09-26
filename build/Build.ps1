param([string]$Configuration='Release',[switch]$Sign,[string]$Dotnet='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$sdk=$Dotnet
if(-not $sdk){$sdk=Join-Path $root '.tools\dotnet\dotnet.exe'}
if(-not(Test-Path -LiteralPath $sdk)){$sdk='dotnet'}
[xml]$project=Get-Content -LiteralPath (Join-Path $root 'src/DriverPilot/DriverPilot.csproj')
$version=[string]$project.Project.PropertyGroup.Version
$out=Join-Path $root "artifacts/release-$version"
# Never reuse a prior signed output or its detached signature for an unsigned build.
if(Test-Path -LiteralPath $out){Remove-Item -LiteralPath $out -Recurse -Force}
& $sdk publish (Join-Path $root 'src/DriverPilot/DriverPilot.csproj') -c $Configuration -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -p:SatelliteResourceLanguages=en -o $out
if($LASTEXITCODE -ne 0){throw 'Build fehlgeschlagen.'}
if(@(Get-ChildItem -LiteralPath $out -Directory).Count){throw 'Package must be flat; unexpected publish directories.'}
if($Sign){
 . (Join-Path $PSScriptRoot 'Signing.ps1')
 foreach($name in @('DriverPilot.exe','DriverPilot.dll','Setup.ps1')){Sign-DriverPilotArtifact (Join-Path $out $name)}
}
$files=[ordered]@{}
Get-ChildItem -LiteralPath $out -File | Sort-Object Name | ForEach-Object {$files[$_.Name]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
$json=[ordered]@{Product='DriverPilot';Version=$version;Files=$files} | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $out 'package-manifest.json'),$json,[Text.UTF8Encoding]::new($false))
if($Sign){
 Sign-DriverPilotManifest (Join-Path $out 'package-manifest.json')
 Add-Type -Path (Join-Path $root 'src/DriverPilot/Security/PackageTrust.cs')
 [void][DriverPilot.Security.PackageTrust]::Verify($out,(Join-Path $root 'assets/certificates/DriverPilot.cer'),'0.0.0',$false)
 Write-Output "Authenticated package created: $out"
}else{Write-Output "UNSIGNED DEVELOPMENT BUILD ONLY (not installable/updateable): $out"}
