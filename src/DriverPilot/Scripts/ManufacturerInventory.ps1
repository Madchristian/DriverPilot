# System/board/BIOS identity comes exclusively from the caller's Snapshot.Hardware.
# Do not independently recollect it here or export disk serial numbers.
$disks=@(Get-CimInstance Win32_DiskDrive | Where-Object {$_.Size -gt 0} | ForEach-Object {[pscustomobject]@{Model=([string]$_.Model).Trim();Firmware=[string]$_.FirmwareRevision;CapacityGB=[math]::Round($_.Size/1GB)}})
$paths=@('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')
$chipset=Get-ItemProperty $paths -ErrorAction SilentlyContinue | Where-Object {$_.DisplayName -eq 'AMD Chipset Software'} | Select-Object -First 1
[pscustomobject]@{Board='';BiosVersion='';BiosDate='';Disks=$disks;ChipsetVersion=[string]$chipset.DisplayVersion} | ConvertTo-Json -Depth 5 -Compress
