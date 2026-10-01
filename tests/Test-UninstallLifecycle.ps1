# Destructive integration test: disposable, elevated GitHub-hosted Windows runners ONLY.
# No certificate import/export, driver operation, interactive UAC or developer-PC cleanup.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$InstallerPath)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}

# Keep every write (including logs, transcripts and Add-Type compilation) AFTER these gates.
Assert ($env:OS -eq 'Windows_NT' -and $env:GITHUB_ACTIONS -eq 'true' -and $env:RUNNER_ENVIRONMENT -eq 'github-hosted' -and $env:RUNNER_OS -eq 'Windows' -and -not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) 'Uninstall lifecycle requires a disposable github-hosted Windows runner; no writes performed.'
Assert ($PSVersionTable.PSEdition -eq 'Desktop' -and $PSVersionTable.PSVersion.Major -eq 5 -and [Environment]::Is64BitProcess) 'Use elevated 64-bit Windows PowerShell 5.1, not pwsh.'
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=[Security.Principal.WindowsPrincipal]::new($identity)
Assert ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) 'Elevated runner required; no interactive UAC permitted.'
$target=Join-Path $env:ProgramFiles 'DriverPilot'
$uninstallRoot=Join-Path $env:ProgramFiles 'DriverPilot.Uninstall'
$uninstaller=Join-Path $uninstallRoot 'unins000.exe'
$data=Join-Path $env:ProgramData 'DriverPilot'
$userData=Join-Path $env:LOCALAPPDATA 'DriverPilot'
$shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'DriverPilot.lnk'
$commonShortcut=Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'DriverPilot.lnk'
$runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$registrationRoots=@('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall','HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall','HKCU:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall')
function Get-Registration {
 foreach($registryRoot in $registrationRoots){
  if(Test-Path -LiteralPath $registryRoot){
   foreach($key in Get-ChildItem -LiteralPath $registryRoot){
    $entry=Get-ItemProperty -LiteralPath $key.PSPath
    if($key.PSChildName -like 'DriverPilot*' -or ($entry.PSObject.Properties['DisplayName'] -and $entry.DisplayName -like 'DriverPilot*')){$entry}
   }
  }
 }
}
function Get-Autorun {
 if(Test-Path -LiteralPath $runKey){(Get-ItemProperty -LiteralPath $runKey).PSObject.Properties | Where-Object {$_.Name -eq 'DriverPilot'}}
}
foreach($path in @($target,$uninstallRoot,$data,$userData,$shortcut,$commonShortcut)){
 Assert (-not(Test-Path -LiteralPath $path)) "Refusing preexisting installation/user data/shortcut: $path"
}
Assert (@(Get-ScheduledTask -TaskName 'DriverPilot Maintenance' -ErrorAction SilentlyContinue).Count -eq 0) 'Refusing preexisting maintenance task'
Assert (@(Get-Process DriverPilot -ErrorAction SilentlyContinue).Count -eq 0) 'Refusing preexisting DriverPilot process'
Assert (@(Get-ChildItem -LiteralPath $env:ProgramFiles -Directory -Filter 'DriverPilot.*-*').Count -eq 0) 'Refusing previous updater state'
Assert (@(Get-Registration).Count -eq 0) 'Refusing preexisting Apps registration'
Assert (@(Get-Autorun).Count -eq 0) 'Refusing preexisting autorun'
Assert (Test-Path -LiteralPath $InstallerPath -PathType Leaf) 'InstallerPath must identify an existing real Inno installer'
$InstallerPath=(Resolve-Path -LiteralPath $InstallerPath).ProviderPath
Assert ([IO.Path]::GetExtension($InstallerPath) -eq '.exe') 'InstallerPath must be an executable'

