[CmdletBinding()]
param([switch]$RemoveSettings,[switch]$RemoveLogs,[switch]$RemoveBackups)
$ErrorActionPreference='Stop'
# Do not resolve elevated cmdlets from caller-controlled or PowerShell 7 module paths.
$env:PSModulePath=[IO.Path]::Combine([Environment]::GetFolderPath('System'),'WindowsPowerShell\v1.0\Modules')

function ConvertTo-UninstallSid([string]$Identity){
 if($Identity -match '\AS-1-'){return ([Security.Principal.SecurityIdentifier]::new($Identity)).Value}
 return ([Security.Principal.NTAccount]::new($Identity)).Translate([Security.Principal.SecurityIdentifier]).Value
}
function Test-UninstallTask($Task,[string]$Target,[string]$OwnerSid){
 if(-not $Task){return $false}
 try{
  return $Task.TaskName -ceq 'DriverPilot Maintenance' -and $Task.TaskPath -eq '\' -and
   @($Task.Actions).Count -eq 1 -and $Task.Actions[0].Execute -ieq (Join-Path $Target 'DriverPilot.exe') -and
   $Task.Actions[0].Arguments -ceq '--maintenance' -and $Task.Actions[0].WorkingDirectory -ieq $Target -and
   (ConvertTo-UninstallSid $Task.Principal.UserId) -eq $OwnerSid -and
   [string]$Task.Principal.LogonType -eq 'Interactive' -and [string]$Task.Principal.RunLevel -eq 'Highest'
 }catch{return $false}
}
function Resolve-UninstallOwner($Metadata,[string]$TaskSid,[string]$CurrentSid,[string]$CurrentProfile,[string]$RegisteredProfile){
 if($Metadata -and ($Metadata.PSObject.Properties.Name -contains 'OwnerSid' -or $Metadata.PSObject.Properties.Name -contains 'OwnerProfilePath')){
  $sid=ConvertTo-UninstallSid $Metadata.OwnerSid
  $profile=[string]$Metadata.OwnerProfilePath
  if(-not $profile -or $profile -notmatch '\A[A-Za-z]:\\' -or [IO.Path]::GetFullPath($profile).TrimEnd('\') -ine $profile.TrimEnd('\')){throw 'Invalid owner profile path.'}
  if(-not $RegisteredProfile -or $profile.TrimEnd('\') -ine $RegisteredProfile.TrimEnd('\')){throw 'Owner profile does not match ProfileList.'}
  if($TaskSid -and $TaskSid -ne $sid){throw 'Task/metadata owner mismatch.'}
 }else{
  if(-not $TaskSid -or $TaskSid -ne $CurrentSid){throw 'Legacy owner cannot be established safely.'}
  $sid=$CurrentSid;$profile=$CurrentProfile
  if(-not $RegisteredProfile -or $profile.TrimEnd('\') -ine $RegisteredProfile.TrimEnd('\')){throw 'Legacy profile mismatch.'}
 }
 return [pscustomobject]@{Sid=$sid;ProfilePath=$profile.TrimEnd('\')}
}
function Test-UninstallRunValue($Value,[string]$Target){return $Value -is [string] -and $Value -ceq ('"'+(Join-Path $Target 'DriverPilot.exe')+'" --tray')}
function Assert-UninstallIdle($Processes,$Task){
 if(@($Processes).Count -gt 0 -or ($Task -and [string]$Task.State -eq 'Running')){throw 'DriverPilot GUI/maintenance is running. Close it and retry.'}
}

function Get-UninstallDataPlan([string]$UserRoot,[string]$SharedRoot,[switch]$RemoveSettings,[switch]$RemoveLogs,[switch]$RemoveBackups){
 if($RemoveSettings){
  foreach($name in @('preferences.json','controls.json','remote-diagnosis.json')){foreach($suffix in @('','.tmp')){[pscustomobject]@{Path=(Join-Path $UserRoot ($name+$suffix));Directory=$false}}}
  [pscustomobject]@{Path=(Join-Path $UserRoot 'Codex');Directory=$true}
 }
 if($RemoveLogs){
  foreach($name in @('chat.json','history.json','ai-actions.json','snapshot.json','observations.json','updates.json','manufacturers.json')){foreach($suffix in @('','.tmp')){[pscustomobject]@{Path=(Join-Path $UserRoot ($name+$suffix));Directory=$false}}}
  foreach($name in @('maintenance.json','maintenance.tmp')){[pscustomobject]@{Path=(Join-Path $SharedRoot $name);Directory=$false}}
  [pscustomobject]@{Path=(Join-Path $SharedRoot 'Audit');Directory=$true}
 }
 if($RemoveBackups){[pscustomobject]@{Path=(Join-Path $SharedRoot 'Backups');Directory=$true}}
}

function Initialize-UninstallNative {
 if('DriverPilotUninstall.Native' -as [type]){return}
 # Hold ancestors without FILE_SHARE_WRITE/DELETE, then delete the checked leaf by HANDLE.
 # OPEN_REPARSE_POINT prevents following a link even when it is replaced mid-operation.
 Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace DriverPilotUninstall {
 public static class Native {
  [StructLayout(LayoutKind.Sequential)] struct Info {public uint Attr;public System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write;public uint Volume,High,Low,Links,IndexHigh,IndexLow;}
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr sa,uint mode,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle h,out Info info);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetFileInformationByHandle(SafeFileHandle h,int type,ref int data,uint size);
  static SafeFileHandle Open(string path,bool delete,bool directory) {
   var h=CreateFile(path,0x80000000U|(delete?0x10000U:0U),1,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
   if(h.IsInvalid){h.Dispose();throw new Win32Exception(Marshal.GetLastWin32Error(),path);}
   try {Info i;if(!GetFileInformationByHandle(h,out i))throw new Win32Exception(Marshal.GetLastWin32Error());
    if((i.Attr&0x400)!=0)throw new IOException("Reparse point rejected: "+path);
    if(((i.Attr&0x10)!=0)!=directory)throw new IOException("Unexpected path type: "+path);
    return h;
   }catch{h.Dispose();throw;}
  }
  public static SafeFileHandle Pin(string path){return Open(path,false,true);}
  public static void Delete(string path,bool directory){using(var h=Open(path,true,directory)){int yes=1;if(!SetFileInformationByHandle(h,4,ref yes,4))throw new Win32Exception(Marshal.GetLastWin32Error(),path);}}
 }
}
'@
}
function Get-UninstallHash([string]$Path){
 $sha=[Security.Cryptography.SHA256]::Create();$stream=[IO.File]::OpenRead($Path)
 try{return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
}
function Assert-UninstallPackage([string]$Path,[string]$CertificateHash='2C9F679EA51A77CDBC9D64E171B6C12FC9E31EBE623C8C6E357CDE5B6C9F929B',$ReceiptPackage=$null){
 Assert-UninstallTree $Path
 if($ReceiptPackage){
  $certificateBytes=[Convert]::FromBase64String($ReceiptPackage.Certificate)
  $bytes=[Convert]::FromBase64String($ReceiptPackage.Manifest)
  $signature=[Convert]::FromBase64String($ReceiptPackage.Signature)
 }else{
  $certificateBytes=[IO.File]::ReadAllBytes((Join-Path $Path 'DriverPilot.cer'))
  $manifestPath=Join-Path $Path 'package-manifest.json';$signaturePath=Join-Path $Path 'package-manifest.sig'
  if((Get-Item -LiteralPath $manifestPath).Length -gt 1MB -or (Get-Item -LiteralPath $signaturePath).Length -gt 16384){throw 'Oversized package metadata.'}
  $bytes=[IO.File]::ReadAllBytes($manifestPath);$signature=[IO.File]::ReadAllBytes($signaturePath)
 }
 if($bytes.Length -gt 1MB -or $signature.Length -gt 16384 -or $certificateBytes.Length -gt 64KB){throw 'Oversized package metadata.'}
 $sha=[Security.Cryptography.SHA256]::Create()
 try{if([BitConverter]::ToString($sha.ComputeHash($certificateBytes)).Replace('-','') -cne $CertificateHash){throw 'Unauthorized uninstall certificate.'}}finally{$sha.Dispose()}
 $cert=[Security.Cryptography.X509Certificates.X509Certificate2]::new([byte[]]$certificateBytes)
 $rsa=[Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($cert)
 try{
  # Removal authenticates existing bytes, not a new release. Expiry must not trap installed software.
  if(-not $rsa.VerifyData($bytes,$signature,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)){throw 'Invalid uninstall manifest signature.'}
 }finally{if($rsa){$rsa.Dispose()};$cert.Dispose()}
 $manifest=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json
 if($manifest.Product -cne 'DriverPilot' -or $manifest.Version -notmatch '\A[0-9]+\.[0-9]+\.[0-9]+\z' -or -not $manifest.Files){throw 'Invalid uninstall manifest.'}
 $files=@{}
 foreach($entry in $manifest.Files.PSObject.Properties){
  if($entry.Name -notmatch '\A[A-Za-z0-9_][A-Za-z0-9_.-]*\.(exe|dll|json|ps1|cs|cer|md|txt)\z' -or $entry.Name.Contains('..') -or $entry.Name -in @('package-manifest.json','package-manifest.sig') -or $entry.Value -notmatch '\A[0-9a-fA-F]{64}\z'){throw 'Invalid uninstall inventory path/hash.'}
  $files.Add($entry.Name,$entry.Value)
 }
 foreach($name in @('DriverPilot.exe','DriverPilot.dll','DriverPilot.deps.json','DriverPilot.runtimeconfig.json','Setup.ps1','PackageTrust.cs','DriverPilot.cer')){if(-not $files.ContainsKey($name)){throw 'Incomplete uninstall package.'}}
 if(Test-Path -LiteralPath $Path){
  if(-not (Get-Item -LiteralPath $Path -Force).PSIsContainer){throw 'Package is not a directory.'}
  foreach($entry in Get-ChildItem -LiteralPath $Path -Force){if($entry.PSIsContainer -or (-not $files.ContainsKey($entry.Name) -and $entry.Name -notin @('package-manifest.json','package-manifest.sig'))){throw 'Unlisted uninstall inventory entry.'}}
 }
 foreach($name in $files.Keys){
  $file=Join-Path $Path $name
  if($ReceiptPackage -and -not (Test-Path -LiteralPath $file)){continue}
  if((Get-UninstallHash $file) -ine $files[$name]){throw 'Uninstall package hash mismatch.'}
 }
 if($ReceiptPackage){
  foreach($pair in @(@('package-manifest.json',$ReceiptPackage.Manifest),@('package-manifest.sig',$ReceiptPackage.Signature))){
   $file=Join-Path $Path $pair[0]
   if((Test-Path -LiteralPath $file) -and [Convert]::ToBase64String([IO.File]::ReadAllBytes($file)) -cne $pair[1]){throw 'Replaced package authentication metadata.'}
  }
 }
}

# The receipt is committed as ONE file by a same-directory rename. An interrupted
# write cannot authorize missing files; an interrupted cleanup cannot shred evidence.
function Assert-UninstallReceiptLocation([string]$Path,[switch]$Protected=$true){
 Assert-UninstallPath $Path
 $parent=Split-Path $Path -Parent
 if(Test-Path -LiteralPath $parent){
  if($Protected){Assert-UninstallAcl (Get-Acl -LiteralPath $parent)}
  foreach($entry in Get-ChildItem -LiteralPath $parent -Force | Where-Object {$_.Name -like 'UninstallPending*'}){
   if($entry.PSIsContainer -or $entry.Name -cnotin @('UninstallPending','UninstallPending.tmp')){throw 'Unexpected removal recovery name.'}
   Assert-UninstallTree $entry.FullName -Protected:$Protected
  }
 }
}
function Read-UninstallReceipt([string]$Path,[string]$ProgramFiles,[string]$CertificateHash='2C9F679EA51A77CDBC9D64E171B6C12FC9E31EBE623C8C6E357CDE5B6C9F929B',[switch]$Protected=$true){
 Assert-UninstallReceiptLocation $Path -Protected:$Protected
 $file=$Path
 if(-not (Test-Path -LiteralPath $file)){return}
 if((Get-Item -LiteralPath $file).Length -gt 16MB){throw 'Oversized removal receipt.'}
 $receipt=[IO.File]::ReadAllText($file)|ConvertFrom-Json
 if($receipt.Schema -ne 1 -or -not $receipt.OwnerSid -or -not $receipt.OwnerProfilePath -or $null -eq $receipt.Packages){throw 'Invalid removal receipt.'}
 $names=@{}
 foreach($package in $receipt.Packages){
  if($package.Name -cnotmatch '\ADriverPilot(?:\.(?:previous|next)-[a-fA-F0-9]{32})?\z' -or $names.ContainsKey($package.Name)){throw 'Invalid removal receipt package name.'}
  $names.Add($package.Name,$true)
  $target=Join-Path $ProgramFiles $package.Name
  Assert-UninstallTree $target -Protected:$Protected
  Assert-UninstallPackage $target $CertificateHash $package
 }
 return $receipt
}
function Save-UninstallReceipt([string]$Path,[string[]]$Packages,$Owner,[string]$CertificateHash='2C9F679EA51A77CDBC9D64E171B6C12FC9E31EBE623C8C6E357CDE5B6C9F929B',[switch]$Protected=$true,[switch]$PassThru){
 Assert-UninstallReceiptLocation $Path -Protected:$Protected
 if(Test-Path -LiteralPath $Path){throw 'Removal receipt already exists.'}
 $inventory=@(foreach($package in $Packages){
  $name=Split-Path $package -Leaf
  if($name -cnotmatch '\ADriverPilot(?:\.(?:previous|next)-[a-fA-F0-9]{32})?\z'){throw 'Invalid removal package name.'}
  Assert-UninstallTree $package -Protected:$Protected;Assert-UninstallPackage $package $CertificateHash
  [ordered]@{Name=$name;Certificate=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $package 'DriverPilot.cer')));Manifest=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $package 'package-manifest.json')));Signature=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $package 'package-manifest.sig')))}
 })
 $bytes=[Text.Encoding]::UTF8.GetBytes((@{Schema=1;OwnerSid=$Owner.Sid;OwnerProfilePath=$Owner.ProfilePath;Packages=$inventory}|ConvertTo-Json -Depth 5))
 if($bytes.Length -gt 16MB){throw 'Oversized removal receipt.'}
 if($Protected){New-UninstallProtectedDirectory (Split-Path $Path -Parent)}else{[void][IO.Directory]::CreateDirectory((Split-Path $Path -Parent))}
 Assert-UninstallReceiptLocation $Path -Protected:$Protected
 $temporary=$Path+'.tmp'
 # A leftover temporary file never grants recovery authority. Full preflight above
 # must succeed again before replacing it.
 Remove-UninstallBoundedPath $temporary
 $stream=[IO.FileStream]::new($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
 try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
 [IO.File]::Move($temporary,$Path)
 if($PassThru){$sha=[Security.Cryptography.SHA256]::Create();try{return [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','')}finally{$sha.Dispose()}}
}
function Remove-UninstallReceiptPackages([string]$Path,[string]$ProgramFiles,[string]$CertificateHash='2C9F679EA51A77CDBC9D64E171B6C12FC9E31EBE623C8C6E357CDE5B6C9F929B',[switch]$Protected=$true){
 $receipt=Read-UninstallReceipt $Path $ProgramFiles -CertificateHash $CertificateHash -Protected:$Protected
 if(-not $receipt){throw 'Committed removal receipt required.'}
 foreach($package in $receipt.Packages){
  $target=Join-Path $ProgramFiles $package.Name
  Remove-UninstallBoundedPath $target -Directory
  if(Test-Path -LiteralPath $target){throw 'Application removal could not be verified.'}
 }
 Remove-UninstallBoundedPath ($Path+'.tmp')
 Remove-UninstallBoundedPath $Path
}
function Assert-UninstallAcl($Acl,[switch]$AllowChildCreation){
 $trusted=@('S-1-5-18','S-1-5-32-544','S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
 if($Acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted){throw 'Untrusted uninstall path owner.'}
 $write=[Security.AccessControl.FileSystemRights]'Write,Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership'
 if($AllowChildCreation){$write=[Security.AccessControl.FileSystemRights]'Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership'}
 foreach($rule in $Acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
  if($rule.AccessControlType -eq 'Allow' -and -not ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -and (([int64]$rule.FileSystemRights -band ([int64]$write -bor 0x50000000)) -ne 0) -and $rule.IdentityReference.Value -notin $trusted){throw 'Writable uninstall path ACL.'}
 }
}
function Assert-UninstallPath([string]$Path){
 for($p=[IO.Path]::GetFullPath($Path);$p;$p=[IO.Path]::GetDirectoryName($p)){
  if(Test-Path -LiteralPath $p){if((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse point in uninstall path.'}}
 }
}
function Assert-UninstallTree([string]$Path,[switch]$Protected){
 Assert-UninstallPath $Path
 if(-not(Test-Path -LiteralPath $Path)){return}
 if($Protected){Assert-UninstallAcl (Get-Acl -LiteralPath $Path)}
 if((Get-Item -LiteralPath $Path -Force).PSIsContainer){foreach($entry in Get-ChildItem -LiteralPath $Path -Force){Assert-UninstallTree $entry.FullName -Protected:$Protected}}
}
function Enter-UninstallPathPins([string]$Path){
 $paths=[Collections.Generic.List[string]]::new()
 for($p=[IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path));$p;$p=[IO.Path]::GetDirectoryName($p)){$paths.Insert(0,$p)}
 $pins=[Collections.Generic.List[IDisposable]]::new()
 try{foreach($p in $paths){if(Test-Path -LiteralPath $p){$pins.Add([DriverPilotUninstall.Native]::Pin($p))}else{break}};return ,$pins}
 catch{foreach($pin in $pins){$pin.Dispose()};throw}
}
function Remove-UninstallBoundedPath([string]$Path,[switch]$Directory){
 $pins=Enter-UninstallPathPins $Path
 try{
  Assert-UninstallPath $Path
  if(-not(Test-Path -LiteralPath $Path)){return}
  if($Directory){
   $pin=[DriverPilotUninstall.Native]::Pin($Path)
   try{foreach($entry in Get-ChildItem -LiteralPath $Path -Force){Remove-UninstallBoundedPath $entry.FullName -Directory:$entry.PSIsContainer}}finally{$pin.Dispose()}
  }
  [DriverPilotUninstall.Native]::Delete($Path,[bool]$Directory)
 }finally{foreach($pin in $pins){$pin.Dispose()}}
}

function Get-UninstallPackagePlan {
 [CmdletBinding()]
 param([string]$ProgramFiles,$Receipt=$null,[string]$CertificateHash='2C9F679EA51A77CDBC9D64E171B6C12FC9E31EBE623C8C6E357CDE5B6C9F929B',[switch]$Protected=$true)
 $packages=[Collections.Generic.List[string]]::new();$empty=[Collections.Generic.List[string]]::new();$observed=[Collections.Generic.List[string]]::new()
 foreach($entry in Get-ChildItem -LiteralPath $ProgramFiles -Force | Where-Object {$_.Name -ieq 'DriverPilot' -or $_.Name -like 'DriverPilot.previous-*' -or $_.Name -like 'DriverPilot.next-*'}){
  $observed.Add($entry.FullName)
  if(-not $entry.PSIsContainer -or $entry.Name -cnotmatch '\ADriverPilot(?:\.(?:previous|next)-[a-fA-F0-9]{32})?\z'){throw 'Unattributable DriverPilot recovery/staging path; manual review required.'}
  Assert-UninstallTree $entry.FullName -Protected:$Protected
  $prior=@($Receipt.Packages | Where-Object {$_.Name -ceq $entry.Name})
  if($prior.Count){Assert-UninstallPackage $entry.FullName $CertificateHash $prior[0];$packages.Add($entry.FullName);continue}
  if($entry.Name -cmatch '\ADriverPilot\.next-'){
   if(@(Get-ChildItem -LiteralPath $entry.FullName -Force).Count -eq 0){$empty.Add($entry.FullName);continue}
   try{Assert-UninstallPackage $entry.FullName $CertificateHash}catch{Write-Warning ('Preserved incomplete or unverifiable staging directory: '+$entry.FullName);continue}
  }else{Assert-UninstallPackage $entry.FullName $CertificateHash}
  $packages.Add($entry.FullName)
 }
 return [pscustomobject]@{Packages=$packages.ToArray();EmptyStages=$empty.ToArray();ObservedPackages=$observed.ToArray()}
}
function Remove-UninstallEmptyStage([string]$Path,[switch]$Protected=$true){
 if((Split-Path $Path -Leaf) -cnotmatch '\ADriverPilot\.next-[a-fA-F0-9]{32}\z'){throw 'Invalid empty staging name.'}
 $pins=Enter-UninstallPathPins $Path
 try{
  Assert-UninstallTree $Path -Protected:$Protected
  if(-not (Test-Path -LiteralPath $Path)){return}
  if(@(Get-ChildItem -LiteralPath $Path -Force).Count){throw 'Staging directory is no longer empty.'}
  # Never recurse into unsigned staging. Native directory deletion fails if a
  # child appears after the empty check, preserving that child.
  [DriverPilotUninstall.Native]::Delete($Path,$true)
 }finally{foreach($pin in $pins){$pin.Dispose()}}
}

function Enter-UninstallLock([string]$Name){
 $guard=[Threading.Mutex]::new($false,$Name);$held=$false
 try{
  try{$held=$guard.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true}
  if(-not $held){throw 'Another DriverPilot operation is running.'}
  return $guard
 }catch{if($held){$guard.ReleaseMutex()};$guard.Dispose();throw}
}
function Get-UninstallTask {
 # Enumerate the exact root folder; errors are not confused with an absent task.
 $tasks=@(Get-ScheduledTask -TaskPath '\' -ErrorAction Stop | Where-Object {$_.TaskName -ceq 'DriverPilot Maintenance'})
 if($tasks.Count -gt 1){throw 'Ambiguous maintenance task.'}
 if($tasks.Count){return $tasks[0]}
}
function Assert-UninstallRuntimeIdle($Task){
 # CIM includes other sessions. Unknown paths do not permit a same-name process through.
 $processes=@(Get-CimInstance Win32_Process -Filter "Name='DriverPilot.exe'" -ErrorAction Stop)
 Assert-UninstallIdle $processes $Task
}
function New-UninstallProtectedDirectory([string]$Path){
 Assert-UninstallPath $Path
 if(-not (Test-Path -LiteralPath $Path)){
  $acl=[Security.AccessControl.DirectorySecurity]::new()
  $acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)')
  [void][IO.Directory]::CreateDirectory($Path,$acl)
 }
 # Existing roots are verified, NEVER repaired.
 Assert-UninstallAcl (Get-Acl -LiteralPath $Path)
}
function Get-UninstallRegisteredProfile([string]$OwnerSid){
 $registered=(Get-ItemProperty -LiteralPath ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\'+$OwnerSid) -Name ProfileImagePath -ErrorAction Stop).ProfileImagePath
 return [Environment]::ExpandEnvironmentVariables($registered)
}
function Test-UninstallShortcut([string]$Path,[string]$Target){
 if(-not (Test-Path -LiteralPath $Path)){return $false}
 $shell=New-Object -ComObject WScript.Shell
 try{
  $shortcut=$shell.CreateShortcut($Path)
  try{return $shortcut.TargetPath -ieq (Join-Path $Target 'DriverPilot.exe') -and $shortcut.WorkingDirectory -ieq $Target -and -not $shortcut.Arguments}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)}
 }finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}
}
function Add-UninstallPreflightPins([string]$Path,[string]$Shared,$Pins){
 # Every duplicate immediate-parent handle belongs to the same release set.
 $sharedFull=[IO.Path]::GetFullPath($Shared).TrimEnd('\')
 $paths=[Collections.Generic.List[string]]::new()
 for($p=[IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path));$p;$p=[IO.Path]::GetDirectoryName($p)){$paths.Insert(0,$p)}
 foreach($p in $paths){
  if(-not (Test-Path -LiteralPath $p)){break}
  $pin=[DriverPilotUninstall.Native]::Pin($p)
  if($p -ieq $sharedFull){$Pins.Shared.Add($pin)}else{$Pins.Persistent.Add($pin)}
 }
}
function Get-UninstallPreflightState($Context,$Pins,[switch]$Fresh,[string]$CertificateHash='2C9F679EA51A77CDBC9D64E171B6C12FC9E31EBE623C8C6E357CDE5B6C9F929B'){
 $programFiles=$Context.ProgramFiles;$shared=$Context.Shared;$target=Join-Path $programFiles 'DriverPilot'
 $RemoveSettings=$Context.RemoveSettings;$RemoveLogs=$Context.RemoveLogs;$RemoveBackups=$Context.RemoveBackups
 Assert-UninstallPath $programFiles;Assert-UninstallAcl (Get-Acl -LiteralPath $programFiles)
 Assert-UninstallTree $Context.Uninstaller -Protected
 Add-UninstallPreflightPins (Join-Path $shared 'UninstallPending') $shared $Pins
  $task=Get-UninstallTask
  Assert-UninstallRuntimeIdle $task
  # Authenticate retained copies after loading any committed recovery authority.
  Assert-UninstallPath $shared
  Assert-UninstallAcl (Get-Acl -LiteralPath (Split-Path $shared)) -AllowChildCreation
  $metadata=$null
  if(Test-Path -LiteralPath $shared){
   Assert-UninstallAcl (Get-Acl -LiteralPath $shared)
   $metadataPath=Join-Path $shared 'app-install.json'
   if(Test-Path -LiteralPath $metadataPath){
    Assert-UninstallTree $metadataPath -Protected
    if((Get-Item -LiteralPath $metadataPath).Length -gt 64KB){throw 'Oversized owner metadata.'}
    $metadata=Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
   }
  }
  $pending=Join-Path $shared 'UninstallPending'
  $receipt=Read-UninstallReceipt $pending $programFiles -CertificateHash $CertificateHash
  if($Fresh){foreach($package in $receipt.Packages){Assert-UninstallPackage (Join-Path $programFiles $package.Name) $CertificateHash}}
  $packagePlan=Get-UninstallPackagePlan $programFiles $receipt -CertificateHash $CertificateHash
  $packages=$packagePlan.Packages
  if($receipt){
   foreach($path in $packages){if((Split-Path $path -Leaf) -cnotin @($receipt.Packages.Name)){throw 'Package not authorized by pending removal receipt.'}}
   if($metadata -and $metadata.OwnerSid -and ($metadata.OwnerSid -ne $receipt.OwnerSid -or $metadata.OwnerProfilePath -ine $receipt.OwnerProfilePath)){throw 'Receipt/metadata owner mismatch.'}
   $metadata=$receipt
  }
  $taskSid=if($task){ConvertTo-UninstallSid $task.Principal.UserId}else{''}
  $ownerSid=if($metadata -and $metadata.OwnerSid){ConvertTo-UninstallSid $metadata.OwnerSid}else{$taskSid}
  if(-not $ownerSid -or $ownerSid -ne $Context.CurrentSid){throw 'Uninstall must run as the recorded installation owner; different-account elevation is unsupported.'}
  $registered=Get-UninstallRegisteredProfile $ownerSid
  $owner=Resolve-UninstallOwner $metadata $taskSid $Context.CurrentSid $Context.CurrentProfile $registered
  if($task -and -not (Test-UninstallTask $task $target $owner.Sid)){throw 'Maintenance task does not match the installed app and owner.'}
  $userRoot=Join-Path $owner.ProfilePath 'AppData\Local\DriverPilot'
  if((Join-Path $Context.LocalApplicationData 'DriverPilot') -ine $userRoot){throw 'Redirected local application data is unsupported; nothing removed.'}
  $shortcutPath=Join-Path $owner.ProfilePath 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs\DriverPilot.lnk'
  if((Join-Path $Context.Programs 'DriverPilot.lnk') -ine $shortcutPath){throw 'Redirected Start Menu is unsupported; nothing removed.'}
  # Pin user-controlled ancestors before reading a shortcut or inspecting/removing selected data.
  Add-UninstallPreflightPins $shortcutPath $shared $Pins
  Assert-UninstallPath $shortcutPath
  $removeShortcut=Test-UninstallShortcut $shortcutPath $target
  $dataPlan=@(Get-UninstallDataPlan $userRoot $shared -RemoveSettings:$RemoveSettings -RemoveLogs:$RemoveLogs -RemoveBackups:$RemoveBackups)
  foreach($entry in $dataPlan){
   Add-UninstallPreflightPins $entry.Path $shared $Pins
   Assert-UninstallTree $entry.Path
   if(Test-Path -LiteralPath $entry.Path){if([bool](Get-Item -LiteralPath $entry.Path -Force).PSIsContainer -ne $entry.Directory){throw 'Unexpected selected data type.'}}
  }
  # Re-read task ownership and runtime immediately before the first state change.
  $task=Get-UninstallTask
  if($task -and -not (Test-UninstallTask $task $target $owner.Sid)){throw 'Maintenance task changed during preflight.'}
  Assert-UninstallRuntimeIdle $task
  $authority=@(if($receipt){$receipt.Packages}else{foreach($package in $packages){
   [pscustomobject]@{Name=(Split-Path $package -Leaf);Certificate=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $package 'DriverPilot.cer')));Manifest=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $package 'package-manifest.json')));Signature=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $package 'package-manifest.sig')))}
  }})
  return [pscustomobject]@{Authority=$authority;Owner=$owner;Receipt=$receipt;PackagePlan=$packagePlan;DataPlan=$dataPlan;Task=$task;RemoveShortcut=$removeShortcut;ShortcutPath=$shortcutPath}
}
function Get-UninstallPreflightBinding($State){
 # Include even preserved/incomplete staging names: a newly appeared directory is
 # not part of the reviewed plan, irrespective of whether it would be removed.
 $authority=@($State.Authority|Sort-Object Name|ForEach-Object {[ordered]@{Name=$_.Name;Certificate=$_.Certificate;Manifest=$_.Manifest;Signature=$_.Signature}})
 return ([ordered]@{Authority=$authority;Owner=$State.Owner;Packages=@($State.PackagePlan.Packages|Sort-Object);EmptyStages=@($State.PackagePlan.EmptyStages|Sort-Object);Observed=@($State.PackagePlan.ObservedPackages|Sort-Object);Data=@($State.DataPlan);Shortcut=$State.ShortcutPath;RemoveShortcut=$State.RemoveShortcut;TaskPresent=[bool]$State.Task}|ConvertTo-Json -Depth 8 -Compress)
}
function Invoke-UninstallReceiptPreflight($Context,$Pins,[string]$CertificateHash='2C9F679EA51A77CDBC9D64E171B6C12FC9E31EBE623C8C6E357CDE5B6C9F929B'){
 $shared=$Context.Shared;$pending=Join-Path $shared 'UninstallPending'
 try{
  # These ancestors, including ProgramData, outlive BOTH phases and publication.
  Add-UninstallPreflightPins $shared $shared $Pins
  Add-UninstallPreflightPins (Join-Path $Context.ProgramFiles 'DriverPilot') $shared $Pins
  Assert-UninstallPath $shared
  Assert-UninstallAcl (Get-Acl -LiteralPath (Split-Path $shared -Parent)) -AllowChildCreation
  New-UninstallProtectedDirectory $shared
  # Never repair an unsafe existing root. Its ACL is the security boundary while
  # its immediate-parent handles are absent; SYSTEM/Admin/TrustedInstaller trusted.
  Assert-UninstallAcl (Get-Acl -LiteralPath $shared)
  $phaseA=Get-UninstallPreflightState $Context $Pins -CertificateHash $CertificateHash
  $binding=Get-UninstallPreflightBinding $phaseA
  if($phaseA.Receipt){
   # Existing recovery authority never opens the publication window.
   $expectedHash=Get-UninstallHash $pending
  }else{
   foreach($pin in $Pins.Shared){$pin.Dispose()};$Pins.Shared.Clear()
   $expectedHash=Save-UninstallReceipt $pending $phaseA.PackagePlan.Packages $phaseA.Owner -CertificateHash $CertificateHash -PassThru
  }
  # Reacquire first; repeat ALL checks, not merely receipt parsing. A newly
  # committed receipt must NOT make missing payloads acceptable as partial recovery.
  $phaseB=Get-UninstallPreflightState $Context $Pins -Fresh:(-not $phaseA.Receipt) -CertificateHash $CertificateHash
  if(-not $phaseB.Receipt -or (Get-UninstallHash $pending) -cne $expectedHash){throw 'Removal receipt commit/binding could not be verified.'}
  if((Get-UninstallPreflightBinding $phaseB) -cne $binding){throw 'Removal plan changed during receipt publication.'}
  return $phaseB
 }catch{
  # No cleanup here: leave receipt, temp and payloads for a safe retry/manual review.
  throw ('No destructive action begun. Removal recovery files were retained; retry after resolving the preflight failure. '+$_.Exception.Message)
 }
}

