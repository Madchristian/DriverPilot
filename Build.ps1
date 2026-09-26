param([string]$Configuration='Release',[switch]$Sign)
$ErrorActionPreference='Stop'
$sdk=Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if(-not(Test-Path -LiteralPath $sdk)){$sdk='dotnet'}
[xml]$project=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'DriverPilot.csproj')
$version=[string]$project.Project.PropertyGroup.Version
$out=Join-Path $PSScriptRoot "release-$version"
& $sdk publish (Join-Path $PSScriptRoot 'DriverPilot.csproj') -c $Configuration -r win-x64 --self-contained true -o $out
if($LASTEXITCODE -ne 0){throw 'Build fehlgeschlagen.'}
if($Sign){
 . (Join-Path $PSScriptRoot 'Signing.ps1')
 foreach($name in @('DriverPilot.exe','DriverPilot.dll','Setup.ps1')){Sign-DriverPilotArtifact (Join-Path $out $name)}
}
$files=[ordered]@{}
Get-ChildItem -LiteralPath $out -File | Where-Object {$_.Name -ne 'package-manifest.json' -and $_.Extension -in @('.exe','.dll','.json','.ps1')} | Sort-Object Name | ForEach-Object {$files[$_.Name]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
@{Product='DriverPilot';Version=$version;Files=$files} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $out 'package-manifest.json') -Encoding UTF8
Write-Output "Paket erstellt: $out"
