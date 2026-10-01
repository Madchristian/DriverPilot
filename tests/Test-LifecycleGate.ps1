param([string]$AppPath='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source=Join-Path $root 'src/DriverPilot/Infrastructure/LifecycleGate.cs'
if(-not(Test-Path -LiteralPath $source)){throw 'FAIL: app startup has no shared uninstall/lifecycle gate.'}
 $probe=@'
using System;
using System.Threading;
public static class GateProbe {
 public static bool CanAcquire(string name) {
  bool result=false;
  var thread=new Thread(()=>{using(var gate=DriverPilot.LifecycleGate.TryEnter(name)){result=gate!=null;}});
  thread.Start();thread.Join();return result;
 }
}
'@
Add-Type -TypeDefinition ($probe+[IO.File]::ReadAllText($source).Replace('using System;','').Replace('using System.Threading;',''))
$name='Local\DriverPilot.GateTest.'+[guid]::NewGuid().ToString('N')
$gate=[DriverPilot.LifecycleGate]::TryEnter($name)
if(-not $gate){throw 'Fresh gate acquisition failed'}
try{if([GateProbe]::CanAcquire($name)){throw 'Competing startup entered held uninstall gate'}}finally{$gate.Dispose()}
if(-not [GateProbe]::CanAcquire($name)){throw 'Gate not released after successful operation'}
$app=[IO.File]::ReadAllText((Join-Path $root 'src/DriverPilot/App.xaml.cs'))
if($app -notmatch 'LifecycleGate.TryEnter\('){throw 'App does not use production lifecycle gate'}
$workerPath=Join-Path $root 'src/DriverPilot/Scripts/Maintenance.ps1'
$worker=[IO.File]::ReadAllText($workerPath)
if($worker -notmatch 'Global\\DriverPilot.Maintenance'){throw 'Maintenance child must own its cross-session gate even if its parent exits'}
$held=[DriverPilot.LifecycleGate]::TryEnter('Global\DriverPilot.Maintenance')
if(-not $held){throw 'Maintenance is active; refusal test cannot start'}
try{
 $p=Start-Process -FilePath "$PSHOME\powershell.exe" -ArgumentList @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',('"'+$workerPath+'"')) -WorkingDirectory $env:SystemRoot -PassThru
 $null=$p.Handle
 try{
  if(-not $p.WaitForExit(15000)){$p.Kill();throw 'Blocked maintenance child did not exit'}
  if($p.ExitCode -ne 0){throw 'Blocked maintenance child must skip work successfully'}
 }finally{$p.Dispose()}
}finally{$held.Dispose()}
'PASS lifecycle gate blocks concurrent startup and releases correctly'
if($AppPath){
 $held=[DriverPilot.LifecycleGate]::TryEnter('Global\DriverPilot.UninstallLifecycle')
 if(-not $held){throw 'Cannot acquire isolated startup test gate'}
 try{
  foreach($mode in @('--tray','--maintenance')){
   $p=Start-Process -FilePath ([IO.Path]::GetFullPath($AppPath)) -ArgumentList $mode -WorkingDirectory $env:SystemRoot -PassThru
   $null=$p.Handle
   try{
    if(-not $p.WaitForExit(15000)){$p.Kill();throw 'Blocked app startup did not exit'}
    if($p.ExitCode -ne 1){throw "App startup $mode ignored uninstall gate: $($p.ExitCode)"}
   }finally{$p.Dispose()}
  }
 }finally{$held.Dispose()}
 'PASS actual application refuses GUI and maintenance startup while uninstall gate is held'
}
