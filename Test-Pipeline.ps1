param([switch]$Installer)
$ErrorActionPreference='Stop'
[xml]$project=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'DriverPilot.csproj')
$version=[string]$project.Project.PropertyGroup.Version
$release=Join-Path $PSScriptRoot "release-$version"
if($Installer){
 if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'Installations-Smoke-Test ausschließlich auf wegwerfbaren GitHub-Runnern erlaubt.'}
 $exe=Join-Path $PSScriptRoot "dist\DriverPilot-$version-Setup-x64.exe"
 $log=Join-Path $PSScriptRoot 'dist\installer-test.log'
 $p=Start-Process -FilePath $exe -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+$log+'"')) -PassThru -WindowStyle Hidden
 if(-not $p.WaitForExit(180000)){$p.Kill();throw 'Installer-Test hat das Zeitlimit überschritten.'}
 if($p.ExitCode -ne 0){throw "Installer fehlgeschlagen: $($p.ExitCode)"}
 $task=Get-ScheduledTask -TaskName 'DriverPilot Maintenance' -ErrorAction Stop
 Disable-ScheduledTask -TaskName $task.TaskName | Out-Null
 if($task.Actions.Execute -ne (Join-Path $env:ProgramFiles 'DriverPilot\DriverPilot.exe')){throw 'Unerwartete Wartungsaktion.'}
 $manifest=Get-Content -LiteralPath (Join-Path $release 'package-manifest.json') -Raw | ConvertFrom-Json
 foreach($entry in $manifest.Files.PSObject.Properties){if((Get-FileHash -LiteralPath (Join-Path "$env:ProgramFiles\DriverPilot" $entry.Name)).Hash -ne $entry.Value){throw "Installierte Datei falsch: $($entry.Name)"}}
 'Installer erfolgreich: geschützte Installation, Dateiprüfsummen und Wartungsauftrag geprüft; Auftrag anschließend deaktiviert. Keine Treiber installiert.'
 exit 0
}
foreach($script in @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1')+@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Scripts') -Filter '*.ps1')){
 $tokens=$null;$errors=$null
 [void][Management.Automation.Language.Parser]::ParseFile($script.FullName,[ref]$tokens,[ref]$errors)
 if($errors){throw ($errors | Out-String)}
}
foreach($mode in @('self','transport')){
 $p=Start-Process -FilePath (Join-Path $release 'DriverPilot.exe') -ArgumentList "--$mode-test" -PassThru -WindowStyle Hidden
 if(-not $p.WaitForExit(240000)){$p.Kill();throw "$mode-Test hat das Zeitlimit überschritten."}
 if($p.ExitCode -ne 0){Get-ChildItem -LiteralPath $release -Filter '*failure.txt' | ForEach-Object {Get-Content -LiteralPath $_.FullName};throw "$mode-Test fehlgeschlagen: $($p.ExitCode)"}
}
Get-ChildItem -LiteralPath $release -Filter '*results.txt' | ForEach-Object {Get-Content -LiteralPath $_.FullName}
