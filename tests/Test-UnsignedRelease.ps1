$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
# No compiler/build output is needed: unsigned release packaging must reject at entry.
try{& (Join-Path $root 'Build-Release.ps1');throw 'SECURITY: unsigned release packaging accepted'}catch{
 if($_.Exception.Message -notmatch 'Unsigned builds are not release/update packages'){throw}
}
Write-Output 'PASS unsigned release packaging fails closed before tools or artifacts'
