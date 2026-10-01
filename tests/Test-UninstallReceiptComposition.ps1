# Extract production functions, never invoke the backend entry or installed state.
param([switch]$RequireProtectedFixtures)
$ErrorActionPreference='Stop'
$env:PSModulePath=Join-Path $PSHOME 'Modules'
$source=Join-Path (Split-Path $PSScriptRoot -Parent) 'installer/Uninstall.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors){throw ($errors|Out-String)}
foreach($f in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){Invoke-Expression $f.Extent.Text}
function Assert($Condition,[string]$Message){if(-not $Condition){throw $Message}}
Assert ($null -ne (Get-Command Invoke-UninstallReceiptPreflight -ErrorAction SilentlyContinue)) 'Missing composed two-phase production preflight'
Assert ($null -ne (Get-Command Invoke-UninstallRemoval -ErrorAction SilentlyContinue)) 'Missing composed production removal helper'
Initialize-UninstallNative
# Both arguments must classify the same immediate parent even if one is an
# equivalent noncanonical/8.3 spelling. No release must depend on raw spelling.
$pinProbeRoot=Join-Path ([IO.Path]::GetFullPath($(if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}))) ('receipt-pin-classification-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($pinProbeRoot)
$probePins=[pscustomobject]@{Persistent=[Collections.Generic.List[IDisposable]]::new();Shared=[Collections.Generic.List[IDisposable]]::new()}
try{
 $alias=Join-Path $pinProbeRoot ('..\'+(Split-Path $pinProbeRoot -Leaf))
 Add-UninstallPreflightPins (Join-Path $pinProbeRoot 'UninstallPending') $alias $probePins
 Assert ($probePins.Shared.Count -eq 1) 'Equivalent shared path spelling must classify its pin into the release set'
}finally{foreach($pin in $probePins.Shared){$pin.Dispose()};foreach($pin in $probePins.Persistent){$pin.Dispose()};[IO.Directory]::Delete($pinProbeRoot)}
$elevated=([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if($RequireProtectedFixtures -and -not $elevated){throw 'Real protected ACL fixtures require elevation; CI must not use descriptor projection.'}
$base=[IO.Path]::GetFullPath($(if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}))
$scratch=Join-Path $base ('DriverPilot-receipt-composition-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$trustedSddl='O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)'
function Descriptor([string]$Sddl){$acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetSecurityDescriptorSddlForm($Sddl);return $acl}
# Unelevated development still exercises the real native pins, Move, hashes,
# signed package validation and complete orchestration. Only the OS ACL provider
# is projected. CI requires real on-disk BA-owned protected fixtures above.
$script:aclOverrides=@{}
$realCreate=${function:New-UninstallProtectedDirectory}
if(-not $elevated){
 function Get-Acl([string]$LiteralPath){
  Assert ($LiteralPath.StartsWith($scratch,[StringComparison]::OrdinalIgnoreCase)) 'ACL fixture escaped scratch'
  if($script:aclOverrides.ContainsKey($LiteralPath)){return (Descriptor $script:aclOverrides[$LiteralPath])}
  return (Descriptor $trustedSddl)
 }
 function New-UninstallProtectedDirectory([string]$Path){
  Assert-UninstallPath $Path
  [void][IO.Directory]::CreateDirectory($Path)
  Assert-UninstallAcl (Get-Acl -LiteralPath $Path)
 }
}
function Set-FixtureAcl([string]$Path,[string]$Sddl){
 if($elevated){Set-Acl -LiteralPath $Path -AclObject (Descriptor $Sddl)}else{$script:aclOverrides[$Path]=$Sddl}
}
function Get-UninstallRegisteredProfile([string]$OwnerSid){return $script:registered}
function Get-UninstallTask {return $script:task}
function Assert-UninstallRuntimeIdle($Task){Assert-UninstallIdle $script:processes $Task}
# Only OS integrations are substituted; the entire production removal helper runs.
function Test-Path {
 [CmdletBinding()]param([Parameter(Position=0)][string]$Path,[string]$LiteralPath)
 $value=if($LiteralPath){$LiteralPath}else{$Path}
 if($value -eq 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'){return $true}
 return (Microsoft.PowerShell.Management\Test-Path @PSBoundParameters)
}
function Get-ItemProperty {
 [CmdletBinding()]param([string]$LiteralPath)
 Assert ($LiteralPath -eq 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run') 'No real registry reads permitted'
 if($script:runValue){return [pscustomobject]@{DriverPilot=$script:runValue}}
 return [pscustomobject]@{}
}
function Remove-ItemProperty {
 [CmdletBinding()]param([string]$LiteralPath,[string]$Name)
 Assert ($LiteralPath -eq 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -and $Name -eq 'DriverPilot') 'No real registry writes permitted'
 $script:runValue=$null;$script:runRemovals++
}
function Unregister-ScheduledTask {
 [CmdletBinding()]param([string]$TaskName,[string]$TaskPath,[switch]$Confirm)
 Assert ($TaskName -ceq 'DriverPilot Maintenance' -and $TaskPath -eq '\') 'No real task writes permitted'
 $script:task=$null;$script:taskRemovals++
}
function Set-UninstallStableDirectory {$script:stableCalled=$true}
function Expected-Selection([int]$Choice){
 if($Choice -band 1){
  foreach($name in @('preferences.json','controls.json','remote-diagnosis.json')){foreach($suffix in @('','.tmp')){Join-Path $script:user ($name+$suffix)}}
  Join-Path $script:user 'Codex'
 }
 if($Choice -band 2){
  foreach($name in @('chat.json','history.json','ai-actions.json','snapshot.json','observations.json','updates.json','manufacturers.json')){foreach($suffix in @('','.tmp')){Join-Path $script:user ($name+$suffix)}}
  foreach($name in @('maintenance.json','maintenance.tmp','Audit')){Join-Path $script:ctx.Shared $name}
 }
 if($Choice -band 4){Join-Path $script:ctx.Shared 'Backups'}
}

Add-Type -TypeDefinition @'
using System;using System.Threading;using System.Runtime.InteropServices;using Microsoft.Win32.SafeHandles;
public static class ReceiptCompositionProbe {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string p,uint a,uint s,IntPtr sa,uint m,uint f,IntPtr t);
 public static SafeFileHandle Writer(string p){var h=CreateFile(p,0x40000000,7,IntPtr.Zero,3,0x02200000,IntPtr.Zero);if(h.IsInvalid){h.Dispose();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}return h;}
 public static bool CanWrite(string p){try{using(var h=Writer(p)){return true;}}catch{return false;}}
 public static bool MutexHeld(string name){bool held=false;var t=new Thread(()=>{using(var m=new Mutex(false,name)){bool got=m.WaitOne(0);held=!got;if(got)m.ReleaseMutex();}});t.Start();t.Join();return held;}
}
'@
$realSave=${function:Save-UninstallReceipt}
function Save-UninstallReceipt([string]$Path,[string[]]$Packages,$Owner,[string]$CertificateHash,[switch]$Protected=$true,[switch]$PassThru){
 $script:saveCalls++
 Assert ($script:pins.Shared.Count -eq 0) 'Every shared immediate-parent pin must be released for publication'
 foreach($pin in $script:pins.Persistent){Assert (-not $pin.IsClosed) 'Persistent pins must survive publication'}
 Assert (-not [ReceiptCompositionProbe]::CanWrite((Split-Path $script:ctx.Shared -Parent))) 'ProgramData ancestor remains pinned'
 Assert (-not [ReceiptCompositionProbe]::CanWrite($script:ctx.Programs)) 'Shortcut ancestors remain pinned'
 if($script:ctx.RemoveSettings -or $script:ctx.RemoveLogs){Assert (-not [ReceiptCompositionProbe]::CanWrite($script:user)) 'User ancestors remain pinned'}
 Assert ([ReceiptCompositionProbe]::MutexHeld($script:mutexName)) 'Lifecycle mutex remains held'
 $result=& $realSave @PSBoundParameters
 Assert ([ReceiptCompositionProbe]::CanWrite($script:ctx.Shared)) 'Own shared pins did not survive publication window'
 if($script:mutation){& $script:mutation}
 $script:afterMutation=Snapshot
 return $result
}
function Snapshot {
 $result=@{}
 foreach($file in Get-ChildItem -LiteralPath $script:caseRoot -Recurse -File -Force){
  if($file.Name -notlike 'UninstallPending*'){$result[$file.FullName]=Get-UninstallHash $file.FullName}
 }
 return $result
}
function Assert-Snapshot($Expected){
 $now=Snapshot
 Assert ($now.Count -eq $Expected.Count) 'No file may be added/deleted by failed preflight'
 foreach($path in $Expected.Keys){Assert ($now[$path] -ceq $Expected[$path]) ('Unexpected byte change: '+$path)}
}
$cert=$null;$rsa=$null
try{
 $rsa=[Security.Cryptography.RSACng]::new(2048)
 $req=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Scratch-only receipt composition',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
 $cert=$req.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-2),[DateTimeOffset]::UtcNow.AddDays(-1))
 $template=Join-Path $scratch 'Template';[void][IO.Directory]::CreateDirectory($template)
 [IO.File]::WriteAllBytes((Join-Path $template 'DriverPilot.cer'),$cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
 $certificateHash=Get-UninstallHash (Join-Path $template 'DriverPilot.cer')
 foreach($name in @('DriverPilot.exe','DriverPilot.dll','DriverPilot.deps.json','DriverPilot.runtimeconfig.json','Setup.ps1','PackageTrust.cs')){[IO.File]::WriteAllText((Join-Path $template $name),'synthetic bytes, never executable')}
 $files=[ordered]@{};foreach($file in Get-ChildItem $template){$files[$file.Name]=Get-UninstallHash $file.FullName}
 $manifest=[Text.Encoding]::UTF8.GetBytes((@{Product='DriverPilot';Version='0.0.1';Files=$files}|ConvertTo-Json))
 [IO.File]::WriteAllBytes((Join-Path $template 'package-manifest.json'),$manifest)
 [IO.File]::WriteAllBytes((Join-Path $template 'package-manifest.sig'),$rsa.SignData($manifest,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1))
 function New-Fixture([string]$Name,[int]$Choice=7,[switch]$Absent){
  $script:caseRoot=Join-Path $scratch $Name
  [void][IO.Directory]::CreateDirectory($script:caseRoot)
  if($elevated){Set-FixtureAcl $script:caseRoot $trustedSddl}
  $pf=Join-Path $script:caseRoot 'ProgramFiles';$pd=Join-Path $script:caseRoot 'ProgramData';$profile=Join-Path $script:caseRoot 'Owner'
  $script:ctx=[pscustomobject]@{ProgramFiles=$pf;Shared=(Join-Path $pd 'DriverPilot');Uninstaller=(Join-Path $pf 'DriverPilot.Uninstall');CurrentSid='S-1-5-21-111-222-333-1001';CurrentProfile=$profile;LocalApplicationData=(Join-Path $profile 'AppData\Local');Programs=(Join-Path $profile 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs');RemoveSettings=[bool]($Choice -band 1);RemoveLogs=[bool]($Choice -band 2);RemoveBackups=[bool]($Choice -band 4)}
  $script:user=Join-Path $script:ctx.LocalApplicationData 'DriverPilot'
  foreach($path in @($pf,$pd,$script:ctx.Uninstaller,$script:user,$script:ctx.Programs)){[void][IO.Directory]::CreateDirectory($path)}
  $script:target=Join-Path $pf 'DriverPilot';Copy-Item $template $script:target -Recurse
  $script:emptyStage=Join-Path $pf ('DriverPilot.next-'+('d'*32));[void][IO.Directory]::CreateDirectory($script:emptyStage)
  $script:pending=Join-Path $script:ctx.Shared 'UninstallPending'
  $script:all=@(Get-UninstallDataPlan $script:user $script:ctx.Shared -RemoveSettings -RemoveLogs -RemoveBackups)
  if(-not $Absent){[void][IO.Directory]::CreateDirectory($script:ctx.Shared)}
  foreach($entry in $script:all){
   if($Absent -and $entry.Path.StartsWith($script:ctx.Shared,[StringComparison]::OrdinalIgnoreCase)){continue}
   $leaf=$entry.Path
   if($entry.Directory){[void][IO.Directory]::CreateDirectory($leaf);$leaf=Join-Path $leaf 'sentinel.bin'}
   [IO.File]::WriteAllBytes($leaf,[byte[]]@(0,255,239,187,191,13,10,128,65))
  }
  [IO.File]::WriteAllText((Join-Path $script:user 'unknown.json'),'preserve user unknown')
  if(-not $Absent){
   [IO.File]::WriteAllText((Join-Path $script:ctx.Shared 'unknown.bin'),'preserve shared unknown')
   [IO.File]::WriteAllText((Join-Path $script:ctx.Shared 'app-install.json'),(@{OwnerSid=$script:ctx.CurrentSid;OwnerProfilePath=$profile}|ConvertTo-Json))
  }
  $shell=New-Object -ComObject WScript.Shell
  try{$link=$shell.CreateShortcut((Join-Path $script:ctx.Programs 'DriverPilot.lnk'));try{$link.TargetPath=Join-Path $script:target 'DriverPilot.exe';$link.WorkingDirectory=$script:target;$link.Save()}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)}}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}
  if($elevated){
   # Do not rely on a runner's default-owner policy for synthetic fixture inputs.
   foreach($item in Get-ChildItem -LiteralPath $script:caseRoot -Recurse -Force){
    $acl=Microsoft.PowerShell.Security\Get-Acl -LiteralPath $item.FullName
    $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    Set-Acl -LiteralPath $item.FullName -AclObject $acl
   }
   Assert-UninstallTree $pf -Protected
   if(-not $Absent){Assert-UninstallTree $script:ctx.Shared -Protected}
  }
  $script:runValue='"'+(Join-Path $script:target 'DriverPilot.exe')+'" --tray';$script:taskRemovals=0;$script:runRemovals=0;$script:stableCalled=$false
  $script:registered=$profile;$script:processes=@()
  $script:task=[pscustomobject]@{TaskName='DriverPilot Maintenance';TaskPath='\';State='Ready';Principal=[pscustomobject]@{UserId=$script:ctx.CurrentSid;LogonType='Interactive';RunLevel='Highest'};Actions=@([pscustomobject]@{Execute=(Join-Path $script:target 'DriverPilot.exe');Arguments='--maintenance';WorkingDirectory=$script:target})}
  $script:pins=[pscustomobject]@{Persistent=[Collections.Generic.List[IDisposable]]::new();Shared=[Collections.Generic.List[IDisposable]]::new()}
  $script:junction=$null;$script:mutation=$null;$script:afterMutation=$null;$script:saveCalls=0;$script:external=$null
  $script:mutexName='Local\DriverPilot.ReceiptFixture.'+[guid]::NewGuid().ToString('N');$script:guard=Enter-UninstallLock $script:mutexName
 }
 function Close-Fixture {
  if($script:external){$script:external.Dispose();$script:external=$null}
  foreach($pin in $script:pins.Shared){$pin.Dispose()};foreach($pin in $script:pins.Persistent){$pin.Dispose()}
  if($script:junction){[IO.Directory]::Delete($script:junction);$script:junction=$null}
  $script:guard.ReleaseMutex();$script:guard.Dispose()
 }
 function Invoke-Fixture {Invoke-UninstallReceiptPreflight $script:ctx $script:pins -CertificateHash $certificateHash}
 Write-Output "PowerShell $($PSVersionTable.PSVersion); real protected ACL fixtures: $elevated"
 for($choice=0;$choice -lt 8;$choice++){
  New-Fixture "choice-$choice" $choice
  try{
   $before=Snapshot;$state=Invoke-Fixture
   Assert ($script:saveCalls -eq 1) 'Fresh run publishes once'
   Assert-Snapshot $before
   Assert ($state.Receipt -and $state.RemoveShortcut) 'Committed receipt and actual shortcut readback'
   Assert (-not [ReceiptCompositionProbe]::CanWrite($script:ctx.Shared)) 'Shared root repinned before caller can remove anything'
   $expected=@(Expected-Selection $choice)
   Assert ((@($state.DataPlan.Path|Sort-Object) -join '|') -ceq (@($expected|Sort-Object) -join '|')) 'Selection bits must map to the independent expected allowlist'
   Invoke-UninstallRemoval $script:ctx $state -CertificateHash $certificateHash
   Assert ($script:taskRemovals -eq 1 -and $script:runRemovals -eq 1 -and $script:stableCalled) 'Production removal invokes exact task/autostart/cwd integration boundaries'
   Assert (-not (Test-Path $state.ShortcutPath) -and -not (Test-Path $script:emptyStage)) 'Production removal retires matching shortcut and empty stage'
   foreach($entry in $script:all){Assert ((Test-Path $entry.Path) -eq ($entry.Path -notin $expected)) "Choice $choice bounded removal"}
   foreach($path in $before.Keys){
    $selected=($path -ieq $state.ShortcutPath) -or $path.StartsWith(($script:target+'\'),[StringComparison]::OrdinalIgnoreCase) -or (@($expected|Where-Object {$path -ieq $_ -or $path.StartsWith(($_+'\'),[StringComparison]::OrdinalIgnoreCase)}).Count -gt 0)
    if(-not $selected){Assert (Test-Path -LiteralPath $path) ('Unselected file disappeared: '+$path);Assert ((Get-UninstallHash $path) -ceq $before[$path]) 'Unselected bytes preserved'}
   }
   foreach($path in @((Join-Path $script:user 'unknown.json'),(Join-Path $script:ctx.Shared 'unknown.bin'),(Join-Path $script:ctx.Shared 'app-install.json'))){Assert ((Test-Path -LiteralPath $path) -and (Get-UninstallHash $path) -ceq $before[$path]) 'Unknown files must exist with original bytes regardless of the returned plan'}
   Assert (-not (Test-Path $script:pending)) 'Receipt retired'
   Write-Output "PASS composed choice $choice, unchanged preflight bytes, unselected/unknown preservation, persistent pins/mutex"
  }finally{Close-Fixture}
 }
 foreach($case in @('absent-root','stale-temp','existing-recovery')){
  New-Fixture $case 7 -Absent:($case -eq 'absent-root')
  try{
   if($case -eq 'stale-temp'){[IO.File]::WriteAllText(($script:pending+'.tmp'),'{interrupted')}
   $state=Invoke-Fixture
   if($case -eq 'absent-root'){Assert-UninstallAcl (Get-Acl -LiteralPath $script:ctx.Shared)}
   if($case -eq 'existing-recovery'){
    # Simulate legitimate partial removal after a successful preflight, retaining receipt.
    [IO.File]::Delete((Join-Path $script:target 'DriverPilot.exe'));[IO.File]::Delete((Join-Path $script:target 'DriverPilot.cer'))
    $script:task=$null;$script:saveCalls=0
    $state=Invoke-Fixture
    Assert ($script:saveCalls -eq 0) 'Existing recovery must never open publication window'
    Invoke-UninstallRemoval $script:ctx $state -CertificateHash $certificateHash
    Assert (-not (Test-Path $script:pending) -and -not (Test-Path $script:target)) 'Existing partial recovery completes production removal and retires receipt'
    Assert ([IO.File]::ReadAllText((Join-Path $script:user 'unknown.json')) -ceq 'preserve user unknown') 'Recovery preserves unknown data'
   }
   Assert (-not (Test-Path ($script:pending+'.tmp'))) 'Stale temp retired after full preflight'
   Write-Output "PASS $case"
  }finally{Close-Fixture}
 }
 $mutations=[ordered]@{
  'reparse'={$path=Join-Path $script:ctx.Shared 'Audit';[IO.Directory]::Delete($path,$true);New-Item -ItemType Junction -Path $path -Target $script:user | Out-Null;$script:junction=$path}
  'untrusted-owner'={Set-FixtureAcl $script:ctx.Shared ('O:'+([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value+'G:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)')}
  'shared-acl'={Set-FixtureAcl $script:ctx.Shared ($trustedSddl+'(A;OICI;FW;;;BU)')}
  'programdata-acl'={Set-FixtureAcl (Split-Path $script:ctx.Shared -Parent) ($trustedSddl+'(A;;0x40;;;BU)')}
  'owner'={[IO.File]::WriteAllText((Join-Path $script:ctx.Shared 'app-install.json'),(@{OwnerSid='S-1-5-21-111-222-333-1002';OwnerProfilePath=$script:ctx.CurrentProfile}|ConvertTo-Json))}
  'profile'={$script:registered=$script:registered+'-changed'}
  'task'={$script:task.Actions[0].Arguments='--foreign'}
  'runtime'={$script:processes=@([pscustomobject]@{Name='DriverPilot.exe';ExecutablePath=$null})}
  'missing-payload'={[IO.File]::Delete((Join-Path $script:target 'DriverPilot.exe'))}
  'missing-authentication'={[IO.File]::Delete((Join-Path $script:target 'package-manifest.json'))}
  'package-tamper'={[IO.File]::AppendAllText((Join-Path $script:target 'DriverPilot.dll'),'tampered')}
  'new-stage'={[void][IO.Directory]::CreateDirectory((Join-Path $script:ctx.ProgramFiles ('DriverPilot.next-'+('a'*32))))}
  'new-recovery'={Copy-Item $template (Join-Path $script:ctx.ProgramFiles ('DriverPilot.previous-'+('b'*32))) -Recurse}
  'data-type'={$path=Join-Path $script:ctx.Shared 'maintenance.json';[IO.File]::Delete($path);[void][IO.Directory]::CreateDirectory($path)}
  'receipt-binding'={$r=[IO.File]::ReadAllText($script:pending)|ConvertFrom-Json;$r|Add-Member NoteProperty Extra 'changed';[IO.File]::WriteAllText($script:pending,($r|ConvertTo-Json -Depth 8))}
  'receipt-readback'={$script:external=[IO.File]::Open($script:pending,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)}
  'pin-reacquire'={$script:external=[ReceiptCompositionProbe]::Writer($script:ctx.Shared)}
  'shortcut'={$shell=New-Object -ComObject WScript.Shell;try{$link=$shell.CreateShortcut((Join-Path $script:ctx.Programs 'DriverPilot.lnk'));try{$link.Arguments='--foreign';$link.Save()}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)}}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}}
 }
 foreach($payload in @('DriverPilot.dll','DriverPilot.deps.json','DriverPilot.runtimeconfig.json','Setup.ps1','PackageTrust.cs','DriverPilot.cer','package-manifest.sig')){
  $mutations.Add(('missing-'+$payload),[scriptblock]::Create("[IO.File]::Delete((Join-Path `$script:target '$payload'))"))
 }
 foreach($name in $mutations.Keys){
  New-Fixture $name
  try{
   $script:mutation=$mutations[$name];$failed=$false
   try{$null=Invoke-Fixture}catch{$failed=$true;$failureMessage=$_.Exception.Message;Assert ($failureMessage -like 'No destructive action begun.*') 'Explicit pre-destructive failure diagnostic'}
   Assert $failed ("Phase B must reject $name")
   Assert ($null -ne $script:afterMutation) 'Mutation must complete before phase B rejects it'
   $reasons=@{'reparse'='Reparse point';'untrusted-owner'='Untrusted uninstall path owner';'shared-acl'='Writable uninstall path ACL';'programdata-acl'='Writable uninstall path ACL';'owner'='Receipt/metadata owner mismatch';'profile'='Owner profile does not match ProfileList';'task'='Maintenance task does not match';'runtime'='is running';'package-tamper'='Uninstall package hash mismatch';'new-stage'='Removal plan changed';'new-recovery'='Package not authorized';'data-type'='Unexpected selected data type';'receipt-binding'='commit/binding';'shortcut'='Removal plan changed'}
   if($reasons.ContainsKey($name)){Assert ($failureMessage.Contains($reasons[$name])) ("Wrong phase-B guard rejected ${name}: $failureMessage")}
   Assert ($script:saveCalls -eq 1 -and (Test-Path $script:pending)) 'Failed phase B retains committed recovery'
   Assert-Snapshot $script:afterMutation
   Assert ((Test-Path $script:emptyStage) -and $script:taskRemovals -eq 0 -and $script:runRemovals -eq 0 -and -not $script:stableCalled) 'Failed preflight must never enter production removal'
   if($name -eq 'profile'){
    $script:registered=$script:ctx.CurrentProfile;$script:mutation=$null;$script:saveCalls=0
    $retry=Invoke-Fixture
    Assert ($retry.Receipt -and $script:saveCalls -eq 0) 'Safe retry after failed phase B retains existing receipt/pins'
   }
   Write-Output "PASS phase mutation $name rejected; no destructive action, receipt retained"
  }finally{Close-Fixture}
 }
 foreach($name in @('unsafe-root','external-parent-lock','external-temp-lock')){
  New-Fixture $name
  try{
   $before=Snapshot
   if($name -eq 'unsafe-root'){Set-FixtureAcl $script:ctx.Shared ($trustedSddl+'(A;OICI;FW;;;BU)')}
   if($name -eq 'external-parent-lock'){$script:external=[DriverPilotUninstall.Native]::Pin($script:ctx.Shared)}
   if($name -eq 'external-temp-lock'){[IO.File]::WriteAllText(($script:pending+'.tmp'),'partial');$script:external=[IO.File]::Open(($script:pending+'.tmp'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)}
   $failed=$false;try{$null=Invoke-Fixture}catch{$failed=$true}
   Assert $failed "$name must fail closed"
   Assert (-not (Test-Path $script:pending)) 'Failed phase A/publication cannot grant recovery authority'
   Assert-Snapshot $before
   if($name -eq 'unsafe-root'){
    $rejected=$false;try{Assert-UninstallAcl (Get-Acl -LiteralPath $script:ctx.Shared)}catch{$rejected=$true};Assert $rejected 'Unsafe root was not repaired'
   }
   Write-Output "PASS $name retains all bytes and grants no receipt authority"
  }finally{Close-Fixture}
 }
 # The entry must call this exact helper after all locks and before any removals.
 $entry=($ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-DriverPilotUninstall'},$false))[0].Extent.Text
 Assert ($entry.IndexOf('Invoke-UninstallReceiptPreflight') -gt $entry.IndexOf('Enter-UninstallLock')) 'Composition under mutexes'
 Assert ($entry.IndexOf('Invoke-UninstallReceiptPreflight') -lt $entry.IndexOf('Invoke-UninstallRemoval')) 'Composition before destructive integration changes'
 Assert ($entry.Contains('Invoke-UninstallRemoval $context $state')) 'Production must consume the returned phase-B state'
 foreach($flag in @('RemoveSettings','RemoveLogs','RemoveBackups')){Assert ($entry.Contains(($flag+'=[bool]$'+$flag))) 'Entry forwards each original selection flag'}
 Assert (-not ($entry -match 'catch\s*\{')) 'No destructive catch cleanup'
 Write-Output 'PASS actual production orchestration is wired before destructive operations'
}finally{
 if($cert){$cert.Dispose()};if($rsa){$rsa.Dispose()}
 if(Test-Path $scratch){Remove-Item -LiteralPath $scratch -Recurse -Force}
}
