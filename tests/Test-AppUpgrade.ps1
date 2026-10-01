# Disposable CI integration suite. No production credentials and no install on developer PCs.
# Run with Windows PowerShell 5.1: powershell -NoProfile -File tests/Test-AppUpgrade.ps1
# Requires .NET 10 SDK and Windows SDK SignTool (windows-2022). Logs only are published.
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$evidence=Join-Path $root 'artifacts/upgrade-tests'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$transcribing=$false
$certificate=$null;$maintenanceLock=$null;$maintenanceHeld=$false;$heldApp=$null;$copyLock=$null
$results=[Collections.Generic.List[object]]::new()
$failure=$null;$workspace=$null;$touchedInstallation=$false
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Record([string]$Name,[string]$Detail){
 $results.Add([pscustomobject]@{Case=$Name;Status='PASS';Detail=$Detail;Time=[DateTime]::UtcNow.ToString('o')})
 Write-Output "PASS $Name - $Detail"
}
function Quote([string]$Value){return "'"+$Value.Replace("'","''")+"'"}
function Run-PowerShell([string]$Name,[string]$Command,[int]$Timeout=600){
 $out=Join-Path $evidence ($Name+'.stdout.log');$err=Join-Path $evidence ($Name+'.stderr.log')
 $wrapped='$ErrorActionPreference=''Stop''; try { '+$Command+'; if(-not $?) {exit 1} } catch { $_ | Format-List * -Force | Out-String | Write-Error; exit 1 }'
 $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($wrapped))
 $p=Start-Process -FilePath $windowsPowerShell -ArgumentList @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-EncodedCommand',$encoded) -WorkingDirectory $env:SystemRoot -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
 # Cache the native handle: Windows PowerShell Start-Process otherwise loses ExitCode.
 $null=$p.Handle
 try{
  if(-not $p.WaitForExit($Timeout*1000)){& taskkill.exe /PID $p.Id /T /F | Out-Null;throw "$Name timed out ($Timeout seconds)"}
  $p.WaitForExit()
  return [pscustomobject]@{ExitCode=$p.ExitCode;Text=((Get-Content -LiteralPath $out -Raw -ErrorAction SilentlyContinue)+(Get-Content -LiteralPath $err -Raw -ErrorAction SilentlyContinue))}
 }finally{$p.Dispose()}
}
function Disable-Maintenance {
 $task=Get-ScheduledTask -TaskName 'DriverPilot Maintenance' -ErrorAction SilentlyContinue
 if($task){Disable-ScheduledTask -TaskName $task.TaskName | Out-Null}
}
function Check-State([string]$Label){
 $actual=[ordered]@{}
 foreach($path in $stateHashes.Keys){
  Assert (Test-Path -LiteralPath $path -PathType Leaf) "User state removed: $path"
  $actual[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
  Assert ($actual[$path] -eq $stateHashes[$path]) "User state changed: $path"
 }
 $actual | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence ($Label+'-state.json')) -Encoding UTF8
}
function Check-Package([string]$Path,[string]$Version,[string]$Label){
 $manifest=[DriverPilot.Security.PackageTrust]::Verify($Path,$testCertificatePath,'0.0.0',$false)
 Assert ($manifest.Version -eq $Version) "Unexpected installed manifest version in $Label"
 Assert (([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Path 'DriverPilot.dll')).FileVersion) -eq ($Version+'.0')) "Unexpected binary version in $Label"
 $hashes=[ordered]@{}
 foreach($entry in $manifest.Files.GetEnumerator()){
  $hashes[$entry.Key]=(Get-FileHash -LiteralPath (Join-Path $Path $entry.Key) -Algorithm SHA256).Hash
  Assert ($hashes[$entry.Key] -eq $entry.Value) "Manifest file mismatch: $($entry.Key)"
 }
 Assert ((Get-ChildItem -LiteralPath $Path -Force).Count -eq ($manifest.Files.Count+2)) 'Unexpected installed files'
 [ordered]@{Path=$Path;Version=$Version;ManifestHash=[DriverPilot.Security.PackageTrust]::ManifestHash($manifest);Files=$hashes} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence ($Label+'-package.json')) -Encoding UTF8
}
function Check-Launch([string]$Path,[string]$Label){
 # Existing isolated production test entry point: no Store, scans, maintenance or drivers.
 $output=Join-Path $evidence ($Label+'-launch')
 New-Item -ItemType Directory -Path $output -Force | Out-Null
 $p=Start-Process -FilePath (Join-Path $Path 'DriverPilot.exe') -ArgumentList ('--system-report-test --system-report-output "'+$output+'"') -WorkingDirectory $env:SystemRoot -PassThru
 $null=$p.Handle
 try{
  if(-not $p.WaitForExit(240000)){$p.Kill();throw "$Label app launch timed out"}
  $p.WaitForExit();Assert ($p.ExitCode -eq 0) "$Label app failed to launch/run isolated UI test: $($p.ExitCode)"
  Assert (Test-Path -LiteralPath (Join-Path $output 'system-report-test-results.txt')) "$Label launch evidence missing"
 }finally{$p.Dispose()}
}
function Check-Preserved([string]$Version,[string]$Label){
 [DriverPilot.Security.PackageTrust]::AssertInstalledUpdater($target)
 Check-Package $target $Version $Label
 Check-State $Label
 Assert (@(Get-ChildItem -LiteralPath $data -Directory -Filter 'AppStaging-*').Count -eq 0) 'Staging residue after update'
 Assert (@(Get-ChildItem -LiteralPath $env:ProgramFiles -Directory -Filter 'DriverPilot.next-*').Count -eq 0) 'Unpublished installation residue'
 Check-Launch $target $Label
}
function Update([string]$Package,[string]$Label,[string]$ExpectedFailure=''){
 $hash=(Get-FileHash -LiteralPath (Join-Path $Package 'package-manifest.json') -Algorithm SHA256).Hash
 # Exact privileged path used by the app; NEVER execute Setup from an update package.
 $command='& '+(Quote (Join-Path $target 'Setup.ps1'))+' -PackagePath '+(Quote $Package)+' -ApprovedManifestHash '+(Quote $hash)
 $result=Run-PowerShell $Label $command
 Disable-Maintenance
 if($ExpectedFailure){
  Assert ($result.ExitCode -ne 0) "$Label unexpectedly succeeded"
  Assert ($result.Text -match $ExpectedFailure) "$Label failed for wrong reason: $($result.Text)"
 }else{Assert ($result.ExitCode -eq 0) "$Label failed: $($result.Text)"}
 return $result
}
try{
 Start-Transcript -Path (Join-Path $evidence 'transcript.log') -Force | Out-Null;$transcribing=$true
 # This gate precedes certificate creation, builds, app execution and ALL system writes.
 Assert ($env:OS -eq 'Windows_NT' -and $env:GITHUB_ACTIONS -eq 'true' -and $env:RUNNER_ENVIRONMENT -eq 'github-hosted' -and $env:RUNNER_OS -eq 'Windows' -and $env:RUNNER_TEMP) 'App-upgrade integration tests require a disposable github-hosted Windows runner.'
 Assert ($PSVersionTable.PSEdition -eq 'Desktop' -and $PSVersionTable.PSVersion.Major -eq 5) 'Use Windows PowerShell 5.1 (shell: powershell), not pwsh.'
 $principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
 Assert ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) 'Elevated runner required; no interactive UAC is permitted.'
 $windowsPowerShell=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
 $target=Join-Path $env:ProgramFiles 'DriverPilot';$data=Join-Path $env:ProgramData 'DriverPilot'
 $userData=Join-Path $env:LOCALAPPDATA 'DriverPilot'
 foreach($path in @($target,$data,$userData)){Assert (-not(Test-Path -LiteralPath $path)) "Refusing existing installation/user data: $path"}
 Assert (-not(Get-ScheduledTask -TaskName 'DriverPilot Maintenance' -ErrorAction SilentlyContinue)) 'Refusing preexisting maintenance task'
 Assert (@(Get-Process DriverPilot -ErrorAction SilentlyContinue).Count -eq 0) 'Refusing preexisting app process'
 Assert (@(Get-ChildItem -LiteralPath $env:ProgramFiles -Directory -Filter 'DriverPilot.*-*').Count -eq 0) 'Refusing previous updater state'
 $workspace=Join-Path $env:RUNNER_TEMP ('DriverPilot-upgrade-'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $workspace | Out-Null
 # Explicit public-source allowlist, not a recursive checkout copy (.git/secrets excluded).
 foreach($dir in @('src','assets','build','installer','docs','contracts')){Copy-Item -LiteralPath (Join-Path $root $dir) -Destination $workspace -Recurse}
 New-Item -ItemType Directory -Path (Join-Path $workspace 'tests') | Out-Null
 Copy-Item -LiteralPath (Join-Path $root 'tests/InApp') -Destination (Join-Path $workspace 'tests') -Recurse
 Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $workspace
 foreach($dir in @(Get-ChildItem -LiteralPath (Join-Path $workspace 'src') -Directory -Recurse | Where-Object {$_.Name -in @('bin','obj')})){
  if(Test-Path -LiteralPath $dir.FullName){Remove-Item -LiteralPath $dir.FullName -Recurse -Force}
 }
 $certificate=New-SelfSignedCertificate -Type CodeSigningCert -Subject ('CN=DriverPilot DISPOSABLE CI '+[guid]::NewGuid().ToString('N')) -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddDays(2)
 $testCertificatePath=Join-Path $workspace 'assets/certificates/DriverPilot.cer'
 Export-Certificate -Cert $certificate -FilePath $testCertificatePath -Force | Out-Null
 Import-Certificate -FilePath $testCertificatePath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
 Import-Certificate -FilePath $testCertificatePath -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
 $pin=(Get-FileHash -LiteralPath $testCertificatePath -Algorithm SHA256).Hash
 $trustPath=Join-Path $workspace 'src/DriverPilot/Security/PackageTrust.cs'
 $trust=[IO.File]::ReadAllText($trustPath)
 $pattern='public const string CertificateHash = "[0-9A-F]{64}";'
 Assert ([regex]::Matches($trust,$pattern).Count -eq 1) 'Cannot identify unique certificate pin'
 # Only the trust identity changes in the isolated source tree. Verification logic stays intact.
 [IO.File]::WriteAllText($trustPath,([regex]::Replace($trust,$pattern,('public const string CertificateHash = "'+$pin+'";'))),[Text.UTF8Encoding]::new($false))
 Add-Type -Path $trustPath
 Add-Type -Path (Join-Path $PSScriptRoot 'UpgradeFaults.cs')
 [xml]$project=Get-Content -LiteralPath (Join-Path $workspace 'src/DriverPilot/DriverPilot.csproj')
 $currentVersion=[string]$project.Project.PropertyGroup.Version
 $baselineVersion='0.0.1'
 Assert ([version]$currentVersion -gt [version]$baselineVersion) 'Current version must exceed test baseline 0.0.1'
 # Build in fresh powershell processes so Add-Type sees only the test pin, never a cached release type.
 foreach($version in @($baselineVersion,$currentVersion)){
  $project.Project.PropertyGroup.Version=$version
  $project.Save((Join-Path $workspace 'src/DriverPilot/DriverPilot.csproj'))
  $built=Run-PowerShell ('build-'+$version) ('& '+(Quote (Join-Path $workspace 'build/Build.ps1'))+' -Sign') 1200
  Assert ($built.ExitCode -eq 0) "Signed build $version failed: $($built.Text)"
 }
 $baseline=Join-Path $workspace ('artifacts/release-'+$baselineVersion)
 $current=Join-Path $workspace ('artifacts/release-'+$currentVersion)
 # Valid signed inventory fixture only; no updater bypass or production fault hook.
 # First entry lengthens the real staging copy so the OS watcher can lock the final entry.
 $barrier=Join-Path $current 'A-copy-window.txt'
 $stream=[IO.File]::Create($barrier);try{$stream.SetLength(128MB)}finally{$stream.Dispose()}
 $manifest=[ordered]@{Product='DriverPilot';Version=$currentVersion;Files=[ordered]@{}}
 Get-ChildItem -LiteralPath $current -File | Where-Object {$_.Name -notin @('package-manifest.json','package-manifest.sig')} | Sort-Object Name | ForEach-Object {$manifest.Files[$_.Name]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
 [IO.File]::WriteAllText((Join-Path $current 'package-manifest.json'),($manifest|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
 $signed=Run-PowerShell 'sign-copy-fixture' ('. '+(Quote (Join-Path $workspace 'build/Signing.ps1'))+'; Sign-DriverPilotManifest '+(Quote (Join-Path $current 'package-manifest.json')))
 Assert ($signed.ExitCode -eq 0) 'Could not authenticate copy fixture'
 Check-Package $baseline $baselineVersion 'baseline-input'
 Check-Package $current $currentVersion 'current-input'
 [ordered]@{CertificateHash=$pin;Baseline=$baselineVersion;Current=$currentVersion;Bootstrap='Authenticated signed Setup.ps1';Update='Installed Setup.ps1 -PackagePath -ApprovedManifestHash';UiDialogAutomated=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'configuration.json') -Encoding UTF8
 # Prevent task execution even in the tiny register/disable window; paused real rules
 # are an independent safety layer. No maintenance, driver installation or reboot invoked.
 $maintenanceLock=[Threading.Mutex]::new($false,'Local\DriverPilot.Maintenance')
 $maintenanceHeld=$maintenanceLock.WaitOne(0)
 Assert $maintenanceHeld 'Maintenance mutex already held'
 New-Item -ItemType Directory -Path $userData | Out-Null
 $utf8=[Text.UTF8Encoding]::new($false)
 [IO.File]::WriteAllText((Join-Path $userData 'preferences.json'),'{"Monitor":false,"Notify":false,"Ignored":["ci-preserve"],"Notified":[],"SeenEvents":[],"ManufacturerNotified":[]}',$utf8)
 [IO.File]::WriteAllText((Join-Path $userData 'controls.json'),' {"Days":[0],"StartHour":12,"EndHour":17,"ObservationDays":30,"PauseUntil":"2099-01-01T00:00:00","BlockedDevices":["CI-device"],"BlockedUpdates":["CI-package"],"BusyProcesses":["ci-game"],"NetworkLimitMB":2}',$utf8)
 [IO.File]::WriteAllText((Join-Path $userData 'chat.json'),'"CI user conversation retained"',$utf8)
 [IO.File]::WriteAllText((Join-Path $userData 'history.json'),'[{"Time":"CI","Action":"preserve","Detail":"existing user history"}]',$utf8)
 $stateHashes=[ordered]@{}
 foreach($file in Get-ChildItem -LiteralPath $userData -File -Recurse){$stateHashes[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
 $touchedInstallation=$true
 $boot=Run-PowerShell 'bootstrap' ('& '+(Quote (Join-Path $baseline 'Setup.ps1')))
 Disable-Maintenance
 Assert ($boot.ExitCode -eq 0) "Baseline bootstrap failed: $($boot.Text)"
 $backup=Join-Path $data 'Backups/ci-preserved/nested'
 New-Item -ItemType Directory -Path $backup -Force | Out-Null
 [IO.File]::WriteAllText((Join-Path $backup 'saved.inf'),'; preservation fixture, NOT an installable driver',$utf8)
 [IO.File]::WriteAllBytes((Join-Path $backup 'saved.bin'),[byte[]](0,1,2,127,255))
 foreach($file in Get-ChildItem -LiteralPath (Join-Path $data 'Backups') -File -Recurse){$stateHashes[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
 $stateHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'initial-state.json') -Encoding UTF8
 Check-Preserved $baselineVersion 'baseline-installed'
 Record 'bootstrap' 'Signed baseline installed, all inventory bytes verified and app launch passed'
 # Real DriverPilot process, held before normal startup so there are no scans or user writes.
 $heldApp=Start-Process -FilePath (Join-Path $target 'DriverPilot.exe') -ArgumentList "--wait-for $PID" -WorkingDirectory $env:SystemRoot -PassThru
 [UpgradeProcessHold]::Suspend($heldApp)
 try{[void](Update $current 'running-app' 'DriverPilot laeuft noch')}finally{if(-not $heldApp.HasExited){$heldApp.Kill();$heldApp.WaitForExit()};$heldApp.Dispose();$heldApp=$null}
 Check-Preserved $baselineVersion 'after-running-app'
 Record 'running-app' 'Update rejected with live installed app; baseline remains verified and launchable'
 $tampered=Join-Path $workspace 'tampered';Copy-Item -LiteralPath $current -Destination $tampered -Recurse
 [IO.File]::AppendAllText((Join-Path $tampered 'Setup.ps1'),"`r`n# attacker payload`r`n")
 $bad=Get-Content -LiteralPath (Join-Path $tampered 'package-manifest.json') -Raw | ConvertFrom-Json
 $bad.Files.'Setup.ps1'=(Get-FileHash -LiteralPath (Join-Path $tampered 'Setup.ps1') -Algorithm SHA256).Hash
 [IO.File]::WriteAllText((Join-Path $tampered 'package-manifest.json'),($bad|ConvertTo-Json -Depth 5),$utf8)
 [void](Update $tampered 'tampered-rehashed' 'Invalid package manifest signature')
 Check-Preserved $baselineVersion 'after-tampered'
 Record 'tampered-rehashed' 'Payload and manifest hash replaced, approval matches attack manifest; pinned signature still rejects'
 $last=@($manifest.Files.Keys)[-1]
 $copyLock=[UpgradeCopyLock]::new($data,(Join-Path $current $last))
 try{
  $fault=Update $current 'copy-fault' 'CopyVerified'
  Assert ($copyLock.Acquired.WaitOne(0)) "Copy fault was not induced by our OS lock: $($copyLock.Error)"
  Assert ($fault.Text -match 'another process|anderen Prozess|being used|sharing violation') 'Failure did not report a real sharing violation'
 }finally{$copyLock.Dispose();$copyLock=$null}
 Check-Preserved $baselineVersion 'after-copy-fault'
 Record 'copy-fault' "Real exclusive lock on $last during staging CopyVerified; baseline launchable, staging removed"
 [void](Update $current 'upgrade')
 Check-Preserved $currentVersion 'after-upgrade'
 $previous=@(Get-ChildItem -LiteralPath $env:ProgramFiles -Directory -Filter 'DriverPilot.previous-*')
 Assert ($previous.Count -eq 1) 'Expected one retained recovery installation'
 Check-Package $previous[0].FullName $baselineVersion 'retained-baseline'
 Check-Launch $previous[0].FullName 'retained-baseline'
 $metadata=Get-Content -LiteralPath (Join-Path $data 'app-install.json') -Raw | ConvertFrom-Json
 Assert ($metadata.Version -eq ($currentVersion+'.0')) 'Install metadata not updated'
 Record 'upgrade' 'Installed trusted updater upgraded baseline; current and retained baseline both launch, all user bytes preserved'
 [void](Update $current 'same-version' 'Package is not newer')
 Check-Preserved $currentVersion 'after-same'
 Record 'same-version' 'Same version rejected without changing launchable current app'
 [void](Update $baseline 'older-version' 'Package is not newer')
 Check-Preserved $currentVersion 'after-older'
 Record 'older-version' 'Downgrade rejected without changing launchable current app'
 Assert ((Get-ScheduledTask -TaskName 'DriverPilot Maintenance').State -eq 'Disabled') 'Maintenance task is not disabled'
 Record 'safety' 'No driver operations/restarts invoked; maintenance mutex held and task disabled; paused real rules retained'
}catch{
 $failure=$_
 $_ | Format-List * -Force | Out-String | Set-Content -LiteralPath (Join-Path $evidence 'failure.log') -Encoding UTF8
 $results.Add([pscustomobject]@{Case='suite';Status='FAIL';Detail=$_.Exception.Message;Time=[DateTime]::UtcNow.ToString('o')})
}finally{
 if($copyLock){$copyLock.Dispose()}
 if($heldApp){if(-not $heldApp.HasExited){$heldApp.Kill();$heldApp.WaitForExit()};$heldApp.Dispose()}
 if($touchedInstallation){
  try{Disable-Maintenance;Get-ScheduledTask -TaskName 'DriverPilot Maintenance' -ErrorAction SilentlyContinue | Export-Clixml -LiteralPath (Join-Path $evidence 'maintenance-task.xml')}catch{$_|Out-String|Add-Content -LiteralPath (Join-Path $evidence 'cleanup.log')}
 }
 if($maintenanceHeld){$maintenanceLock.ReleaseMutex()}
 if($maintenanceLock){$maintenanceLock.Dispose()}
 if($certificate){
  foreach($store in @('LocalMachine\Root','LocalMachine\TrustedPublisher','CurrentUser\My')){
   try{Remove-Item -LiteralPath ('Cert:\'+$store+'\'+$certificate.Thumbprint) -Force -ErrorAction Stop}catch{$_|Out-String|Add-Content -LiteralPath (Join-Path $evidence 'cleanup.log');if(-not $failure){$failure=$_}}
  }
  $certificate.Dispose()
 }
 # Packages/private build tree remain only in disposable RUNNER_TEMP, never uploaded.
 $results.ToArray() | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'results.json') -Encoding UTF8
 if($transcribing){Stop-Transcript | Out-Null}
}
if($failure){throw $failure}
Write-Output 'PASS signed app upgrade/recovery integration suite; evidence: artifacts/upgrade-tests'
