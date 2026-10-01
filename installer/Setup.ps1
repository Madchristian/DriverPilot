param([switch]$Disable,[string]$PackagePath='', [int]$WaitForPid=0,[string]$ApprovedManifestHash='')
$ErrorActionPreference='Stop'
$env:PSModulePath=[IO.Path]::Combine([Environment]::GetFolderPath('System'),'WindowsPowerShell\v1.0\Modules')
if($Disable){Disable-ScheduledTask -TaskName 'DriverPilot Maintenance' -ErrorAction Stop | Out-Null;exit 0}
$source=if($PackagePath){[IO.Path]::GetFullPath($PackagePath)}else{$PSScriptRoot}
$target=Join-Path $env:ProgramFiles 'DriverPilot'
$data=Join-Path $env:ProgramData 'DriverPilot'
$exe=Join-Path $target 'DriverPilot.exe'
function Assert-PathAncestors([string]$Path){
 for($p=[IO.Path]::GetFullPath($Path);$p;$p=[IO.Path]::GetDirectoryName($p)){
  if(Test-Path -LiteralPath $p){if((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse point in trusted path.'}}
 }
}
function Assert-TrustedAcl($Acl,[switch]$AllowChildCreation){
 $trusted=@('S-1-5-18','S-1-5-32-544','S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
 if($Acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted){throw 'Untrusted installation/staging owner.'}
 $write=[Security.AccessControl.FileSystemRights]'Write,Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership'
 if($AllowChildCreation){$write=[Security.AccessControl.FileSystemRights]'Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership'}
 foreach($rule in $Acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
  if($rule.AccessControlType -eq 'Allow' -and -not ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -and (([int64]$rule.FileSystemRights -band ([int64]$write -bor 0x50000000)) -ne 0) -and $rule.IdentityReference.Value -notin $trusted){throw 'Writable installation/staging ACL.'}
 }
}
function Assert-ProtectedTree([string]$Path){
 Assert-PathAncestors $Path
 Assert-TrustedAcl (Get-Acl -LiteralPath $Path)
 foreach($entry in Get-ChildItem -LiteralPath $Path -Force){
  Assert-PathAncestors $entry.FullName
  Assert-TrustedAcl (Get-Acl -LiteralPath $entry.FullName)
  if($entry.PSIsContainer){Assert-ProtectedTree $entry.FullName}
 }
}
function New-ProtectedDirectory([string]$Path){
 Assert-PathAncestors $Path
 if(Test-Path -LiteralPath $Path){throw 'Protected destination already exists.'}
 $acl=New-Object Security.AccessControl.DirectorySecurity
 $acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)')
 [void][IO.Directory]::CreateDirectory($Path,$acl)
 Assert-ProtectedTree $Path
}
# Caller has authenticated/protected the complete Next tree and holds the update lock.
function Publish-PackageDirectory([string]$Target,[string]$Next,[string]$Previous){
 # Direct callers can also inherit the installation as their native cwd. Set-Location alone does not release it.
 $stableDirectory=[Environment]::GetFolderPath([Environment+SpecialFolder]::System)
 Set-Location -LiteralPath $stableDirectory
 [Environment]::CurrentDirectory=$stableDirectory
 $hadPrevious=[IO.Directory]::Exists($Target)
 if($hadPrevious){[IO.Directory]::Move($Target,$Previous)}
 try{[IO.Directory]::Move($Next,$Target)}
 catch{if($hadPrevious){[IO.Directory]::Move($Previous,$Target)};throw}
}
function Enter-UpdateLock([string]$Name){
 $guard=[Threading.Mutex]::new($false,$Name)
 try{if(-not $guard.WaitOne(0)){throw 'Another app update is already running.'};return $guard}
 catch{$guard.Dispose();throw}
}
function Get-InstallationOwner($Metadata,[string]$Sid,[string]$ProfilePath){
 if($Metadata -and ($Metadata.PSObject.Properties.Name -contains 'OwnerSid' -or $Metadata.PSObject.Properties.Name -contains 'OwnerProfilePath')){
  if($Metadata.OwnerSid -ne $Sid -or $Metadata.OwnerProfilePath -ne $ProfilePath){throw 'DriverPilot gehoert einem anderen Benutzer oder die Besitzerzuordnung ist unvollstaendig. Einrichtung als urspruenglicher Benutzer starten.'}
 }
 return @{OwnerSid=$Sid;OwnerProfilePath=$ProfilePath}
}
function Assert-NoPendingUninstall([string]$SharedRoot){
 if(Test-Path -LiteralPath (Join-Path $SharedRoot 'UninstallPending')){throw 'Eine DriverPilot-Deinstallation ist noch nicht abgeschlossen. Zuerst die Deinstallation erneut ausfuehren; keine neue Version darueber installieren.'}
}
function Assert-SetupRuntimeIdle {
 # A live starting/suspended process may have no readable image path yet.
 # Match by name across sessions, conservatively including portable instances;
 # a failed query is unknown, never proof that it is safe to replace the app.
 $running=@(Get-CimInstance Win32_Process -Filter "Name='DriverPilot.exe'" -ErrorAction Stop)
 if($running.Count){throw 'DriverPilot laeuft noch. Bitte alle Instanzen beenden.'}
}
# An update can ONLY be orchestrated by the protected, already installed updater.
# Bootstrap is supplied by an independently authenticated installer, never by an update package.
if($PackagePath){
 if([IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') -ne [IO.Path]::GetFullPath($target).TrimEnd('\')){throw 'Portable update elevation is not supported; use the verified signed installer.'}
 Assert-ProtectedTree $target
 if($ApprovedManifestHash -notmatch '\A[0-9A-Fa-f]{64}\z'){throw 'Missing explicit manifest approval.'}
}
Assert-PathAncestors $PSScriptRoot
Assert-TrustedAcl (Get-Acl -LiteralPath $env:ProgramFiles)
Add-Type -Path (Join-Path $PSScriptRoot 'PackageTrust.cs')
$certificate=Join-Path $PSScriptRoot 'DriverPilot.cer'
$updateLock=Enter-UpdateLock 'Global\DriverPilot.AuthenticatedAppUpdate'
try{
Assert-NoPendingUninstall $data
$current='0.0.0'
if(Test-Path -LiteralPath $target){
 Assert-ProtectedTree $target
 $current=([version][Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $target 'DriverPilot.dll')).FileVersion).ToString(3)
}
$manifest=[DriverPilot.Security.PackageTrust]::Verify($source,$certificate,$current,[bool]$PackagePath)
if($PackagePath){[DriverPilot.Security.PackageTrust]::AssertApproval($manifest,$ApprovedManifestHash)}
else{$ApprovedManifestHash=[DriverPilot.Security.PackageTrust]::ManifestHash($manifest)}
# Reconfiguring the already installed app must not try replacing its running executable.
$replace=[IO.Path]::GetFullPath($source).TrimEnd('\') -ne [IO.Path]::GetFullPath($target).TrimEnd('\')
# Only after authenticating the source may protected staging be created. Existing writable trees are rejected, not repaired.
Assert-PathAncestors $data
Assert-TrustedAcl (Get-Acl -LiteralPath $env:ProgramData) -AllowChildCreation
if(Test-Path -LiteralPath $data){Assert-ProtectedTree $data}else{New-ProtectedDirectory $data}
$metadataPath=Join-Path $data 'app-install.json'
$metadata=if(Test-Path -LiteralPath $metadataPath){Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json}else{$null}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$owner=Get-InstallationOwner $metadata $identity.User.Value ([Environment]::GetFolderPath('UserProfile'))
# Legacy installations have no owner receipt: an existing task must belong to this account.
$existingTask=@(Get-ScheduledTask -ErrorAction Stop | Where-Object {$_.TaskPath -eq '\' -and $_.TaskName -eq 'DriverPilot Maintenance'})
if($existingTask.Count){
 $taskUser=$existingTask[0].Principal.UserId
 $taskSid=if($taskUser -match '^S-1-'){([Security.Principal.SecurityIdentifier]$taskUser).Value}else{([Security.Principal.NTAccount]$taskUser).Translate([Security.Principal.SecurityIdentifier]).Value}
 if($taskSid -ne $owner.OwnerSid){throw 'Wartungsauftrag gehoert einem anderen Benutzer. Einrichtung abgebrochen.'}
}
if($replace){
$stage=Join-Path $data ('AppStaging-'+[guid]::NewGuid().ToString('N'))
$next=Join-Path $env:ProgramFiles ('DriverPilot.next-'+[guid]::NewGuid().ToString('N'))
$previous=Join-Path $env:ProgramFiles ('DriverPilot.previous-'+[guid]::NewGuid().ToString('N'))
$stageCreated=$false;$nextCreated=$false
try{
 New-ProtectedDirectory $stage
 $stageCreated=$true
 [DriverPilot.Security.PackageTrust]::CopyVerified($source,$stage,$manifest)
 $staged=[DriverPilot.Security.PackageTrust]::Verify($stage,$certificate,$current,[bool]$PackagePath)
 [DriverPilot.Security.PackageTrust]::AssertApproval($staged,$ApprovedManifestHash)
 Assert-ProtectedTree $stage
 # Prepare a verified sibling on the destination volume; never overlay stale executables.
 Assert-TrustedAcl (Get-Acl -LiteralPath $env:ProgramFiles)
 New-ProtectedDirectory $next
 $nextCreated=$true
 [DriverPilot.Security.PackageTrust]::CopyVerified($stage,$next,$staged)
 $ready=[DriverPilot.Security.PackageTrust]::Verify($next,$certificate,$current,[bool]$PackagePath)
 [DriverPilot.Security.PackageTrust]::AssertApproval($ready,$ApprovedManifestHash)
 Assert-ProtectedTree $next
 if($WaitForPid -gt 0){Wait-Process -Id $WaitForPid -Timeout 30 -ErrorAction SilentlyContinue}
 Assert-SetupRuntimeIdle
 if(Test-Path -LiteralPath $target){Assert-ProtectedTree $target}
 Publish-PackageDirectory $target $next $previous
}finally{
 if($stageCreated -and (Test-Path -LiteralPath $stage)){Assert-ProtectedTree $stage;Remove-Item -LiteralPath $stage -Recurse -Force}
 if($nextCreated -and (Test-Path -LiteralPath $next)){Assert-ProtectedTree $next;Remove-Item -LiteralPath $next -Recurse -Force}
}
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

@{Version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $target 'DriverPilot.dll')).FileVersion;Time=(Get-Date).ToString('o');OwnerSid=$owner.OwnerSid;OwnerProfilePath=$owner.OwnerProfilePath} | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding UTF8
}finally{$updateLock.ReleaseMutex();$updateLock.Dispose()}