# Only public store names/thumbprints are read; never private key material or credentials.
function Get-TrustSnapshot {
 foreach($location in @('CurrentUser','LocalMachine')){
  foreach($store in Get-ChildItem -LiteralPath ('Cert:\'+$location)){
   foreach($cert in Get-ChildItem -LiteralPath ('Cert:\'+$location+'\'+$store.PSChildName)){
    '{0}\{1}\{2}' -f $location,$store.PSChildName,$cert.Thumbprint
   }
  }
 }
}
$trustBefore=@(Get-TrustSnapshot | Sort-Object)
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'helpers/UninstallLifecycleHelpers.ps1')
# Reuse the production ACL contract without executing Setup's installation body.
$tokens=$null;$errors=$null
$setupAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'installer/Setup.ps1'),[ref]$tokens,[ref]$errors)
Assert (-not $errors) 'Setup parse failed'
foreach($f in $setupAst.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -in @('Assert-PathAncestors','Assert-TrustedAcl','Assert-ProtectedTree')},$false)){Invoke-Expression $f.Extent.Text}
Add-Type -Path (Join-Path $root 'src/DriverPilot/Security/PackageTrust.cs')
$evidence=Join-Path $root ('artifacts/uninstall-tests/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$trustBefore | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'trust-before.json') -Encoding UTF8
$results=[Collections.Generic.List[object]]::new()
$stateHashes=[ordered]@{}
$maintenanceLock=$null;$maintenanceHeld=$false;$heldApp=$null;$failure=$null;$touchedInstallation=$false
$utf8=[Text.UTF8Encoding]::new($false)
function Record([string]$Name,[string]$Detail){
 $results.Add([pscustomobject]@{Case=$Name;Status='PASS';Detail=$Detail})
 Write-Output "PASS $Name - $Detail"
}
function Invoke-Inno([string]$Exe,[string]$Label,[string[]]$Extra=@()){
 $log=Join-Path $evidence ($Label+'.log')
 $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+$log+'"'))+$Extra
 $p=Start-Process -FilePath $Exe -ArgumentList $arguments -WorkingDirectory $env:SystemRoot -PassThru
 $null=$p.Handle
 try{
  if(-not $p.WaitForExit(180000)){
   & taskkill.exe /PID $p.Id /T /F | Out-Null
   throw "$Label timed out; disposable runner must be discarded"
  }
  $p.WaitForExit()
  Assert ($null -ne $p.ExitCode) "$Label returned no exit code"
  Assert (Test-Path -LiteralPath $log -PathType Leaf) "$Label produced no Inno log"
  return [int]$p.ExitCode
 }finally{$p.Dispose()}
}
function Disable-Maintenance {
 Get-ScheduledTask -TaskName 'DriverPilot Maintenance' -ErrorAction SilentlyContinue | Disable-ScheduledTask | Out-Null
}
function Enter-Maintenance {
 if(-not $script:maintenanceLock){$script:maintenanceLock=[Threading.Mutex]::new($false,'Local\DriverPilot.Maintenance')}
 $script:maintenanceHeld=$script:maintenanceLock.WaitOne(0)
 Assert $script:maintenanceHeld 'Maintenance mutex already held'
}
function Exit-Maintenance {
 if($script:maintenanceHeld){$script:maintenanceLock.ReleaseMutex();$script:maintenanceHeld=$false}
}
function Check-State([string]$Label){
 foreach($path in $stateHashes.Keys){
  Assert (Test-Path -LiteralPath $path -PathType Leaf) "$Label removed retained data: $path"
  Assert ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $stateHashes[$path]) "$Label changed retained bytes: $path"
 }
 Record $Label 'All seeded preferences, controls, logs/history and backup bytes unchanged'
}
function Check-Installed {
 Assert (Test-Path -LiteralPath (Join-Path $target 'DriverPilot.exe') -PathType Leaf) 'Installed application missing'
 Assert (Test-Path -LiteralPath $uninstaller -PathType Leaf) 'Real Inno uninstaller missing'
 $registrations=@(Get-Registration)
 Assert ($registrations.Count -eq 1) 'Expected exactly one Apps & Features registration'
 $entry=$registrations[0]
 Assert ($entry.DisplayName -eq 'DriverPilot') 'Unexpected Apps display name'
 Assert (-not [string]::IsNullOrWhiteSpace($entry.DisplayVersion)) 'Apps version missing'
 Assert ($entry.UninstallString -like ('*'+$uninstaller+'*')) 'Apps uninstall command does not target the real Inno uninstaller'
 $task=Get-ScheduledTask -TaskName 'DriverPilot Maintenance' -ErrorAction Stop
 Assert ($task.State -eq 'Disabled') 'Maintenance must stay disabled'
 Assert (@($task.Actions).Count -eq 1 -and $task.Actions[0].Execute -eq (Join-Path $target 'DriverPilot.exe') -and $task.Actions[0].Arguments -eq '--maintenance') 'Unexpected maintenance action'
 Assert (Test-Path -LiteralPath $shortcut -PathType Leaf) 'Start menu shortcut missing'
 Assert (@(Get-Autorun).Count -eq 1) 'Autorun missing'
 Assert-ProtectedTree $target
 Assert-ProtectedTree $uninstallRoot
 $manifest=[DriverPilot.Security.PackageTrust]::Verify($target,(Join-Path $root 'assets/certificates/DriverPilot.cer'),'0.0.0',$false)
 Assert ((Get-ChildItem -LiteralPath $target -Force).Count -eq ($manifest.Files.Count+2)) 'Unexpected installed inventory entry'
 foreach($file in $manifest.Files.GetEnumerator()){
  Assert ((Get-FileHash -LiteralPath (Join-Path $target $file.Key) -Algorithm SHA256).Hash -eq $file.Value) "Installed package bytes mismatch: $($file.Key)"
 }
 $owner=Get-Content -LiteralPath (Join-Path $data 'app-install.json') -Raw | ConvertFrom-Json
 Assert ($owner.OwnerSid -eq $identity.User.Value -and $owner.OwnerProfilePath -eq [Environment]::GetFolderPath('UserProfile')) 'Installed owner receipt mismatch'
}
function Install([string]$Label){
 Enter-Maintenance
 try{
  $script:touchedInstallation=$true
  $code=Invoke-Inno $InstallerPath $Label
  Disable-Maintenance
  Assert ($code -eq 0) "$Label failed with exit code $code"
  Check-Installed
 }finally{Disable-Maintenance;Exit-Maintenance}
 Record $Label 'Real installer, registered Apps entry, payload hashes and disabled maintenance verified'
}
function Check-Uninstalled {
 # Inno can finish deleting its own temporary worker just after the parent exits.
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 while((Test-Path -LiteralPath $uninstallRoot) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 250}
 foreach($path in @($target,$uninstallRoot,$shortcut,$commonShortcut)){
  Assert (-not(Test-Path -LiteralPath $path)) "Uninstall residue: $path"
 }
 Assert (@(Get-Registration).Count -eq 0) 'Apps registration survived uninstall'
 $receipt=Get-Content -LiteralPath (Join-Path $data 'app-install.json') -Raw | ConvertFrom-Json
 Assert ($receipt.OwnerSid -eq $identity.User.Value) 'Retained data ownership receipt missing or changed'
 Assert (@(Get-Autorun).Count -eq 0) 'Autorun survived uninstall'
 Assert (@(Get-ScheduledTask -TaskName 'DriverPilot Maintenance' -ErrorAction SilentlyContinue).Count -eq 0) 'Maintenance task survived uninstall'
 Assert (@(Get-ChildItem -LiteralPath $env:ProgramFiles -Directory -Filter 'DriverPilot.*-*').Count -eq 0) 'Updater/recovery directories survived uninstall'
}
function Seed([string]$Path,[string]$Text){
 New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
 [IO.File]::WriteAllText($Path,$Text,$utf8)
 $stateHashes[$Path]=(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}
try{
 # Seed paused controls before the first task can exist. Mutex also covers registration.
 Seed (Join-Path $userData 'preferences.json') '{"Monitor":false,"Notify":false,"Ignored":["ci-preserve"],"Notified":[],"SeenEvents":[],"ManufacturerNotified":[]}'
 Seed (Join-Path $userData 'controls.json') ' {"Days":[0],"StartHour":12,"EndHour":17,"ObservationDays":30,"PauseUntil":"2099-01-01T00:00:00","BlockedDevices":["CI-device"],"BlockedUpdates":["CI-package"],"BusyProcesses":["ci-game"],"NetworkLimitMB":2}'
 Install 'first-install'
 foreach($name in @('chat.json','history.json','ai-actions.json','snapshot.json','observations.json','updates.json','manufacturers.json')){
  Seed (Join-Path $userData $name) ('{"fixture":"'+$name+'","note":"preserve exact CI bytes"}')
 }
 foreach($name in @('maintenance.json','maintenance.tmp','Audit/ci-lifecycle.json','Backups/ci-preserved/nested/saved.inf')){
  Seed (Join-Path $data $name) ('CI preservation fixture, not a driver: '+$name)
 }
 $binary=Join-Path $data 'Backups/ci-preserved/nested/saved.bin'
 [IO.File]::WriteAllBytes($binary,[byte[]](0,1,2,127,255))
 $stateHashes[$binary]=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
 $stateHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'initial-state.json') -Encoding UTF8
 Install 'same-installer-reinstall'
 Check-State 'reinstall-preserves-data'

 # Both failures must occur BEFORE Inno overwrites the recovery helper or Apps entry.
 $helperHashes=[ordered]@{}
 foreach($file in Get-ChildItem -LiteralPath $uninstallRoot -File){$helperHashes[$file.Name]=(Get-FileHash -LiteralPath $file.FullName).Hash}
 $registrationBefore=@(Get-Registration)[0] | Select-Object DisplayName,DisplayVersion,UninstallString,QuietUninstallString | ConvertTo-Json -Compress
 $pending=Join-Path $data 'UninstallPending'
 [IO.File]::WriteAllText($pending,'CI pending marker: deliberately not a valid receipt',$utf8)
 try{
  $code=Invoke-Inno $InstallerPath 'pending-uninstall-refuses-installer'
  Assert ($code -ne 0) 'Installer falsely succeeded while removal was pending'
 }finally{[IO.File]::Delete($pending)}
 foreach($name in $helperHashes.Keys){Assert ((Get-FileHash -LiteralPath (Join-Path $uninstallRoot $name)).Hash -eq $helperHashes[$name]) 'Installer changed recovery helper before pending refusal'}
 Assert (((@(Get-Registration)[0] | Select-Object DisplayName,DisplayVersion,UninstallString,QuietUninstallString | ConvertTo-Json -Compress)) -ceq $registrationBefore) 'Pending refusal changed Apps registration'
 Check-State 'pending-refusal-preserves-data'
 Record 'pending-installer-refusal' 'Nonzero installer exit; existing uninstaller and registration byte/field checks unchanged'

 $ownerPath=Join-Path $data 'app-install.json';$ownerBytes=[IO.File]::ReadAllBytes($ownerPath)
 $wrongOwner=[Text.Encoding]::UTF8.GetString($ownerBytes) | ConvertFrom-Json
 $wrongOwner.OwnerSid='S-1-5-21-1-2-3-9999'
 try{
  $wrongOwner | ConvertTo-Json | Set-Content -LiteralPath $ownerPath -Encoding UTF8
  $code=Invoke-Inno $InstallerPath 'backend-error-refuses-installer'
  Assert ($code -ne 0) 'Installer swallowed Setup backend failure'
 }finally{[IO.File]::WriteAllBytes($ownerPath,$ownerBytes)}
 foreach($name in $helperHashes.Keys){Assert ((Get-FileHash -LiteralPath (Join-Path $uninstallRoot $name)).Hash -eq $helperHashes[$name]) 'Backend failure changed existing uninstall engine'}
 Check-Installed
 Check-State 'backend-refusal-preserves-data'
 Record 'backend-installer-refusal' 'Real Setup owner failure produces failing Inno exit without replacing uninstall engine'

 Enter-Maintenance
 try{
  $code=Invoke-Inno $uninstaller 'maintenance-refusal'
  Assert ($code -ne 0) 'Uninstaller accepted an active maintenance mutex'
  Check-Installed
  Check-State 'maintenance-preserves-data'
  Record 'maintenance-refusal' 'Held real maintenance mutex rejected without removing installed state'
 }finally{Exit-Maintenance}

 # Normal productive GUI startup, including its read-only inventory scan. No special
 # CLI switch, suspend/kill, helper invocation, or production test bypass is allowed.
 # These JSON fixtures must be valid for the actual GUI readers.
 Seed (Join-Path $userData 'chat.json') '"CI conversation"'
 foreach($name in @('history.json','observations.json','ai-actions.json','updates.json','manufacturers.json')){Seed (Join-Path $userData $name) '[]'}
 Seed (Join-Path $userData 'snapshot.json') '{}'
 Initialize-LifecycleAutomation
 Enter-Maintenance
 try{
  $heldApp=Start-Process -FilePath (Join-Path $target 'DriverPilot.exe') -WorkingDirectory $env:SystemRoot -PassThru
  $null=$heldApp.Handle
  Wait-LifecycleGuiReady $heldApp
  # Startup legitimately writes scan output and SeenEvents. Establish the refusal
  # baseline only AFTER that scan; do not silently refresh controls/backup hashes.
  foreach($name in @('preferences.json','history.json','snapshot.json','observations.json')){
   $path=Join-Path $userData $name
   $stateHashes[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
  }
  $stateHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'gui-ready-state.json') -Encoding UTF8
  Check-State 'normal-gui-ready'
 }finally{Exit-Maintenance}
 Assert (-not $maintenanceHeld) 'GUI refusal must not be masked by maintenance mutex'
 $code=Invoke-Inno $uninstaller 'running-gui-refusal'
 Assert ($code -ne 0) 'Uninstaller accepted a running application'
 Assert (-not $heldApp.HasExited) 'Uninstaller forcibly terminated the running application'
 Check-Installed
 Check-State 'running-gui-preserves-data'
 Record 'running-gui-refusal' 'Normally started installed GUI refused; installed state and post-scan bytes preserved'
 Enter-Maintenance
 try{Invoke-LifecycleTrayQuit $heldApp}finally{Exit-Maintenance}
 Record 'graceful-tray-quit' "Productive Beenden menu invoked for PID $($heldApp.Id); exit code zero, no forced termination"
 $heldApp.Dispose();$heldApp=$null
 Assert (@(Get-Process DriverPilot -ErrorAction SilentlyContinue).Count -eq 0) 'DriverPilot process survived graceful Quit'

 # No selection flags: silent default MUST retain all user categories.
 # Retry the SAME registered uninstaller immediately after Quit, not Uninstall.ps1.
 $code=Invoke-Inno $uninstaller 'retry-after-tray-quit-default-retention'
 Assert ($code -eq 0) "Default uninstall failed with exit code $code"
 Check-Uninstalled
 Check-State 'default-retains-all-data'
 Record 'default-uninstall' 'App, Apps registration, task, autorun, shortcut and recovery state removed'

 # Reseed all categories for EVERY case, including .tmp siblings and nested directories.
 # Never infer a retained category from an empty/missing fixture left by a prior case.
 foreach($choice in Get-UninstallChoices){
  $stateHashes=[ordered]@{};$categories=@{}
  foreach($name in @('preferences.json','controls.json','remote-diagnosis.json')){
   foreach($suffix in @('','.tmp')){
    $path=Join-Path $userData ($name+$suffix)
    $text=if($name -eq 'preferences.json'){'{"Monitor":false,"Notify":false}'}elseif($name -eq 'remote-diagnosis.json'){'{"fixture":"synthetic-local-access-no-real-token"}'}else{'{"PauseUntil":"2099-01-01T00:00:00","Days":[0],"StartHour":12,"EndHour":17,"ObservationDays":30,"BlockedDevices":[],"BlockedUpdates":[],"BusyProcesses":[],"NetworkLimitMB":2}'}
    Seed $path $text;$categories[$path]='Settings'
   }
  }
  $path=Join-Path $userData 'Codex/nested/ci-fixture.txt';Seed $path 'Synthetic settings only, no credentials';$categories[$path]='Settings'
  foreach($name in @('chat.json','history.json','ai-actions.json','snapshot.json','observations.json','updates.json','manufacturers.json')){
   foreach($suffix in @('','.tmp')){$path=Join-Path $userData ($name+$suffix);Seed $path ('CI log bytes '+$name+$suffix);$categories[$path]='Logs'}
  }
  foreach($name in @('maintenance.json','maintenance.tmp','Audit/nested/ci-lifecycle.json')){$path=Join-Path $data $name;Seed $path ('CI log bytes '+$name);$categories[$path]='Logs'}
  $path=Join-Path $data 'Backups/ci-preserved/nested/saved.inf';Seed $path 'CI fixture, not a driver';$categories[$path]='Backups'
  $binary=Join-Path $data 'Backups/ci-preserved/nested/saved.bin'
  [IO.File]::WriteAllBytes($binary,[byte[]](0,1,2,127,255));$stateHashes[$binary]=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash;$categories[$binary]='Backups'
  # Unknown siblings must survive even all-selected removal.
  foreach($base in @($userData,$data)){$path=Join-Path $base 'ci-unrelated.keep';Seed $path 'Not an owned data category';$categories[$path]='Unselected'}
  Install ($choice.Name+'-install')
  Check-State ($choice.Name+'-seeded')
  $stateHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence ($choice.Name+'-before.json')) -Encoding UTF8
  $code=Invoke-Inno $uninstaller $choice.Name $choice.Flags
  Assert ($code -eq 0) "$($choice.Name) failed with exit code $code"
  Check-Uninstalled
  Assert-LifecycleDataOutcome $choice $stateHashes $categories
  foreach($directory in @(@((Join-Path $userData 'Codex'),'Settings'),@((Join-Path $data 'Audit'),'Logs'),@((Join-Path $data 'Backups'),'Backups'))){
   Assert ((Test-Path -LiteralPath $directory[0] -PathType Container) -eq (-not $choice.($directory[1]))) "$($choice.Name) wrong directory retention: $($directory[0])"
  }
  Record $choice.Name ("Settings=$($choice.Settings); Logs=$($choice.Logs); Backups=$($choice.Backups): selected absent, unselected hashes exact, integrations removed")
 }
}catch{
 $failure=$_
 $results.Add([pscustomobject]@{Case='suite';Status='FAIL';Detail=$_.Exception.Message})
 $_ | Format-List * -Force | Out-String | Set-Content -LiteralPath (Join-Path $evidence 'failure.log') -Encoding UTF8
}finally{
 # Do NOT silently substitute manual deletion for the real uninstaller under test.
 # On failure retain evidence/state on this disposable runner, but always disable its task.
 # Preserve failed GUI state for diagnosis; never turn a failed Quit into a killed-app PASS.
 if($heldApp){$heldApp.Dispose()}
 if($touchedInstallation){
  try{Disable-Maintenance}catch{if(-not $failure){$failure=$_}}
 }
 Exit-Maintenance
 if($maintenanceLock){$maintenanceLock.Dispose()}
 try{
  $trustAfter=@(Get-TrustSnapshot | Sort-Object)
  $trustAfter | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'trust-after.json') -Encoding UTF8
  Assert (($trustBefore -join "`n") -ceq ($trustAfter -join "`n")) 'Public certificate thumbprint snapshot changed during lifecycle'
  Record 'trust-unchanged' 'All CurrentUser/LocalMachine certificate-store thumbprints unchanged; no private keys read/exported'
 }catch{
  $results.Add([pscustomobject]@{Case='trust-unchanged';Status='FAIL';Detail=$_.Exception.Message})
  if(-not $failure){$failure=$_}
 }
 $results.ToArray() | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'results.json') -Encoding UTF8
}
if($failure){throw $failure}
Write-Output "PASS uninstall lifecycle; evidence: $evidence"
