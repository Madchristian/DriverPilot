$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
# Load function definitions only, NEVER execute Setup's installation body.
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'Setup.ps1'),[ref]$tokens,[ref]$errors)
if($errors){throw ($errors|Out-String)}
$functions=$ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)
foreach($f in $functions){Invoke-Expression $f.Extent.Text}
if(-not(Get-Command Assert-TrustedAcl -ErrorAction SilentlyContinue)){throw 'SECURITY: updater has no writable-ACL rejection'}
function Reject([scriptblock]$Action,[string]$Label){try{& $Action}catch{Write-Output "PASS $Label";return};throw "SECURITY: accepted $Label"}
$secure=New-Object Security.AccessControl.DirectorySecurity
$secure.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)')
Assert-TrustedAcl $secure
$programFilesAcl=New-Object Security.AccessControl.DirectorySecurity
$programFilesAcl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICIIO;GA;;;CO)(A;;FA;;;BA)(A;;FA;;;SY)(A;;GRGX;;;BU)')
Assert-TrustedAcl $programFilesAcl
Write-Output 'PASS inherit-only creator-owner ACE does not grant writes on parent'
$writable=New-Object Security.AccessControl.DirectorySecurity
$writable.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;BU)')
Reject {Assert-TrustedAcl $writable} 'admin-owned but user-writable stage parent'
$generic=New-Object Security.AccessControl.DirectorySecurity
$generic.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;;GA;;;BU)(A;;FA;;;BA)')
Reject {Assert-TrustedAcl $generic} 'generic-all untrusted ACE'
$untrusted=New-Object Security.AccessControl.DirectorySecurity
$untrusted.SetSecurityDescriptorSddlForm('O:BUG:BAD:P(A;OICI;FA;;;BA)')
Reject {Assert-TrustedAcl $untrusted} 'user-owned updater'
$parent=New-Object Security.AccessControl.DirectorySecurity
$parent.SetSecurityDescriptorSddlForm('O:SYG:SYD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1201bf;;;BU)')
Assert-TrustedAcl $parent -AllowChildCreation
$parent.SetSecurityDescriptorSddlForm('O:SYG:SYD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1201ff;;;BU)')
Reject {Assert-TrustedAcl $parent -AllowChildCreation} 'parent delete-child permission'
Write-Output 'PASS staging parent permits creation but rejects child replacement'
if(-not(Get-Command Publish-PackageDirectory -ErrorAction SilentlyContinue)){throw 'SECURITY: updater has no testable exact-directory replacement'}
$base=if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}
$tree=Join-Path $base ('DriverPilot-publish-test-'+[guid]::NewGuid().ToString('N'))
$target=Join-Path $tree 'target';$next=Join-Path $tree 'next';$previous=Join-Path $tree 'previous'
try{
 [void][IO.Directory]::CreateDirectory($target);[void][IO.Directory]::CreateDirectory($next)
 [IO.File]::WriteAllText((Join-Path $target 'obsolete.dll'),'old')
 [IO.File]::WriteAllText((Join-Path $next 'current.dll'),'new')
 Publish-PackageDirectory $target $next $previous
 if((Test-Path -LiteralPath (Join-Path $target 'obsolete.dll')) -or -not (Test-Path -LiteralPath (Join-Path $target 'current.dll')) -or -not (Test-Path -LiteralPath (Join-Path $previous 'obsolete.dll'))){throw 'Exact replacement failed'}
 $rollback=Join-Path $tree 'rollback'
 Reject {Publish-PackageDirectory $target $next $rollback} 'failed replacement'
 if(-not (Test-Path -LiteralPath (Join-Path $target 'current.dll')) -or (Test-Path -LiteralPath $rollback)){throw 'Rollback did not restore exact previous installation'}
 Write-Output 'PASS real scratch-directory replacement and failure rollback; no obsolete executable survives'
}finally{if(Test-Path -LiteralPath $tree){Remove-Item -LiteralPath $tree -Recurse -Force}}
if(-not(Get-Command Enter-UpdateLock -ErrorAction SilentlyContinue)){throw 'SECURITY: updater has no concurrent-update lock'}
$lockName='Local\DriverPilot-test-'+[guid]::NewGuid().ToString('N')
$guard=Enter-UpdateLock $lockName
try{
 $command='$m=[Threading.Mutex]::new($false,'''+$lockName+''');if($m.WaitOne(0)){$m.ReleaseMutex();exit 9}else{exit 0}'
 & "$PSHOME/powershell.exe" -NoProfile -Command $command
 if($LASTEXITCODE -ne 0){throw 'SECURITY: second updater obtained lock'}
}finally{$guard.ReleaseMutex();$guard.Dispose()}
Write-Output 'PASS concurrent updater excluded'
Write-Output 'PASS updater ACL policy (no installation or certificate-store changes)'
& (Join-Path $PSScriptRoot 'Test-UpdaterWorkingDirectory.ps1')