function Set-UninstallStableDirectory {
 $stable=[Environment]::GetFolderPath('System');Set-Location -LiteralPath $stable;[Environment]::CurrentDirectory=$stable
}
function Invoke-UninstallRemoval($Context,$State,[string]$CertificateHash='2C9F679EA51A77CDBC9D64E171B6C12FC9E31EBE623C8C6E357CDE5B6C9F929B'){
 $programFiles=$Context.ProgramFiles;$target=Join-Path $programFiles 'DriverPilot'
  $task=$state.Task;$owner=$state.Owner;$packagePlan=$state.PackagePlan;$dataPlan=$state.DataPlan;$removeShortcut=$state.RemoveShortcut;$shortcutPath=$state.ShortcutPath
  $pending=Join-Path $Context.Shared 'UninstallPending'
  if($task){Unregister-ScheduledTask -TaskName 'DriverPilot Maintenance' -TaskPath '\' -Confirm:$false -ErrorAction Stop;if(Get-UninstallTask){throw 'Maintenance task removal could not be verified.'}}
  $run='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
  if(Test-Path -LiteralPath $run){
   $value=(Get-ItemProperty -LiteralPath $run -ErrorAction Stop).DriverPilot
   if(Test-UninstallRunValue $value $target){
    Remove-ItemProperty -LiteralPath $run -Name 'DriverPilot' -ErrorAction Stop
    if((Get-ItemProperty -LiteralPath $run -ErrorAction Stop).PSObject.Properties.Name -contains 'DriverPilot'){throw 'Autostart removal could not be verified.'}
   }
  }
  if($removeShortcut){Remove-UninstallBoundedPath $shortcutPath}
  foreach($entry in $dataPlan){Remove-UninstallBoundedPath $entry.Path -Directory:$entry.Directory}
  # Native cwd, not only PowerShell's provider location, must leave the application tree.
  Set-UninstallStableDirectory
  foreach($path in $packagePlan.EmptyStages){Remove-UninstallEmptyStage $path}
  Remove-UninstallReceiptPackages $pending $programFiles -CertificateHash $CertificateHash
}

function Invoke-DriverPilotUninstall([switch]$RemoveSettings,[switch]$RemoveLogs,[switch]$RemoveBackups){
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
 $principal=[Security.Principal.WindowsPrincipal]::new($identity)
 if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or -not [Environment]::Is64BitProcess){throw 'Use elevated 64-bit Windows PowerShell.'}
 $programFiles=[Environment]::GetFolderPath('ProgramFiles')
 $shared=Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'DriverPilot'
 $target=Join-Path $programFiles 'DriverPilot'
 $uninstaller=Join-Path $programFiles 'DriverPilot.Uninstall'
 if([IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') -notin @($target,$uninstaller)){throw 'Run the protected installed uninstaller, not a portable copy.'}
 Assert-UninstallPath $programFiles;Assert-UninstallAcl (Get-Acl -LiteralPath $programFiles)
 Assert-UninstallTree $PSScriptRoot -Protected
 Initialize-UninstallNative
 $guards=[Collections.Generic.List[IDisposable]]::new()
 $pins=[pscustomobject]@{Persistent=[Collections.Generic.List[IDisposable]]::new();Shared=[Collections.Generic.List[IDisposable]]::new()}
 try{
  foreach($name in @('Global\DriverPilot.UninstallLifecycle','Global\DriverPilot.AuthenticatedAppUpdate','Global\DriverPilot.Maintenance','Local\DriverPilot.Maintenance','Local\DriverPilot.Desktop')){$guards.Add((Enter-UninstallLock $name))}
  $context=[pscustomobject]@{ProgramFiles=$programFiles;Shared=$shared;Uninstaller=$PSScriptRoot;CurrentSid=$identity.User.Value;CurrentProfile=[Environment]::GetFolderPath('UserProfile');LocalApplicationData=[Environment]::GetFolderPath('LocalApplicationData');Programs=[Environment]::GetFolderPath('Programs');RemoveSettings=[bool]$RemoveSettings;RemoveLogs=[bool]$RemoveLogs;RemoveBackups=[bool]$RemoveBackups}
  $state=Invoke-UninstallReceiptPreflight $context $pins
  Invoke-UninstallRemoval $context $state
  Write-Output 'DriverPilot removed. Unselected data and certificate stores were preserved.'
 }finally{
  foreach($pin in $pins.Shared){$pin.Dispose()};foreach($pin in $pins.Persistent){$pin.Dispose()}
  for($i=$guards.Count-1;$i -ge 0;$i--){try{$guards[$i].ReleaseMutex()}finally{$guards[$i].Dispose()}}
 }
}

Invoke-DriverPilotUninstall -RemoveSettings:$RemoveSettings -RemoveLogs:$RemoveLogs -RemoveBackups:$RemoveBackups
