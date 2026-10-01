# Definitions only: safe to import in local unit tests.
function Assert-LifecycleDataOutcome($Choice,[Collections.IDictionary]$Hashes,[Collections.IDictionary]$Categories){
 foreach($path in $Hashes.Keys){
  $category=$Categories[$path]
  if($category -notin @('Settings','Logs','Backups','Unselected')){throw "Unknown fixture category: $path"}
  $remove=($category -ne 'Unselected' -and $Choice.$category)
  if($remove){
   if(Test-Path -LiteralPath $path){throw "$($Choice.Name) selected data survived: $path"}
  }else{
   if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "$($Choice.Name) removed unselected data: $path"}
   if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $Hashes[$path]){throw "$($Choice.Name) changed retained bytes: $path"}
  }
 }
}

function Select-LifecycleQuitItem([object[]]$Candidates,[int]$ProcessId){
 $matches=@($Candidates | Where-Object {$_.Name -ceq 'Beenden' -and $_.ProcessId -eq $ProcessId -and $_.Enabled})
 if($matches.Count -ne 1){throw "Expected exactly one enabled tray Quit for PID $ProcessId; found $($matches.Count)"}
 $matches[0].Element
}

function Initialize-LifecycleAutomation {
 Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase
 Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LifecycleTrayMouse {
 [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
 public static void RightClick(int x, int y) {
  if(!SetCursorPos(x,y)) throw new InvalidOperationException("Cannot position tray pointer");
  mouse_event(0x0008,0,0,0,UIntPtr.Zero); mouse_event(0x0010,0,0,0,UIntPtr.Zero);
 }
}
'@
}

function Wait-LifecycleGuiReady([Diagnostics.Process]$Process){
 $deadline=[DateTime]::UtcNow.AddSeconds(120)
 do{
  if($Process.HasExited){throw 'Normal GUI exited during startup'}
  $window=[Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$Process.Id))
  if($window){
   $scan=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'ScanButton'))
   if($scan -and $scan.Current.IsEnabled){return}
  }
  Start-Sleep -Milliseconds 250
 }while([DateTime]::UtcNow -lt $deadline)
 throw 'Normal GUI did not finish initial read-only inventory scan within 120 seconds'
}

function Invoke-LifecycleTrayQuit([Diagnostics.Process]$Process){
 # Never use WM_CLOSE (hides to tray), Kill, injected code, or a helper uninstall bypass.
 # An interactive Explorer desktop is required; fail, rather than skip, if unavailable.
 if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'Tray interaction is restricted to the disposable lifecycle runner'}
 $desktop=[Windows.Automation.AutomationElement]::RootElement
 $trayName='DriverPilot '+[char]0xb7+' Stabilit'+[char]0xe4+'t zuerst'
 $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$trayName)
 $icons=@($desktop.FindAll([Windows.Automation.TreeScope]::Descendants,$condition) | Where-Object {-not $_.Current.IsOffscreen})
 if($icons.Count -eq 0){
  $chevrons=@($desktop.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Show hidden icons')) | Where-Object {-not $_.Current.IsOffscreen})
  if($chevrons.Count -ne 1){throw 'No unique visible tray overflow button; interactive English Explorer desktop required'}
  $chevrons[0].GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 500
  $icons=@($desktop.FindAll([Windows.Automation.TreeScope]::Descendants,$condition) | Where-Object {-not $_.Current.IsOffscreen})
 }
 if($icons.Count -ne 1){throw "Expected one visible DriverPilot tray icon; found $($icons.Count)"}
 $point=$icons[0].GetClickablePoint()
 [LifecycleTrayMouse]::RightClick([int]$point.X,[int]$point.Y)
 $deadline=[DateTime]::UtcNow.AddSeconds(10)
 do{
  $candidates=@($desktop.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::MenuItem)) | ForEach-Object {
   [pscustomobject]@{Name=$_.Current.Name;ProcessId=$_.Current.ProcessId;Enabled=($_.Current.IsEnabled -and -not $_.Current.IsOffscreen);Element=$_}
  })
  $owned=@($candidates | Where-Object {$_.Name -ceq 'Beenden' -and $_.ProcessId -eq $Process.Id})
  if($owned.Count -gt 0){break}
  Start-Sleep -Milliseconds 200
 }while([DateTime]::UtcNow -lt $deadline)
 $quit=Select-LifecycleQuitItem $candidates $Process.Id
 $quit.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
 if(-not $Process.WaitForExit(30000)){throw 'Tray Quit did not exit within 30 seconds; no forced termination or retry allowed'}
 $Process.WaitForExit()
 if($Process.ExitCode -ne 0){throw "Tray Quit exited with code $($Process.ExitCode)"}
}

function Get-UninstallChoices {
 foreach($mask in 0..7){
  $flags=@()
  if($mask -band 1){$flags+='/REMOVESETTINGS'}
  if($mask -band 2){$flags+='/REMOVELOGS'}
  if($mask -band 4){$flags+='/REMOVEBACKUPS'}
  [pscustomobject]@{Name="choice-$mask";Settings=[bool]($mask -band 1);Logs=[bool]($mask -band 2);Backups=[bool]($mask -band 4);Flags=$flags}
 }
}