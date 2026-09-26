$board=Get-CimInstance Win32_BaseBoard | Select-Object -First 1
$bios=Get-CimInstance Win32_BIOS | Select-Object -First 1
$disks=@(Get-CimInstance Win32_DiskDrive | Where-Object {$_.Size -gt 0} | ForEach-Object {[pscustomobject]@{Model=$_.Model.Trim();Firmware=[string]$_.FirmwareRevision;CapacityGB=[math]::Round($_.Size/1GB)}})
$paths=@('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')
$chipset=Get-ItemProperty $paths -ErrorAction SilentlyContinue | Where-Object {$_.DisplayName -eq 'AMD Chipset Software'} | Select-Object -First 1
[pscustomobject]@{Board=$board.Product;BiosVersion=$bios.SMBIOSBIOSVersion;BiosDate=$bios.ReleaseDate.ToString('yyyy-MM-dd');Disks=$disks;ChipsetVersion=[string]$chipset.DisplayVersion} | ConvertTo-Json -Depth 5 -Compress
