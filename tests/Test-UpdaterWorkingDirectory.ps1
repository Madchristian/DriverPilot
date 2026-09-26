$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
# Execute the real launch initializers without the GUI, package verification or UAC.
# Only the elevation verb is cleared below; both ShellExecute modes remain intact.
$features=[IO.File]::ReadAllText((Join-Path $root 'src/DriverPilot/UI/Features.cs'))
$window=[IO.File]::ReadAllText((Join-Path $root 'src/DriverPilot/UI/MainWindow.xaml.cs'))
$updater=[regex]::Match($features,'var script=Path.Combine\(AppContext.BaseDirectory,"Setup.ps1"\);var psi=(new ProcessStartInfo\([^;]+);').Groups[1].Value
$helper=[regex]::Match($features,'var helper=(new ProcessStartInfo\([^;]+);').Groups[1].Value
$configure=[regex]::Match($window,'var psi=(new ProcessStartInfo\(Path.Combine\(Environment.GetFolderPath\(Environment.SpecialFolder.System\)[^;]+);').Groups[1].Value
if(-not $updater -or -not $helper -or -not $configure){throw 'Could not load production launch initializers.'}
Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Diagnostics;
public static class UpdaterLaunchProbe {
 public static ProcessStartInfo Updater() { return $updater; }
 public static ProcessStartInfo Helper() { return $helper; }
 public static ProcessStartInfo Configure() { return $configure; }
}
"@
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'installer/Setup.ps1'),[ref]$tokens,[ref]$errors)
if($errors){throw ($errors|Out-String)}
# Never execute the installer body, change ACLs, tasks, registry or certificate stores.
foreach($f in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){Invoke-Expression $f.Extent.Text}
$base=if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}
$tree=Join-Path $base ('DriverPilot-cwd-test-'+[guid]::NewGuid().ToString('N'))
$savedNative=[Environment]::CurrentDirectory;$savedLocation=Get-Location
$failures=@()
try{
 foreach($mode in @('Updater','Helper','Configure','DirectSetup')){
  $case=Join-Path $tree $mode
  $target=Join-Path $case 'installed';$next=Join-Path $case 'next';$previous=Join-Path $case 'previous'
  [void][IO.Directory]::CreateDirectory($target);[void][IO.Directory]::CreateDirectory($next)
  [IO.File]::WriteAllText((Join-Path $target 'old.txt'),'old')
  [IO.File]::WriteAllText((Join-Path $next 'new.txt'),'new')
  $child=$null
  try{
   # Model the installed shortcut's real Win32 cwd, not just PowerShell's location.
   Set-Location -LiteralPath $target
   [Environment]::CurrentDirectory=$target
   if($mode -ne 'DirectSetup'){
    $ready=Join-Path $case 'ready.txt';$stop=Join-Path $case 'stop.txt'
    $command="[IO.File]::WriteAllText('$($ready.Replace("'","''"))',[Environment]::CurrentDirectory);while(-not [IO.File]::Exists('$($stop.Replace("'","''"))')){Start-Sleep -Milliseconds 50}"
    $psi=switch($mode){'Updater'{[UpdaterLaunchProbe]::Updater()};'Helper'{[UpdaterLaunchProbe]::Helper()};'Configure'{[UpdaterLaunchProbe]::Configure()}}
    $psi.Verb=''
    $psi.Arguments='-NoProfile -EncodedCommand '+[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $child=[Diagnostics.Process]::Start($psi)
    $deadline=[DateTime]::UtcNow.AddSeconds(15)
    while(-not [IO.File]::Exists($ready)){
     if($child.HasExited -or [DateTime]::UtcNow -gt $deadline){throw 'Child failed to become ready'}
     Start-Sleep -Milliseconds 50
    }
    # GUI has exited/released its own cwd; only the update child still holds it.
    Set-Location -LiteralPath $savedLocation.Path
    [Environment]::CurrentDirectory=$savedNative
    Write-Output "$mode child native cwd: $([IO.File]::ReadAllText($ready))"
   }
   Publish-PackageDirectory $target $next $previous
   if(-not [IO.File]::Exists((Join-Path $target 'new.txt')) -or -not [IO.File]::Exists((Join-Path $previous 'old.txt'))){throw 'Replacement did not preserve expected trees'}
   if($child -and $child.HasExited){throw 'Child exited before replacement assertion'}
   Write-Output "PASS $mode permits real directory replacement from installed-shortcut cwd"
  }catch{
   $failures+="$mode : $($_.Exception.Message)"
   Write-Output "FAIL $($failures[-1])"
  }finally{
   Set-Location -LiteralPath $savedLocation.Path
   [Environment]::CurrentDirectory=$savedNative
   if($child){[IO.File]::WriteAllText($stop,'stop');if(-not $child.WaitForExit(5000)){$child.Kill();$child.WaitForExit()};$child.Dispose()}
  }
 }
}finally{
 Set-Location -LiteralPath $savedLocation.Path
 [Environment]::CurrentDirectory=$savedNative
 if(Test-Path -LiteralPath $tree){Remove-Item -LiteralPath $tree -Recurse -Force}
}
if($failures.Count){throw ($failures -join "`n")}
Write-Output 'PASS working-directory regression (isolated scratch only; no UAC or installation)'