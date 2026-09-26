$warnings = [System.Collections.Generic.List[string]]::new()
$os=Get-CimInstance Win32_OperatingSystem
$cpu=Get-CimInstance Win32_Processor | Select-Object -First 1
$board=Get-CimInstance Win32_BaseBoard | Select-Object -First 1
$bios=Get-CimInstance Win32_BIOS | Select-Object -First 1
$ram=(Get-CimInstance Win32_PhysicalMemory | Measure-Object Capacity -Sum).Sum / 1GB
$drivers=@{}
Get-CimInstance Win32_PnPSignedDriver | ForEach-Object { if($_.DeviceID) { $drivers[$_.DeviceID]=$_ } }
$devices=@(Get-CimInstance Win32_PnPEntity | Where-Object { $_.Present -ne $false -and $_.Name } | ForEach-Object {
  $d=$drivers[$_.DeviceID]
  [pscustomobject]@{Name=$_.Name;Class=[string]$_.PNPClass;Version=[string]$d.DriverVersion;Provider=[string]$d.DriverProviderName;Date=$(if($d.DriverDate){$d.DriverDate.ToString('yyyy-MM-dd')}else{''});Code=[int]$_.ConfigManagerErrorCode;Id=$_.DeviceID}
})
$findings=[System.Collections.Generic.List[object]]::new()
foreach($d in $devices) { if($d.Code -ne 0) {
  $detail=switch($d.Code){22 {'Gerät ist deaktiviert. Falls dies beabsichtigt ist, als bekannt markieren.'};28 {'Für dieses Gerät ist kein Treiber installiert.'};10 {'Windows kann dieses Gerät nicht starten.'};43 {'Das Gerät hat einen Fehler gemeldet und wurde angehalten.'};default {"Windows meldet Gerätecode $($d.Code). Eine Ursache ist noch nicht bestimmt."}}
  $findings.Add([pscustomobject]@{Key="device:$($d.Id):$($d.Code)";Title=$d.Name;Detail=$detail;Severity=$(if($d.Code -eq 22){'Info'}else{'Warnung'});Time=(Get-Date).ToString('dd.MM.yyyy HH:mm')})
} }
# Nur ausgewählte Hardware-/Treiberanbieter. Ereignisse sind Hinweise, keine Kausalitätsbeweise.
try {
  $query="*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= 86400000] and (Provider[@Name='Display'] or Provider[@Name='nvlddmkm'] or Provider[@Name='amdwddmg'] or Provider[@Name='Microsoft-Windows-Kernel-PnP'] or Provider[@Name='Microsoft-Windows-DriverFrameworks-UserMode'] or Provider[@Name='Microsoft-Windows-WHEA-Logger'] or Provider[@Name='disk'] or Provider[@Name='stornvme'] or Provider[@Name='storahci'])]]"
  $eventErrors=@()
  $events=@(Get-WinEvent -LogName System -FilterXPath $query -MaxEvents 300 -ErrorAction SilentlyContinue -ErrorVariable eventErrors)
  $actualErrors=@($eventErrors | Where-Object {$_.FullyQualifiedErrorId -notmatch 'NoMatchingEventsFound'})
  if($actualErrors.Count){$warnings.Add('Windows-Ereignisprotokoll konnte nicht vollständig gelesen werden.')}
  # WLAN-Verbindungsabbrüche ergänzen, falls das optionale Protokoll vorhanden ist.
  $wlanLog=Get-WinEvent -ListLog 'Microsoft-Windows-WLAN-AutoConfig/Operational' -ErrorAction SilentlyContinue
  if($wlanLog -and $wlanLog.IsEnabled){
   $events+=@(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-WLAN-AutoConfig/Operational';Id=8003;StartTime=(Get-Date).AddDays(-1)} -MaxEvents 100 -ErrorAction SilentlyContinue)
  }
  $disks=@(Get-CimInstance Win32_DiskDrive)
  $enriched=@(foreach($event in $events){
   $xml=[xml]$event.ToXml();$target='';$label=''
   foreach($entry in @($xml.Event.EventData.Data)){if($entry.Name -in @('DeviceInstanceId','DeviceInstanceID','DeviceName','DeviceId','DiskNumber','DeviceNumber','AdapterName','InterfaceGuid')){$target=[string]$entry.'#text';if($target){break}}}
   if($event.ProviderName -eq 'disk' -and $event.Id -eq 158){
    $number=$null
    if($target -match '^\d+$'){$number=[int]$target}
    elseif($event.Message -match '(?:Datenträger|Disk)\s+["\x27]?(\d+)'){$number=[int]$Matches[1]}
    if($null -ne $number){$target="Disk $number";$disk=$disks | Where-Object Index -eq $number | Select-Object -First 1
     if($disk){$label="Aktuelle Zuordnung: Datenträger $number · $($disk.Model) · $([math]::Round($disk.Size/1GB)) GB. Die Nummer kann sich nach einem Neustart ändern."}else{$label="Datenträger $number ist aktuell nicht als physisches Laufwerk zugeordnet (möglicherweise virtuell oder getrennt)."}}
   }
   $device=$devices | Where-Object Id -eq $target | Select-Object -First 1
   if($device){$label="Gerät: $($device.Name)."}
   [pscustomobject]@{Event=$event;Target=$target;Label=$label;Group="$($event.ProviderName):$($event.Id):$target`:$($event.TimeCreated.ToString('yyyyMMddHH'))"}
  })
  foreach($group in ($enriched | Group-Object Group)) {
    $row=$group.Group[0];$e=$row.Event
    $message=([string]$e.Message -replace '\s+',' ').Trim()
    if($message.Length -gt 700){$message=$message.Substring(0,700)}
    $first=($group.Group.Event.TimeCreated | Sort-Object | Select-Object -First 1)
    $last=($group.Group.Event.TimeCreated | Sort-Object -Descending | Select-Object -First 1)
    $findings.Add([pscustomobject]@{Key="event:$($row.Group)";Title="$($e.ProviderName) · Ereignis $($e.Id)";Detail="$($group.Count)x zwischen $($first.ToString('HH:mm')) und $($last.ToString('HH:mm')). $($row.Label) $message";Severity=$(if($group.Count -ge 3 -or $e.Level -le 2){'Warnung'}else{'Info'});Time=$last.ToString('dd.MM.yyyy HH:mm');DeviceRef=$row.Target;Count=$group.Count;FirstSeen=$first.ToString('o');LastSeen=$last.ToString('o')})
  }
} catch {$warnings.Add("Ereignisprüfung fehlgeschlagen: $($_.Exception.Message)")}
[pscustomobject]@{Windows="$($os.Caption) · Build $($os.BuildNumber)";Cpu=$cpu.Name.Trim();Board=$board.Product;Bios=$bios.SMBIOSBIOSVersion;RamGB=[math]::Round($ram);Devices=@($devices | Sort-Object Class,Name);Findings=@($findings.ToArray());Warnings=@($warnings.ToArray())} | ConvertTo-Json -Depth 6 -Compress
