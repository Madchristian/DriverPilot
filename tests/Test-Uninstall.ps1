# AST-only tests: NEVER invoke the uninstall entry point or touch installed state.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source=Join-Path $root 'installer/Uninstall.ps1'
function Assert($Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Reject([scriptblock]$Action,[string]$Message){$rejected=$false;try{& $Action}catch{$rejected=$true};Assert $rejected $Message}
function Hash([string]$Path){$sha=[Security.Cryptography.SHA256]::Create();$stream=[IO.File]::OpenRead($Path);try{return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}}
Assert (Test-Path -LiteralPath $source) 'Missing uninstall backend'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
Assert (-not $errors) ($errors|Out-String)
foreach($f in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){Invoke-Expression $f.Extent.Text}
$userRoot='C:\Users\Fixture\AppData\Local\DriverPilot';$shared='C:\ProgramData\DriverPilot'
Assert (@(Get-UninstallDataPlan $userRoot $shared).Count -eq 0) 'Default must preserve all data'
$settings=@(Get-UninstallDataPlan $userRoot $shared -RemoveSettings)
Assert ($settings.Path -contains (Join-Path $userRoot 'remote-diagnosis.json')) 'Remote diagnosis local access belongs to settings'
Assert ($settings.Path -contains (Join-Path $userRoot 'remote-diagnosis.json.tmp')) 'Interrupted remote access saves belong to settings'
Assert ($settings.Count -eq 7) 'Settings allowlist includes remote access, atomic-save leftovers and the dedicated Codex home'
Assert ($settings.Path -contains "$userRoot\preferences.json") 'Preferences selected'
Assert ($settings.Path -contains "$userRoot\controls.json") 'Controls selected'
Assert ($settings.Path -contains "$userRoot\Codex") 'Dedicated Codex authentication/configuration selected'
$logs=@(Get-UninstallDataPlan $userRoot $shared -RemoveLogs)
Assert ($logs.Path -contains "$userRoot\chat.json") 'Chat belongs to logs'
Assert ($logs.Path -contains "$userRoot\chat.json.tmp") 'Interrupted chat saves belong to logs'
Assert ($logs.Path -contains "$shared\Audit") 'Audit belongs to logs'
Assert ($logs.Path -notcontains "$shared\Backups") 'Logs must not select backups'
$backups=@(Get-UninstallDataPlan $userRoot $shared -RemoveBackups)
Assert ($backups.Count -eq 1 -and $backups[0].Path -eq "$shared\Backups") 'Independent backup switch'
Write-Output 'PASS independent bounded data plans; default preserves everything'
$sid='S-1-5-21-111-222-333-1001';$other='S-1-5-21-111-222-333-1002'
$task=[pscustomobject]@{TaskName='DriverPilot Maintenance';TaskPath='\';State='Ready';Principal=[pscustomobject]@{UserId=$sid;LogonType='Interactive';RunLevel='Highest'};Actions=@([pscustomobject]@{Execute='C:\Program Files\DriverPilot\DriverPilot.exe';Arguments='--maintenance';WorkingDirectory='C:\Program Files\DriverPilot'})}
Assert (Test-UninstallTask $task 'C:\Program Files\DriverPilot' $sid) 'Exact task must match'
Assert (-not (Test-UninstallTask $task 'C:\Program Files\DriverPilot' $other)) 'Foreign identity must not match'
$task.Actions[0].Arguments='--maintenance --other'
Assert (-not (Test-UninstallTask $task 'C:\Program Files\DriverPilot' $sid)) 'Extra arguments must not match'
$task.Actions[0].Arguments='--maintenance';$task.TaskPath='\Other\'
Assert (-not (Test-UninstallTask $task 'C:\Program Files\DriverPilot' $sid)) 'Foreign task folder'
$task.TaskPath='\'
$metadata=[pscustomobject]@{OwnerSid=$sid;OwnerProfilePath='C:\Users\Fixture'}
$owner=Resolve-UninstallOwner $metadata $sid $other 'C:\Users\Admin' 'C:\Users\Fixture'
Assert ($owner.Sid -eq $sid -and $owner.ProfilePath -eq 'C:\Users\Fixture') 'Metadata owner, not elevated caller'
Reject {Resolve-UninstallOwner $metadata $other $sid 'C:\Users\Fixture' 'C:\Users\Fixture'} 'Task/metadata disagreement'
Reject {Resolve-UninstallOwner $metadata $sid $sid 'C:\Users\Fixture' 'C:\Users\Elsewhere'} 'Profile registry disagreement'
Reject {Resolve-UninstallOwner $null $sid $other 'C:\Users\Admin' ''} 'Legacy foreign owner must fail closed'
$legacy=Resolve-UninstallOwner $null $sid $sid 'C:\Users\Fixture' 'C:\Users\Fixture'
Assert ($legacy.Sid -eq $sid) 'Legacy same identity permitted'
Assert (Test-UninstallRunValue '"C:\Program Files\DriverPilot\DriverPilot.exe" --tray' 'C:\Program Files\DriverPilot') 'Exact autostart'
Assert (-not (Test-UninstallRunValue '"C:\Other\DriverPilot.exe" --tray' 'C:\Program Files\DriverPilot')) 'Foreign autostart retained'
Reject {Assert-UninstallIdle @([pscustomobject]@{Name='DriverPilot.exe'}) $null} 'Running GUI must block'
$task.State='Running'
Reject {Assert-UninstallIdle @() $task} 'Running maintenance must block'
Write-Output 'PASS identity, exact integration ownership and running-process gates'
$base=if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}
$scratch=Join-Path $base ('DriverPilot-uninstall-test-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
try{
 $victim=Join-Path $scratch 'bounded';[void][IO.Directory]::CreateDirectory($victim)
 [IO.File]::WriteAllText((Join-Path $victim 'keep.txt'),'sentinel')
 Initialize-UninstallNative
 Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;using Microsoft.Win32.SafeHandles;
public static class UninstallWriteProbe {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string p,uint a,uint s,IntPtr sa,uint m,uint f,IntPtr t);
 public static bool CanWriteDirectory(string p){using(var h=CreateFile(p,0x40000000,7,IntPtr.Zero,3,0x02200000,IntPtr.Zero)){return !h.IsInvalid;}}
}
'@
 Assert ([UninstallWriteProbe]::CanWriteDirectory($victim)) 'Scratch directory writable before pin'
 $pin=[DriverPilotUninstall.Native]::Pin($victim)
 try{
  Reject {[IO.Directory]::Move($victim,($victim+'-moved'))} 'Pinned directory must not be renameable'
  Assert (-not [UninstallWriteProbe]::CanWriteDirectory($victim)) 'Pin must prevent in-place reparse mutation too'
 }finally{$pin.Dispose()}
 $nested=Join-Path $victim 'nested';[void][IO.Directory]::CreateDirectory($nested)
 [IO.File]::WriteAllText((Join-Path $nested 'remove.txt'),'remove')
 Remove-UninstallBoundedPath $nested -Directory
 Assert (-not (Test-Path -LiteralPath $nested)) 'Bounded directory removed'
 Assert ([IO.File]::ReadAllText((Join-Path $victim 'keep.txt')) -eq 'sentinel') 'Outside selection survives'
 $junction=Join-Path $scratch 'junction'
 New-Item -ItemType Junction -Path $junction -Target $victim | Out-Null
 Reject {Remove-UninstallBoundedPath $junction -Directory} 'Refuse junction selection'
 Reject {Remove-UninstallBoundedPath (Join-Path $junction 'keep.txt')} 'Refuse ancestor traversal'
 [IO.Directory]::Delete($junction)
 foreach($selection in @('Default','Settings','Logs','Backups','All')){
  $user=Join-Path $scratch ($selection+'\User');$common=Join-Path $scratch ($selection+'\Shared')
  [void][IO.Directory]::CreateDirectory($user);[void][IO.Directory]::CreateDirectory($common)
  $all=@(Get-UninstallDataPlan $user $common -RemoveSettings -RemoveLogs -RemoveBackups)
  foreach($entry in $all){if($entry.Directory){[void][IO.Directory]::CreateDirectory($entry.Path);[IO.File]::WriteAllText((Join-Path $entry.Path 'sentinel.txt'),'selected')}else{[IO.File]::WriteAllText($entry.Path,'selected')}}
  [IO.File]::WriteAllText((Join-Path $user 'unknown.json'),'preserve')
  [IO.File]::WriteAllText((Join-Path $common 'app-install.json'),'preserve metadata')
  $selected=@(Get-UninstallDataPlan $user $common -RemoveSettings:($selection -in @('Settings','All')) -RemoveLogs:($selection -in @('Logs','All')) -RemoveBackups:($selection -in @('Backups','All')))
  foreach($entry in $selected){Remove-UninstallBoundedPath $entry.Path -Directory:$entry.Directory}
  foreach($entry in $all){Assert ((Test-Path -LiteralPath $entry.Path) -eq ($entry.Path -notin $selected.Path)) "$selection must remove only selected paths"}
  Assert ([IO.File]::ReadAllText((Join-Path $user 'unknown.json')) -eq 'preserve') 'Unknown user data preserved'
  Assert ([IO.File]::ReadAllText((Join-Path $common 'app-install.json')) -eq 'preserve metadata') 'Shared installation metadata preserved'
 }
 Write-Output 'PASS real scratch removals for default, settings, logs, backups and combined choices'
 $acl=[Security.AccessControl.DirectorySecurity]::new()
 $acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)')
 Assert-UninstallAcl $acl
 $acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FW;;;BU)')
 Reject {Assert-UninstallAcl $acl} 'Writable app ACL must fail'
 $acl.SetSecurityDescriptorSddlForm('O:BUG:BAD:P(A;OICI;FA;;;BA)')
 Reject {Assert-UninstallAcl $acl} 'Untrusted app owner must fail'
 Write-Output 'PASS native no-rename pins, bounded scratch deletion, reparse and ACL gates'
 $package=Join-Path $scratch 'package';[void][IO.Directory]::CreateDirectory($package)
 $rsa=[Security.Cryptography.RSACng]::new(2048)
 $request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Uninstall scratch only',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
 $cert=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-3),[DateTimeOffset]::UtcNow.AddDays(-1))
 try{
  [IO.File]::WriteAllBytes((Join-Path $package 'DriverPilot.cer'),$cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
  $hash=Hash (Join-Path $package 'DriverPilot.cer')
  $files=[ordered]@{}
  foreach($name in @('DriverPilot.exe','DriverPilot.dll','DriverPilot.deps.json','DriverPilot.runtimeconfig.json','Setup.ps1','PackageTrust.cs')){[IO.File]::WriteAllText((Join-Path $package $name),'scratch-only fixture')}
  foreach($file in Get-ChildItem -LiteralPath $package){$files[$file.Name]=Hash $file.FullName}
  $json=@{Product='DriverPilot';Version='0.0.1';Files=$files}|ConvertTo-Json
  $bytes=[Text.Encoding]::UTF8.GetBytes($json)
  [IO.File]::WriteAllBytes((Join-Path $package 'package-manifest.json'),$bytes)
  [IO.File]::WriteAllBytes((Join-Path $package 'package-manifest.sig'),$rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1))
  Assert-UninstallPackage $package $hash
  Reject {Assert-UninstallPackage $package ('0'*64)} 'Wrong certificate pin'
  [IO.File]::WriteAllText((Join-Path $package 'extra.txt'),'unlisted')
  Reject {Assert-UninstallPackage $package $hash} 'Unlisted inventory entry'
  [IO.File]::Delete((Join-Path $package 'extra.txt'))
  [IO.File]::AppendAllText((Join-Path $package 'DriverPilot.exe'),'tamper')
  Reject {Assert-UninstallPackage $package $hash} 'Tampered package file'
  [IO.File]::WriteAllText((Join-Path $package 'package-manifest.json'),$json.Replace('0.0.1','0.0.2'))
  Reject {Assert-UninstallPackage $package $hash} 'Manifest tampering'
  Write-Output 'PASS pinned manifest authentication, full inventory and expired certificate removal'
  # Recover a real partially deleted package using an atomic, independently authenticated receipt.
  [IO.File]::WriteAllText((Join-Path $package 'DriverPilot.exe'),'scratch-only fixture')
  [IO.File]::WriteAllBytes((Join-Path $package 'package-manifest.json'),$bytes)
  $installed=Join-Path $scratch 'DriverPilot';[IO.Directory]::Move($package,$installed)
  $pending=Join-Path $scratch 'UninstallPending'
  $staged=Join-Path $scratch ('DriverPilot.previous-'+('a'*32))
  Copy-Item -LiteralPath $installed -Destination $staged -Recurse
  $emptyNext=Join-Path $scratch ('DriverPilot.next-'+('b'*32));[void][IO.Directory]::CreateDirectory($emptyNext)
  $partialNext=Join-Path $scratch ('DriverPilot.next-'+('c'*32));[void][IO.Directory]::CreateDirectory($partialNext)
  [IO.File]::WriteAllText((Join-Path $partialNext 'outsider.txt'),'preserve partial staging')
  $plan=Get-UninstallPackagePlan $scratch -CertificateHash $hash -Protected:$false -WarningVariable stagingWarnings
  Assert ($installed -in $plan.Packages -and $staged -in $plan.Packages) 'Authenticated main and previous remain eligible'
  Assert ($emptyNext -in $plan.EmptyStages) 'Empty next staging can be safely removed'
  Assert ($partialNext -notin $plan.Packages -and $stagingWarnings.Count -gt 0) 'Partial staging is preserved with warning'
  $malformed=Join-Path $scratch 'DriverPilot.next-invalid';[void][IO.Directory]::CreateDirectory($malformed)
  Reject {Get-UninstallPackagePlan $scratch -CertificateHash $hash -Protected:$false} 'Malformed staging names fail closed'
  [IO.Directory]::Delete($malformed)
  [IO.File]::WriteAllText((Join-Path $staged 'extra.txt'),'foreign')
  Reject {Get-UninstallPackagePlan $scratch -CertificateHash $hash -Protected:$false -WarningAction SilentlyContinue} 'Previous package remains strictly authenticated'
  [IO.File]::Delete((Join-Path $staged 'extra.txt'))
  # A crash during copying must not publish recovery authority or remove anything.
  [IO.File]::WriteAllText(($pending+'.tmp'),'{truncated')
  Assert ($null -eq (Read-UninstallReceipt $pending $scratch -CertificateHash $hash -Protected:$false)) 'Partial temp is never removal authority'
  $tempLock=[IO.File]::Open(($pending+'.tmp'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
  try{Reject {Save-UninstallReceipt $pending @($installed,$staged) $legacy -CertificateHash $hash -Protected:$false} 'Locked temporary receipt must abort commit'}finally{$tempLock.Dispose()}
  Assert (-not (Test-Path -LiteralPath $pending)) 'Failed commit publishes no receipt'
  Assert-UninstallPackage $installed $hash
  Save-UninstallReceipt $pending @($installed,$staged) $legacy -CertificateHash $hash -Protected:$false
  Assert (-not (Test-Path -LiteralPath ($pending+'.tmp'))) 'Retry replaces stale partial temp atomically'
  $savedReceipt=[IO.File]::ReadAllText($pending)
  foreach($badName in @('..\bounded','DriverPilot.previous-invalid','DriverPilot.Uninstall','DriverPilot.next-'+('a'*32)+'\nested')){
   $bad=$savedReceipt|ConvertFrom-Json;$bad.Packages[0].Name=$badName
   [IO.File]::WriteAllText($pending,($bad|ConvertTo-Json -Depth 5))
   Reject {Read-UninstallReceipt $pending $scratch -CertificateHash $hash -Protected:$false} 'Receipt rejects unbounded or unexpected package names'
  }
  $bad=$savedReceipt|ConvertFrom-Json;$bad.Packages[0].Manifest=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json.Replace('0.0.1','0.0.2')))
  [IO.File]::WriteAllText($pending,($bad|ConvertTo-Json -Depth 5))
  Reject {Read-UninstallReceipt $pending $scratch -CertificateHash $hash -Protected:$false} 'Receipt signature remains mandatory'
  [IO.File]::WriteAllText($pending,$savedReceipt)
  Reject {Read-UninstallReceipt $pending $scratch -CertificateHash ('0'*64) -Protected:$false} 'Receipt certificate pin remains mandatory'
  [IO.File]::WriteAllText(($pending+'.unknown'),'sentinel')
  Reject {Read-UninstallReceipt $pending $scratch -CertificateHash $hash -Protected:$false} 'Unexpected recovery names fail closed'
  Assert ([IO.File]::ReadAllText(($pending+'.unknown')) -eq 'sentinel') 'Unexpected recovery artifact preserved'
  [IO.File]::Delete(($pending+'.unknown'))
  $receipt=Read-UninstallReceipt $pending $scratch -CertificateHash $hash -Protected:$false
  Assert ($receipt.OwnerSid -eq $sid -and $receipt.OwnerProfilePath -eq 'C:\Users\Fixture') 'Legacy identity must survive task removal'
  $retryOwner=Resolve-UninstallOwner $receipt '' $sid 'C:\Users\Fixture' 'C:\Users\Fixture'
  Assert ($retryOwner.Sid -eq $sid) 'Legacy retry resolves owner without a surviving task'
  [IO.File]::WriteAllText((Join-Path $staged 'extra.txt'),'outsider')
  Reject {Remove-UninstallReceiptPackages $pending $scratch -CertificateHash $hash -Protected:$false} 'All retained copies must authenticate before any deletion'
  Assert-UninstallPackage $installed $hash
  [IO.File]::Delete((Join-Path $staged 'extra.txt'))
  $locked=[IO.File]::Open((Join-Path $installed 'Setup.ps1'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
  try{Reject {Remove-UninstallReceiptPackages $pending $scratch -CertificateHash $hash -Protected:$false} 'Locked file must leave a recoverable removal'}finally{$locked.Dispose()}
  Assert (Test-Path -LiteralPath $pending) 'Failure must retain receipt'
  Assert (-not (Test-Path -LiteralPath (Join-Path $installed 'DriverPilot.cer'))) 'Failure actually deleted original authentication metadata'
  $retryPlan=Get-UninstallPackagePlan $scratch (Read-UninstallReceipt $pending $scratch -CertificateHash $hash -Protected:$false) -CertificateHash $hash -Protected:$false -WarningAction SilentlyContinue
  Assert ($installed -in $retryPlan.Packages) 'Production retry planning uses the retained signed inventory'
  Reject {Assert-UninstallPackage $installed $hash} 'Ordinary preflight still rejects missing inventory'
  [IO.File]::WriteAllText((Join-Path $installed 'extra.txt'),'outsider')
  Reject {Remove-UninstallReceiptPackages $pending $scratch -CertificateHash $hash -Protected:$false} 'Recovery rejects unlisted files'
  Assert (Test-Path -LiteralPath (Join-Path $installed 'Setup.ps1')) 'Failed preflight must not delete remaining files'
  [IO.File]::Delete((Join-Path $installed 'extra.txt'))
  [IO.File]::WriteAllText((Join-Path $installed 'DriverPilot.exe'),'replacement')
  Reject {Remove-UninstallReceiptPackages $pending $scratch -CertificateHash $hash -Protected:$false} 'Recovery rejects tampered replacement'
  [IO.File]::Delete((Join-Path $installed 'DriverPilot.exe'))
  [IO.File]::WriteAllText((Join-Path $emptyNext 'outsider.txt'),'late arrival')
  Reject {Remove-UninstallEmptyStage $emptyNext -Protected:$false} 'Empty stage removal never recurses into newly arrived files'
  Assert ([IO.File]::ReadAllText((Join-Path $emptyNext 'outsider.txt')) -eq 'late arrival') 'Late staging outsider preserved'
  [IO.File]::Delete((Join-Path $emptyNext 'outsider.txt'))
  Remove-UninstallEmptyStage $emptyNext -Protected:$false
  Assert (-not (Test-Path -LiteralPath $emptyNext)) 'Empty next directory actually removed'
  $receiptLock=[IO.File]::Open($pending,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
  try{Reject {Remove-UninstallReceiptPackages $pending $scratch -CertificateHash $hash -Protected:$false} 'Receipt deletion failure must preserve complete authority'}finally{$receiptLock.Dispose()}
  Assert (-not (Test-Path -LiteralPath $installed)) 'Package removal succeeds before receipt retirement'
  Assert ([IO.File]::ReadAllText($pending) -ceq $savedReceipt) 'Locked receipt remains byte-for-byte complete'
  Remove-UninstallReceiptPackages $pending $scratch -CertificateHash $hash -Protected:$false
  Assert (-not (Test-Path -LiteralPath $installed)) 'Second attempt removes remaining package'
  Assert (-not (Test-Path -LiteralPath $pending)) 'Success retires receipt atomically without blocking reinstall'
  Assert ([IO.File]::ReadAllText((Join-Path $victim 'keep.txt')) -eq 'sentinel') 'Recovery preserves outsider sentinel'
  Assert (-not (Test-Path -LiteralPath $staged)) 'Authenticated previous copy removed'
  Assert ([IO.File]::ReadAllText((Join-Path $partialNext 'outsider.txt')) -eq 'preserve partial staging') 'Unverifiable partial staging survives successful main removal'
  Write-Output 'PASS atomic receipt failures, pinned retry, legacy owner, locked deletion and bounded staging preservation'
 }finally{$cert.Dispose();$rsa.Dispose()}
}finally{if(Test-Path -LiteralPath $scratch){Remove-Item -LiteralPath $scratch -Recurse -Force}}
$lockName='Local\DriverPilot.Uninstall.Test.'+[guid]::NewGuid().ToString('N')
Add-Type -TypeDefinition @'
using System;using System.Threading;
public sealed class UninstallTestLock:IDisposable {
 readonly ManualResetEvent ready=new ManualResetEvent(false),stop=new ManualResetEvent(false);readonly Thread thread;
 public UninstallTestLock(string name){thread=new Thread(()=>{using(var m=new Mutex(false,name)){m.WaitOne();ready.Set();stop.WaitOne();m.ReleaseMutex();}});thread.Start();ready.WaitOne();}
 public void Dispose(){stop.Set();thread.Join();ready.Dispose();stop.Dispose();}
}
'@
$holder=[UninstallTestLock]::new($lockName)
try{Reject {Enter-UninstallLock $lockName} 'Concurrent update/maintenance must block'}finally{$holder.Dispose()}
$guard=Enter-UninstallLock $lockName
try{Assert ($null -ne $guard) 'Acquire released lock'}finally{$guard.ReleaseMutex();$guard.Dispose()}
Assert ($null -ne (Get-Command Invoke-DriverPilotUninstall -ErrorAction SilentlyContinue)) 'Missing guarded entry point'
$text=[IO.File]::ReadAllText($source)
Assert ($text.Contains('Global\DriverPilot.AuthenticatedAppUpdate') -and $text.Contains('Local\DriverPilot.Maintenance')) 'Shared lock names'
Assert ($text.Contains('Global\DriverPilot.UninstallLifecycle') -and $text.Contains('Global\DriverPilot.Maintenance')) 'Cross-session startup and full-maintenance gates'
$entryText=($ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-DriverPilotUninstall'},$false))[0].Extent.Text
Assert ($entryText.Contains('Invoke-UninstallReceiptPreflight') -and $entryText.Contains('Invoke-UninstallRemoval')) 'Entry point must consume composed persistent recovery authority'
Assert ($entryText.IndexOf('Invoke-UninstallReceiptPreflight') -lt $entryText.IndexOf('Invoke-UninstallRemoval')) 'Composed receipt preflight must finish before task removal'
Assert ($text.Contains('$env:PSModulePath=')) 'Windows PowerShell module resolution must ignore inherited PS7 paths'
Assert (-not ($text -match 'Cert:\\|Stop-Process|taskkill|pnputil')) 'No trust store/driver/process-kill operations'
Write-Output 'PASS lock contention/release and guarded entry point contract'
