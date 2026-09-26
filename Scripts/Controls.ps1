function Read-PilotControls {
 param([string]$File=(Join-Path $env:LOCALAPPDATA 'DriverPilot\controls.json'))
 $c=@{Days=@(0);StartHour=12;EndHour=17;ObservationDays=14;PauseUntil='';BlockedDevices=@();BlockedUpdates=@();BusyProcesses=@();NetworkLimitMB=2}
 if(Test-Path -LiteralPath $file){
  $o=Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
  foreach($key in @($c.Keys)){if($o.PSObject.Properties.Name -contains $key){$c[$key]=$o.$key}}
 }
 if(@($c.Days).Count -eq 0 -or @($c.Days | Where-Object {"$_" -notmatch '^[0-6]$'}).Count){throw 'Ungültige Wartungstage.'}
 foreach($key in @('StartHour','EndHour','ObservationDays','NetworkLimitMB')){if("$($c[$key])" -notmatch '^\d+$'){throw 'Ungültige Wartungsregel.'};$c[$key]=[int]$c[$key]}
 if($c.StartHour -lt 0 -or $c.EndHour -gt 24 -or $c.StartHour -ge $c.EndHour -or $c.ObservationDays -lt 14 -or $c.ObservationDays -gt 90 -or $c.NetworkLimitMB -lt 1 -or $c.NetworkLimitMB -gt 100){throw 'Wartungsregel außerhalb des erlaubten Bereichs.'}
 if($c.PauseUntil){[void][datetime]::Parse($c.PauseUntil)}
 if(@($c.BusyProcesses).Count -gt 100 -or @($c.BusyProcesses | Where-Object {$_ -notmatch '^[\p{L}\p{N}_ .-]{1,100}$'}).Count){throw 'Ungültige Prozessliste.'}
 foreach($key in @('BlockedDevices','BlockedUpdates')){if(@($c[$key]).Count -gt 1000 -or @($c[$key] | Where-Object {$_ -isnot [string] -or $_.Length -gt 512}).Count){throw 'Ungültige Sperrliste.'}}
 return $c
}
function Test-PilotWindow($Controls,[datetime]$Now){
 if($Controls.PauseUntil -and [datetime]$Controls.PauseUntil -gt $Now){return $false}
 return ([int]$Now.DayOfWeek -in $Controls.Days -and $Now.Hour -ge $Controls.StartHour -and $Now.Hour -lt $Controls.EndHour)
}
