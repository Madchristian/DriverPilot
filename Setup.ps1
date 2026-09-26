param([switch]$Disable,[string]$PackagePath='', [int]$WaitForPid=0)
$ErrorActionPreference='Stop'
if($Disable){Disable-ScheduledTask -TaskName 'DriverPilot Maintenance' -ErrorAction Stop | Out-Null;exit 0}
$source=if($PackagePath){[IO.Path]::GetFullPath($PackagePath)}else{$PSScriptRoot}
$target=Join-Path $env:ProgramFiles 'DriverPilot'
$data=Join-Path $env:ProgramData 'DriverPilot'
$exe=Join-Path $target 'DriverPilot.exe'
# Bestehende Installationen mit Verknüpfungen oder fremden Dateien nicht übernehmen.
function Assert-NoReparse([string]$path){
 foreach($entry in (Get-ChildItem -LiteralPath $path -Force)){
  if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Verknüpfung in Installationsdaten gefunden. Einrichtung abgebrochen.'}
  if($entry.PSIsContainer){Assert-NoReparse $entry.FullName}
 }
}
foreach($dir in @($target,$data)){
 if(Test-Path -LiteralPath $dir){
  if((Get-Item -LiteralPath $dir).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Installationspfad darf keine Verknüpfung sein.'}
  $owner=(Get-Acl -LiteralPath $dir).GetOwner([Security.Principal.SecurityIdentifier]).Value
  if($owner -notin @('S-1-5-32-544','S-1-5-18')){throw 'Vorhandener Installationspfad hat keinen vertrauenswürdigen Eigentümer. Bitte manuell prüfen.'}
 }
 New-Item -ItemType Directory -Path $dir -Force | Out-Null
 Assert-NoReparse $dir
 $acl=New-Object Security.AccessControl.DirectorySecurity
 $acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)')
 Set-Acl -LiteralPath $dir -AclObject $acl
}
if([IO.Path]::GetFullPath($source).TrimEnd('\') -ne [IO.Path]::GetFullPath($target).TrimEnd('\')){
 # Recheck every byte under elevation and copy first into an administrator-owned staging directory.
 $manifestPath=Join-Path $source 'package-manifest.json'
 if((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Paket darf keine Verknüpfung sein.'}
 if((Get-Item -LiteralPath $manifestPath).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Manifest darf keine Verknüpfung sein.'}
 $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
 if($manifest.Product -ne 'DriverPilot' -or -not ([version]$manifest.Version)){throw 'Ungültiges Paket.'}
 $entries=@($manifest.Files.PSObject.Properties)
 if($entries.Count -lt 5 -or $entries.Count -gt 1000){throw 'Ungültige Paketgröße.'}
 foreach($required in @('DriverPilot.exe','DriverPilot.dll','DriverPilot.deps.json','DriverPilot.runtimeconfig.json','Setup.ps1')){if($required -notin $entries.Name){throw "Paket unvollständig: $required"}}
 if($PackagePath -and (Test-Path -LiteralPath (Join-Path $target 'DriverPilot.dll'))){
  $current=[version][Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $target 'DriverPilot.dll')).FileVersion
  if([version]$manifest.Version -le [version]("{0}.{1}.{2}" -f $current.Major,$current.Minor,$current.Build)){throw 'Updatepaket ist nicht neuer als die installierte Version.'}
 }
 $stage=Join-Path $data ('AppStaging-'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $stage | Out-Null
 foreach($entry in $entries){
  $name=$entry.Name
  if([IO.Path]::GetFileName($name) -ne $name -or $name -match '[:/\\]' -or [IO.Path]::GetExtension($name) -notin @('.exe','.dll','.json','.ps1') -or $name -eq 'package-manifest.json'){throw 'Ungültiger Paketpfad.'}
  $inputFile=Join-Path $source $name
  if((Get-Item -LiteralPath $inputFile).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Verknüpfung im Paket.'}
  $stagedFile=Join-Path $stage $name
  Copy-Item -LiteralPath $inputFile -Destination $stagedFile
  if((Get-FileHash -LiteralPath $stagedFile -Algorithm SHA256).Hash -ne $entry.Value){throw "Prüfsummenfehler: $name"}
 }
 $binaryVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $stage 'DriverPilot.dll')).FileVersion
 if([version]$binaryVersion -ne [version]($manifest.Version+'.0')){throw 'Programmversion und Manifest stimmen nicht überein.'}
 if($WaitForPid -gt 0){Wait-Process -Id $WaitForPid -Timeout 30 -ErrorAction SilentlyContinue}
 $running=@(Get-Process DriverPilot -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $exe})
 if($running.Count){throw 'DriverPilot läuft noch. Bitte über das Infobereich-Menü beenden.'}
 # Keep the previous installation for recovery if replacement fails.
 $previous=Join-Path $data ('AppPrevious-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
 New-Item -ItemType Directory -Path $previous | Out-Null
 Get-ChildItem -LiteralPath $target -File | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $previous}
 try{Get-ChildItem -LiteralPath $stage -File | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $target -Force}}
 catch{Get-ChildItem -LiteralPath $previous -File | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $target -Force};throw}
}
if(-not(Test-Path -LiteralPath $exe)){throw 'DriverPilot.exe fehlt. Bitte Setup aus dem veröffentlichten Programmordner starten.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent().Name
$action=New-ScheduledTaskAction -Execute $exe -Argument '--maintenance' -WorkingDirectory $target
$trigger=New-ScheduledTaskTrigger -Daily -At '00:00'
$repeat=New-ScheduledTaskTrigger -Once -At '00:00' -RepetitionInterval (New-TimeSpan -Minutes 30) -RepetitionDuration (New-TimeSpan -Days 1)
$trigger.Repetition=$repeat.Repetition
$principal=New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Highest
$settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 3) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName 'DriverPilot Maintenance' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Treibersuche alle 30 Minuten; Installation nur im benutzerdefinierten Wartungsfenster bei Leerlauf. Keine Neustarts.' -Force | Out-Null
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'DriverPilot.lnk'))
$shortcut.TargetPath=$exe;$shortcut.WorkingDirectory=$target;$shortcut.Save()
$run='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $run -Force | Out-Null
New-ItemProperty -Path $run -Name 'DriverPilot' -Value ('"'+$exe+'" --tray') -PropertyType String -Force | Out-Null

@{Version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $target 'DriverPilot.dll')).FileVersion;Time=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $data 'app-install.json') -Encoding UTF8
