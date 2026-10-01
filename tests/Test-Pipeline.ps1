param([switch]$Installer)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[xml]$project=Get-Content -LiteralPath (Join-Path $root 'src/DriverPilot/DriverPilot.csproj')
$version=[string]$project.Project.PropertyGroup.Version
$release=Join-Path $root "artifacts/release-$version"
if($Installer){
 if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'Installations-Smoke-Test ausschließlich auf wegwerfbaren GitHub-Runnern erlaubt.'}
 $exe=Join-Path $root "artifacts/dist/DriverPilot-$version-Setup-x64.exe"
 & (Join-Path $PSScriptRoot 'Test-UninstallLifecycle.ps1') -InstallerPath $exe
 exit 0
}
& (Join-Path $PSScriptRoot 'Test-InstallationContract.ps1')
& (Join-Path $PSScriptRoot 'Test-SetupProcessGate.ps1')
& (Join-Path $PSScriptRoot 'Test-SetupProcessGateNative.ps1') -AppPath (Join-Path $release 'DriverPilot.exe')
& (Join-Path $PSScriptRoot 'Test-LifecycleGate.ps1') -AppPath (Join-Path $release 'DriverPilot.exe')
& (Join-Path $PSScriptRoot 'Test-Uninstall.ps1')
$scriptDirectories=@('build','tests','installer','src/DriverPilot/Scripts')
foreach($script in @($scriptDirectories | ForEach-Object {Get-ChildItem -LiteralPath (Join-Path $root $_) -Filter '*.ps1' -File})){
 $tokens=$null;$errors=$null
 [void][Management.Automation.Language.Parser]::ParseFile($script.FullName,[ref]$tokens,[ref]$errors)
 if($errors){throw ($errors | Out-String)}
}
$originalRelease=$release
$testRoot=if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}
$release=Join-Path $testRoot ('DriverPilot-app-tests-'+[guid]::NewGuid().ToString('N'))
Copy-Item -LiteralPath $originalRelease -Destination $release -Recurse
foreach($mode in @('self','transport','system-report','remote-transparency')){
 $p=Start-Process -FilePath (Join-Path $release 'DriverPilot.exe') -ArgumentList "--$mode-test" -PassThru -WindowStyle Hidden
 if(-not $p.WaitForExit(240000)){$p.Kill();throw "$mode-Test hat das Zeitlimit überschritten."}
 if($p.ExitCode -ne 0){Get-ChildItem -LiteralPath $release -Filter '*failure.txt' | ForEach-Object {Get-Content -LiteralPath $_.FullName};throw "$mode-Test fehlgeschlagen: $($p.ExitCode)"}
}
Get-ChildItem -LiteralPath $release -Filter '*results.txt' | ForEach-Object {Get-Content -LiteralPath $_.FullName}
Remove-Item -LiteralPath $release -Recurse -Force
