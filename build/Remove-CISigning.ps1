$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'Nur auf GitHub-Runnern erlaubt.'}
. (Join-Path $PSScriptRoot 'Signing.ps1')
$thumbprint=(Get-DriverPilotCertificate).Thumbprint
$privatePath="Cert:\CurrentUser\My\$thumbprint"
if(Test-Path -LiteralPath $privatePath){Remove-Item -LiteralPath $privatePath -DeleteKey}
$rootPath="Cert:\LocalMachine\Root\$thumbprint"
if(Test-Path -LiteralPath $rootPath){Remove-Item -LiteralPath $rootPath}
