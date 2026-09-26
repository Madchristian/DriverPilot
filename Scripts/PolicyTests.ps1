$now=[datetime]'2026-09-26T12:00:00'
$u=[pscustomobject]@{DriverClass='Display';DriverHardwareID='PCI\TEST';DriverManufacturer='NVIDIA';Title='NVIDIA Display';InstallationBehavior=[pscustomobject]@{CanRequestUserInput=$false};LastDeploymentChangeTime=$now.AddDays(-30)}
$device=[pscustomobject]@{HardwareID=@('PCI\TEST');CompatibleID=@();ConfigManagerErrorCode=0}
function Assert-Result([string]$expected,[datetime]$seen=$now.AddDays(-20)){
 $actual=Get-PilotEligibility -Update $u -Devices @($device) -FirstSeen $seen -Now $now
 if($actual -ne $expected){throw "Expected '$expected', got '$actual'."}
}
Assert-Result 'Bereit für Wartungsfenster'
Assert-Result '14-tägige Beobachtungszeit läuft' ($now.AddDays(-13))
Assert-Result 'Bereit für Wartungsfenster' ($now.AddDays(-14))
$u.LastDeploymentChangeTime=$now.AddDays(-1);Assert-Result 'Paket wurde vor weniger als 14 Tagen geändert';$u.LastDeploymentChangeTime=$now.AddDays(-30)
$u.Title='NVIDIA Firmware';Assert-Result 'Firmware oder Vorabversion ausgeschlossen';$u.Title='NVIDIA Display'
$u.DriverClass='SCSIAdapter';Assert-Result 'Geräteklasse von Automatik ausgenommen';$u.DriverClass='Display'
$device.ConfigManagerErrorCode=22;Assert-Result 'Betroffenes Gerät ist deaktiviert';$device.ConfigManagerErrorCode=0
$u.DriverHardwareID='UNKNOWN';Assert-Result 'Hardwarezuordnung nicht eindeutig bestätigt';$u.DriverHardwareID='PCI\TEST'
$u.InstallationBehavior.CanRequestUserInput=$true;Assert-Result 'Benötigt interaktive Installation';$u.InstallationBehavior.CanRequestUserInput=$false
$u.DriverClass='System';Assert-Result 'Systempaket benötigt manuelle Prüfung'
'10 policy cases passed. No downloads or installations performed.'
$u.DriverClass='Display'
$u | Add-Member Identity ([pscustomobject]@{UpdateID='test-update'})
$device | Add-Member DeviceID 'device-test'
$controls=@{Days=@(0);StartHour=12;EndHour=17;ObservationDays=30;PauseUntil='';BlockedDevices=@();BlockedUpdates=@()}
if((Get-PilotEligibility $u @($device) $now.AddDays(-20) $now $controls) -ne '30-tägige Beobachtungszeit läuft'){throw 'Custom grace period failed'}
$controls.ObservationDays=1
if((Get-PilotEligibility $u @($device) $now.AddDays(-13) $now $controls) -ne '14-tägige Beobachtungszeit läuft'){throw 'Minimum grace protection failed'}
$controls.BlockedUpdates=@('test-update')
if((Get-PilotEligibility $u @($device) $now.AddDays(-20) $now $controls) -ne 'Dieses Updatepaket ist gesperrt'){throw 'Package block failed'}
$controls.BlockedUpdates=@();$controls.BlockedDevices=@('device-test')
if((Get-PilotEligibility $u @($device) $now.AddDays(-20) $now $controls) -ne 'Gerät auf installierter Version gehalten'){throw 'Device pin failed'}
$sunday=[datetime]'2026-09-27T12:00:00'
if(-not(Test-PilotWindow $controls $sunday)){throw 'Window start failed'}
if(Test-PilotWindow $controls $sunday.AddHours(5)){throw 'Window end failed'}
if(Test-PilotWindow $controls $sunday.AddDays(1)){throw 'Wrong day allowed'}
$controls.PauseUntil=$sunday.AddDays(1).ToString('o')
if(Test-PilotWindow $controls $sunday){throw 'Pause failed'}
'8 additional policy cases passed: user grace/minimum, package/device blocks, window boundaries, day and pause.'
$fixture=[IO.Path]::GetTempFileName()
try{
 foreach($bad in @('{"Days":[]}','{"ObservationDays":0}','{"StartHour":23,"EndHour":12}','{"BusyProcesses":["game;cmd"]}','{"Days":[9]}','{"NetworkLimitMB":0}')){
  [IO.File]::WriteAllText($fixture,$bad)
  $rejected=$false
  try{$null=Read-PilotControls -File $fixture}catch{$rejected=$true}
  if(-not $rejected){throw "Invalid controls accepted: $bad"}
 }
 [IO.File]::WriteAllText($fixture,'{"Days":[1,3],"ObservationDays":30,"BusyProcesses":["my-game"],"BlockedUpdates":["test-update"]}')
 $valid=Read-PilotControls -File $fixture
 if($valid.ObservationDays -ne 30 -or $valid.Days.Count -ne 2){throw 'Valid controls rejected'}
}finally{Remove-Item -LiteralPath $fixture}
'7 control-file validation cases passed. User preferences were not modified.'
