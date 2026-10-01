# Safe real-process regression: starts only --wait-for, suspends, and terminates its own children.
param([Parameter(Mandatory=$true)][string]$AppPath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'installer/Setup.ps1'),[ref]$tokens,[ref]$errors)
if($errors){throw ($errors|Out-String)}
$gate=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Assert-SetupRuntimeIdle'},$false))
if($gate.Count -ne 1){throw 'Missing productive gate'}
Invoke-Expression $gate[0].Extent.Text
Add-Type -Path (Join-Path $PSScriptRoot 'UpgradeFaults.cs')
$unknownPaths=0
foreach($i in 1..10){
 $p=Start-Process -FilePath $AppPath -ArgumentList "--wait-for $PID" -WorkingDirectory $env:SystemRoot -PassThru
 try{
  [UpgradeProcessHold]::Suspend($p)
  if($p.HasExited){throw 'Fixture exited before process gate probe'}
  if(-not (Get-Process -Id $p.Id -ErrorAction Stop).Path){$unknownPaths++}
  $seen=@(Get-CimInstance Win32_Process -Filter "Name='DriverPilot.exe'" -ErrorAction Stop | Where-Object {$_.ProcessId -eq $p.Id})
  if($seen.Count -ne 1){throw 'Actual fixture not visible to productive query'}
  $blocked=$false
  try{Assert-SetupRuntimeIdle}catch{if($_.Exception.Message -notlike 'DriverPilot laeuft noch*'){throw};$blocked=$true}
  if(-not $blocked -or $p.HasExited){throw 'Live fixture escaped productive gate'}
 }finally{if(-not $p.HasExited){$p.Kill();$p.WaitForExit()};$p.Dispose()}
}
"PASS productive setup guard rejected all 10 live suspended app starts; unreadable paths observed: $unknownPaths (deterministic null-path coverage is in Test-SetupProcessGate)."
