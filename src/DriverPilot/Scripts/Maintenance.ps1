$globalMutex=[Threading.Mutex]::new($false,'Global\DriverPilot.Maintenance')
$globalHeld=$false
try{
 $globalHeld=$globalMutex.WaitOne(0)
 if(-not $globalHeld){exit 0}
$root=Join-Path $env:ProgramData 'DriverPilot'
if(-not(Test-Path -LiteralPath $root)){throw 'Die geschützte Installation fehlt.'}
$mutex=New-Object Threading.Mutex($false,'Local\DriverPilot.Maintenance')
if(-not $mutex.WaitOne(0)){exit 0}
function Save-State {
 $state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'maintenance.tmp') -Encoding UTF8
 Move-Item -LiteralPath (Join-Path $root 'maintenance.tmp') -Destination (Join-Path $root 'maintenance.json') -Force
}
function Note([string]$message) {$state.Status=$message; $state.Time=(Get-Date).ToString('o'); Save-State}
try {
 $path=Join-Path $root 'maintenance.json'
 $state=@{Status='Start';Time=(Get-Date).ToString('o');FirstSeen=@{};Pending=$null;History=@();Updates=@()}
 if(Test-Path -LiteralPath $path){
   $old=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
   foreach($p in $old.FirstSeen.PSObject.Properties){$state.FirstSeen[$p.Name]=$p.Value}
   $state.History=@($old.History); $state.Pending=$old.Pending
 }
 $controls=Read-PilotControls
 if($state.Pending){Note 'Vorherige Installation unvollständig dokumentiert. Automatik gestoppt; manuelle Prüfung erforderlich.'; exit 0}
 $sys=New-Object -ComObject Microsoft.Update.SystemInfo
 if($sys.RebootRequired -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending')){Note 'Neustart ausstehend. Bitte Windows selbst neu starten.';exit 0}
 Note 'Windows Update wird nach Treiberpaketen durchsucht.'
 $session=New-Object -ComObject Microsoft.Update.Session
 $session.ClientApplicationID='DriverPilot Wartung'
 $result=$session.CreateUpdateSearcher().Search("IsInstalled=0 and IsHidden=0 and Type='Driver'")
 if($result.ResultCode -ne 2){throw "Unvollständige Suche: $($result.ResultCode)"}
 $eligible=[System.Collections.Generic.List[object]]::new()
 $rows=[System.Collections.Generic.List[object]]::new()
 $devices=@(Get-CimInstance Win32_PnPEntity)
 foreach($u in $result.Updates){
   $id=$u.Identity.UpdateID+':'+$u.Identity.RevisionNumber
   if(-not $state.FirstSeen.ContainsKey($id)){$state.FirstSeen[$id]=(Get-Date).ToUniversalTime().ToString('o')}
   $reason=Get-PilotEligibility -Update $u -Devices $devices -FirstSeen ([datetime]$state.FirstSeen[$id]) -Now (Get-Date) -Controls $controls
   if($reason -eq 'Bereit für Wartungsfenster'){$eligible.Add($u)}
   $rows.Add([pscustomobject]@{Title=$u.Title;Reason=$reason;FirstSeen=$state.FirstSeen[$id]})
 }
 $state.Updates=@($rows.ToArray()); Save-State
 if(-not(Test-PilotWindow $controls (Get-Date))){Note "$($rows.Count) Pakete gefunden. Installation wartet auf das eingestellte Wartungsfenster oder Ende der Pause.";exit 0}
 if(-not $eligible.Count){Note 'Keine Pakete erfüllen aktuell die Stabilitätsregeln.';exit 0}
 Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class PilotIdle {
 [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO {public uint cbSize; public uint dwTime;}
 [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
 public static uint Seconds(){var i=new LASTINPUTINFO();i.cbSize=(uint)Marshal.SizeOf(i);if(!GetLastInputInfo(ref i))return 0;return unchecked((uint)Environment.TickCount-i.dwTime)/1000;}
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out RECT rect);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
 [StructLayout(LayoutKind.Sequential)] struct RECT {public int Left,Top,Right,Bottom;}
 public static int[] Foreground(){var h=GetForegroundWindow();RECT r;uint id;GetWindowThreadProcessId(h,out id);if(!GetWindowRect(h,out r))return new[]{0,0,0,0,0};return new[]{r.Left,r.Top,r.Right,r.Bottom,(int)id};}
}
'@
 Add-Type -AssemblyName System.Windows.Forms
 function Busy {
   if([PilotIdle]::Seconds() -lt 600){return $true}
   $rect=[PilotIdle]::Foreground()
   if($rect[4] -eq 0){return $true}
   $front=Get-Process -Id $rect[4] -ErrorAction SilentlyContinue
   if($front.ProcessName -ne 'explorer'){
    foreach($screen in [Windows.Forms.Screen]::AllScreens){$b=$screen.Bounds;if($rect[0] -le $b.Left -and $rect[1] -le $b.Top -and $rect[2] -ge $b.Right -and $rect[3] -ge $b.Bottom){return $true}}
   }
   if(Get-Process -Name 'wow','wowclassic','cs2','dota2','FortniteClient-Win64-Shipping','VALORANT-Win64-Shipping','Cyberpunk2077','eldenring','r5apex','League of Legends' -ErrorAction SilentlyContinue){return $true}
   $names=@(Get-Process | Select-Object -ExpandProperty ProcessName)
   foreach($name in @($controls.BusyProcesses)+@('obs64','Streamlabs','xsplit.core')){if($names -contains $name){return $true}}
   try {
    $net=@(Get-CimInstance Win32_PerfFormattedData_Tcpip_NetworkInterface -ErrorAction Stop)
    if(-not $net.Count){return $true}
    if(($net | Measure-Object BytesTotalPersec -Sum).Sum -ge ($controls.NetworkLimitMB*1MB)){return $true}
   } catch {return $true}
   return $false
 }
 if(Busy){Note 'Wartung verschoben: PC aktiv, Spiel/Stream, Netzwerklast oder Aktivitätsprüfung unklar.';exit 0}
 if(@($devices | Where-Object {$_.ConfigManagerErrorCode -notin @(0,22)}).Count){Note 'Vorhandene Gerätefehler: Automatische Installation bis zur Prüfung zurückgestellt.';exit 0}
 $backup=Join-Path $root ('Backups\'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
 New-Item -ItemType Directory -Path $backup -Force | Out-Null
 & "$env:SystemRoot\System32\pnputil.exe" /export-driver '*' $backup | Out-Null
 if($LASTEXITCODE -ne 0){throw 'Treibersicherung fehlgeschlagen. Keine Installation gestartet.'}
 foreach($u in $eligible){
  $controls=Read-PilotControls
  if(-not(Test-PilotWindow $controls (Get-Date))){Note 'Wartungsfenster beendet oder pausiert.';exit 0}
  $id=$u.Identity.UpdateID+':'+$u.Identity.RevisionNumber
  if((Get-PilotEligibility -Update $u -Devices @(Get-CimInstance Win32_PnPEntity) -FirstSeen ([datetime]$state.FirstSeen[$id]) -Now (Get-Date) -Controls $controls) -ne 'Bereit für Wartungsfenster'){continue}
  if(Busy){Note 'Weitere Pakete verschoben: PC wird verwendet.';exit 0}
  $sys=New-Object -ComObject Microsoft.Update.SystemInfo
  if($sys.RebootRequired){Note 'Neustart erforderlich. Weitere Pakete warten.';exit 0}
  if(-not $u.EulaAccepted){$u.AcceptEula()}
  $collection=New-Object -ComObject Microsoft.Update.UpdateColl
  [void]$collection.Add($u)
  Note "Download: $($u.Title)"
  $downloader=$session.CreateUpdateDownloader();$downloader.Updates=$collection
  $download=$downloader.Download()
  if($download.ResultCode -ne 2){throw "Download fehlgeschlagen: $($u.Title)"}
  if(Busy){Note 'Download abgeschlossen. Installation wartet auf Leerlauf.';exit 0}
  $controls=Read-PilotControls
  if(-not(Test-PilotWindow $controls (Get-Date))){Note 'Download abgeschlossen. Wartungsfenster beendet oder pausiert.';exit 0}
  if((Get-PilotEligibility -Update $u -Devices @(Get-CimInstance Win32_PnPEntity) -FirstSeen ([datetime]$state.FirstSeen[$id]) -Now (Get-Date) -Controls $controls) -ne 'Bereit für Wartungsfenster'){Note 'Installation durch geänderte Regeln zurückgestellt.';exit 0}
  $before=@(Get-CimInstance Win32_PnPSignedDriver | Select-Object DeviceName,DeviceID,DriverVersion,InfName)
  $audit=Join-Path $root ('Audit\'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.json')
  New-Item -ItemType Directory -Path (Split-Path $audit) -Force | Out-Null
  @{Time=(Get-Date).ToString('o');Title=$u.Title;Backup=$backup;Before=$before;After=@();Result='Installation noch nicht abgeschlossen'} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $audit -Encoding UTF8
  $state.Pending=$u.Title; Note "Installation: $($u.Title)"
  $installer=$session.CreateUpdateInstaller();$installer.Updates=$collection;$installer.ForceQuiet=$true;$installer.AllowSourcePrompts=$false
  if($installer.RebootRequiredBeforeInstallation){$state.Pending=$null;Note 'Windows verlangt vor der Installation einen manuellen Neustart.';exit 0}
  $installed=$installer.Install()
  @{Time=(Get-Date).ToString('o');Title=$u.Title;Backup=$backup;Before=$before;After=@(Get-CimInstance Win32_PnPSignedDriver | Select-Object DeviceName,DeviceID,DriverVersion,InfName);Result=[int]$installed.ResultCode} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $audit -Encoding UTF8
  $entry=[pscustomobject]@{Time=(Get-Date).ToString('o');Title=$u.Title;Result=[int]$installed.ResultCode;HResult=$installed.HResult;Reboot=[bool]$installed.RebootRequired;Backup=$backup}
  $state.History=@($entry)+@($state.History | Select-Object -First 99)
  # Fehler bleiben verriegelt, damit nicht täglich erneut installiert wird.
  if($installed.ResultCode -ne 2){Note "Installation fehlgeschlagen. Automatik gestoppt: $($u.Title)";exit 0}
  $state.Pending=$null;Save-State
  if($installed.RebootRequired){Note 'Treiber installiert. Bitte Windows selbst neu starten; weitere Pakete warten.';exit 0}
  $after=@(Get-CimInstance Win32_PnPEntity | Where-Object {$_.ConfigManagerErrorCode -notin @(0,22)})
  if($after.Count){$state.Pending='Gerätefehler nach Wartung';Note 'Gerätefehler nach Installation erkannt. Weitere Updates gestoppt; bitte prüfen.';exit 0}
 }
 Note 'Wartung abgeschlossen. Es wurde kein Neustart ausgelöst.'
} catch {
 if($state){Note ("Wartungsfehler: "+$_.Exception.Message)}
 throw
} finally {try{$mutex.ReleaseMutex()}catch{};$mutex.Dispose()}
}finally{if($globalHeld){$globalMutex.ReleaseMutex()};$globalMutex.Dispose()}
