function Get-PilotEligibility {
 param($Update, [object[]]$Devices, [datetime]$FirstSeen, [datetime]$Now, $Controls=@{ObservationDays=14;BlockedDevices=@();BlockedUpdates=@()})
 $u=$Update
 $match=@($Devices | Where-Object { $_.HardwareID -contains $u.DriverHardwareID -or $_.CompatibleID -contains $u.DriverHardwareID })
 if($u.Identity.UpdateID -in $Controls.BlockedUpdates){return 'Dieses Updatepaket ist gesperrt'}
 if(@($match | Where-Object {$_.DeviceID -in $Controls.BlockedDevices}).Count){return 'Gerät auf installierter Version gehalten'}
 if($u.DriverClass -notin @('Display','Net','MEDIA','Bluetooth','System')){return 'Geräteklasse von Automatik ausgenommen'}
 if($u.DriverClass -eq 'System' -and $u.DriverManufacturer -notmatch 'AMD|Advanced Micro Devices'){return 'Systempaket benötigt manuelle Prüfung'}
 if($u.Title -match '(?i)firmware|BIOS|\bbeta\b|preview'){return 'Firmware oder Vorabversion ausgeschlossen'}
 if($match.Count -eq 0){return 'Hardwarezuordnung nicht eindeutig bestätigt'}
 if(@($match | Where-Object {$_.ConfigManagerErrorCode -eq 22}).Count){return 'Betroffenes Gerät ist deaktiviert'}
 if($u.InstallationBehavior.CanRequestUserInput){return 'Benötigt interaktive Installation'}
 $days=[math]::Max(14,[int]$Controls.ObservationDays)
 if($FirstSeen.ToUniversalTime() -gt $Now.ToUniversalTime().AddDays(-$days)){return "$days-tägige Beobachtungszeit läuft"}
 if($u.LastDeploymentChangeTime -gt $Now.AddDays(-$days)){return "Paket wurde vor weniger als $days Tagen geändert"}
 return 'Bereit für Wartungsfenster'
}
