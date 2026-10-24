$warnings = [System.Collections.Generic.List[string]]::new()
# Erfassungsstatus je Bereich: Lücken dürfen im Ferndiagnosebericht nie wie ein unauffälliger Befund aussehen.
$collection=[ordered]@{Hardware='complete';Devices='complete';SystemEvents='complete';WlanEvents='unavailable'}
function Read-HardwareCim($class) {
  try { Get-CimInstance $class -ErrorAction Stop | Select-Object -First 1 }
  catch { $warnings.Add("Hardwareabfrage unvollständig: $class"); $collection.Hardware='failed'; $null }
}
$os=Read-HardwareCim Win32_OperatingSystem
$cpu=Read-HardwareCim Win32_Processor
$board=Read-HardwareCim Win32_BaseBoard
$bios=Read-HardwareCim Win32_BIOS
$system=Read-HardwareCim Win32_ComputerSystem
$enclosure=Read-HardwareCim Win32_SystemEnclosure
$architecture=[string]$os.OSArchitecture
# Windows architecture, not the bitness of the PowerShell process.
try { $architecture=[System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() } catch { }
$biosDate=''
if($bios.ReleaseDate) { try { $biosDate=([datetime]$bios.ReleaseDate).ToString('yyyy-MM-dd') } catch { $warnings.Add('BIOS-Datum nicht lesbar.') } }
$hardware=[pscustomobject]@{
  SystemManufacturer=[string]$system.Manufacturer;SystemModel=[string]$system.Model
  BoardManufacturer=[string]$board.Manufacturer;BoardModel=[string]$board.Product;BoardRevision=[string]$board.Version
  BiosVersion=[string]$bios.SMBIOSBIOSVersion;BiosDate=$biosDate;Architecture=$architecture;WindowsBuild=[string]$os.BuildNumber
  IsPortable=[bool]($system.PCSystemType -eq 2 -or @($enclosure.ChassisTypes | Where-Object {$_ -in @(8,9,10,11,14,30,31,32)}).Count -gt 0)
}
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
  if($actualErrors.Count){$warnings.Add('Windows-Ereignisprotokoll konnte nicht vollständig gelesen werden.');$collection.SystemEvents='failed'}
  elseif($events.Count -ge 300){$collection.SystemEvents='truncated'}
  # WLAN-Verbindungsabbrüche ergänzen, falls das optionale Protokoll vorhanden ist.
  $wlanLog=Get-WinEvent -ListLog 'Microsoft-Windows-WLAN-AutoConfig/Operational' -ErrorAction SilentlyContinue
  if($wlanLog -and $wlanLog.IsEnabled){
   $wlanEvents=@(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-WLAN-AutoConfig/Operational';Id=8003;StartTime=(Get-Date).AddDays(-1)} -MaxEvents 100 -ErrorAction SilentlyContinue)
   $collection.WlanEvents=$(if($wlanEvents.Count -ge 100){'truncated'}else{'complete'});$events+=$wlanEvents
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
    $findings.Add([pscustomobject]@{Key="event:$($row.Group)";Title="$($e.ProviderName) · Ereignis $($e.Id)";Detail="$($group.Count)x zwischen $($first.ToString('HH:mm')) und $($last.ToString('HH:mm')). $($row.Label) $message";Severity=$(if($group.Count -ge 3 -or $e.Level -le 2){'Warnung'}else{'Info'});Time=$last.ToString('dd.MM.yyyy HH:mm');DeviceRef=$row.Target;Count=$group.Count;FirstSeen=$first.ToString('o');LastSeen=$last.ToString('o');Source=[string]$e.ProviderName;EventId=[int]$e.Id;Level=[int](($group.Group.Event.Level | Measure-Object -Minimum).Minimum)})
  }
} catch {$warnings.Add("Ereignisprüfung fehlgeschlagen: $($_.Exception.Message)");$collection.SystemEvents='failed'}
# Detailangaben (Bericht 1.1): Rohwerte ohne Seriennummern. Jeder Bereich einzeln; ein Fehler macht nur ihn null.
# Listen kommen mit vorangestelltem Komma zurück, damit ein einzelnes Element nicht zum Einzelobjekt entpackt wird.
function Read-DetailSection($name,[scriptblock]$block) {
  try { & $block } catch { $warnings.Add("Detailabfrage unvollständig: $name"); $null }
}
function Read-Registry($path,$name) {
  try { (Get-ItemProperty -LiteralPath $path -Name $name -ErrorAction Stop).$name } catch { $null }
}
function Invoke-NvidiaSmi {
  # Nur die vom Treiber installierten Orte; beide sind ohne Administratorrechte nicht beschreibbar.
  $smi=@("$env:SystemRoot\System32\nvidia-smi.exe","$env:ProgramFiles\NVIDIA Corporation\NVSMI\nvidia-smi.exe") | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
  if(-not $smi){return @()}
  $psi=[System.Diagnostics.ProcessStartInfo]::new($smi,'--query-gpu=name,temperature.gpu,fan.speed,clocks.gr,clocks.max.gr,clocks.mem,clocks.max.mem,power.draw,power.limit,power.default_limit,power.max_limit,pcie.link.gen.current,pcie.link.gen.max,pcie.link.width.current,pcie.link.width.max --format=csv,noheader,nounits')
  $psi.UseShellExecute=$false;$psi.RedirectStandardOutput=$true;$psi.CreateNoWindow=$true
  $process=[System.Diagnostics.Process]::Start($psi);$output=$process.StandardOutput.ReadToEndAsync()
  if(-not $process.WaitForExit(10000)){try{$process.Kill()}catch{};$warnings.Add('nvidia-smi hat nicht rechtzeitig geantwortet.');return @()}
  if($process.ExitCode -ne 0){return @()}
  @($output.Result -split "`r?`n" | Where-Object {$_.Trim()})
}
$details=[pscustomobject]@{
  Cpu=Read-DetailSection 'CPU' { [pscustomobject]@{Cores=[int]$cpu.NumberOfCores;Threads=[int]$cpu.NumberOfLogicalProcessors;MaxClockMhz=[int]$cpu.MaxClockSpeed} }
  Memory=Read-DetailSection 'Arbeitsspeicher' {
    $slots=(Get-CimInstance Win32_PhysicalMemoryArray -ErrorAction Stop | Where-Object Use -eq 3 | Measure-Object MemoryDevices -Sum).Sum
    [pscustomobject]@{SlotsTotal=[int]$slots;Modules=@(Get-CimInstance Win32_PhysicalMemory -ErrorAction Stop | Where-Object {$_} | ForEach-Object {
      [pscustomobject]@{Slot=[string]$_.DeviceLocator;CapacityMb=[long]($_.Capacity/1MB);SmbiosType=[int]$_.SMBIOSMemoryType;RatedMts=[int]$_.Speed;ConfiguredMts=[int]$_.ConfiguredClockSpeed;Manufacturer=[string]$_.Manufacturer;PartNumber=[string]$_.PartNumber}
    })}
  }
  Gpus=Read-DetailSection 'Grafikkarten' {
    # AdapterRAM endet bei 4 GB; der Treiber legt den echten Wert als qwMemorySize in seinem Klassenschlüssel ab.
    $memory=@{}
    Get-ChildItem -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}' -ErrorAction SilentlyContinue | ForEach-Object {
      $size=Read-Registry $_.PSPath 'HardwareInformation.qwMemorySize';$desc=Read-Registry $_.PSPath 'DriverDesc'
      try{if($size -is [byte[]]){$size=[BitConverter]::ToInt64($size,0)};if($desc -and $size){$memory[[string]$desc]=[long]$size}}catch{}
    }
    ,@(Get-CimInstance Win32_VideoController -ErrorAction Stop | Where-Object {$_} | ForEach-Object {
      $vram=$(if($memory.ContainsKey([string]$_.Name)){$memory[[string]$_.Name]}else{[long]$_.AdapterRAM})
      [pscustomobject]@{Name=[string]$_.Name;PnpId=[string]$_.PNPDeviceID;DriverVersion=[string]$_.DriverVersion;VramBytes=$vram}
    })
  }
  NvidiaSmi=Read-DetailSection 'nvidia-smi' { ,@(Invoke-NvidiaSmi) }
  Storage=Read-DetailSection 'Laufwerke' {
    $systemDisk=$null
    try{$systemDisk=[string](Get-Partition -DriveLetter $env:SystemDrive.TrimEnd(':') -ErrorAction Stop | Select-Object -First 1).DiskNumber}catch{}
    $volume=Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$env:SystemDrive'" -ErrorAction Stop
    $pagefile=(Get-CimInstance Win32_PageFileUsage -ErrorAction SilentlyContinue | Measure-Object AllocatedBaseSize -Sum).Sum
    [pscustomobject]@{SystemSizeGb=[long]($volume.Size/1GB);SystemFreeGb=[long]($volume.FreeSpace/1GB);PagefileMb=$(if($pagefile){[long]$pagefile}else{$null});Disks=@(Get-PhysicalDisk -ErrorAction Stop | Where-Object {$_} | ForEach-Object {
      [pscustomobject]@{Model=[string]$_.Model;MediaType=[string]$_.MediaType;BusType=[string]$_.BusType;SizeGb=[long]($_.Size/1GB);Health=[string]$_.HealthStatus;Firmware=[string]$_.FirmwareVersion;SystemDisk=$(if($systemDisk){[string]$_.DeviceId -eq $systemDisk}else{$null})}
    })}
  }
  Platform=Read-DetailSection 'Plattform' {
    # Nur die GUID des Plans: Den Namen kann der Nutzer frei vergeben.
    $plan=$null;try{$plan=[string](Get-CimInstance -Namespace root\cimv2\power -ClassName Win32_PowerPlan -Filter 'IsActive=TRUE' -ErrorAction Stop | Select-Object -First 1).InstanceID}catch{}
    [pscustomobject]@{FirmwareType=[string]$env:firmware_type;SecureBoot=Read-Registry 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State' 'UEFISecureBootEnabled';PowerPlanId=$plan
      HwSchMode=Read-Registry 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' 'HwSchMode';UptimeHours=$(if($os.LastBootUpTime){((Get-Date)-$os.LastBootUpTime).TotalHours}else{$null})}
  }
}
[pscustomobject]@{Windows="$($os.Caption) · Build $($os.BuildNumber)";Cpu=([string]$cpu.Name).Trim();Board=[string]$board.Product;Bios=[string]$bios.SMBIOSBIOSVersion;RamGB=[math]::Round($ram);Devices=@($devices | Sort-Object Class,Name);Findings=@($findings.ToArray());Warnings=@($warnings.ToArray());Hardware=$hardware;Collection=[pscustomobject]$collection;Details=$details} | ConvertTo-Json -Depth 6 -Compress
