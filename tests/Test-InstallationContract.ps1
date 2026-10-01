$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'installer/Setup.ps1'),[ref]$tokens,[ref]$errors)
if($errors){throw ($errors|Out-String)}
$owner=$ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-InstallationOwner'},$false)
if(-not $owner){throw 'FAIL: Setup does not bind installation to its owner SID/profile.'}
Invoke-Expression $owner.Extent.Text
$pending=$ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Assert-NoPendingUninstall'},$false)
if(-not $pending){throw 'Setup must refuse replacement while a recoverable uninstall is pending'}
Invoke-Expression $pending.Extent.Text
$base=if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}
$scratch=Join-Path $base ('DriverPilot-pending-test-'+[guid]::NewGuid().ToString('N'))
try{
 [void][IO.Directory]::CreateDirectory($scratch)
 Assert-NoPendingUninstall $scratch
 [void][IO.Directory]::CreateDirectory((Join-Path $scratch 'UninstallPending'))
 $rejected=$false
 try{Assert-NoPendingUninstall $scratch}catch{$rejected=$true}
 if(-not $rejected){throw 'Pending uninstall accepted new installation generation'}
}finally{if(Test-Path -LiteralPath $scratch){Remove-Item -LiteralPath $scratch -Recurse -Force}}
function Assert($Condition,$Message){if(-not $Condition){throw $Message}}
$sid='S-1-5-21-1-2-3-1001';$profile='C:\Users\fixture'
$result=Get-InstallationOwner $null $sid $profile
Assert ($result.OwnerSid -eq $sid -and $result.OwnerProfilePath -eq $profile) 'Fresh installation owner missing'
$result=Get-InstallationOwner ([pscustomobject]@{Version='0.3.4'}) $sid $profile
Assert ($result.OwnerSid -eq $sid) 'Legacy migration should bind current owner'
$result=Get-InstallationOwner ([pscustomobject]@{OwnerSid=$sid;OwnerProfilePath=$profile}) $sid $profile
Assert ($result.OwnerSid -eq $sid) 'Same-user upgrade lost ownership'
foreach($old in @([pscustomobject]@{OwnerSid='S-1-5-21-1-2-3-1002';OwnerProfilePath=$profile},[pscustomobject]@{OwnerSid=$sid;OwnerProfilePath='C:\Users\someone-else'},[pscustomobject]@{OwnerSid=$sid})){
 $rejected=$false
 try{Get-InstallationOwner $old $sid $profile | Out-Null}catch{$rejected=$true}
 Assert $rejected 'Different/incomplete installation owner accepted'
}
$iss=[IO.File]::ReadAllText((Join-Path $root 'installer/Installer.iss'))
Assert ($iss -match '(?m)^Uninstallable=yes\r?$') 'Apps registration is missing'
Assert ($iss -match '(?m)^UninstallFilesDir=\{autopf\}\\DriverPilot.Uninstall\r?$') 'Uninstaller must stay outside authenticated app inventory'
Assert ($iss -match 'function InitializeUninstall\(\): Boolean;') 'Missing fail-closed uninstall gate'
Assert ($iss -match "UninstallSilent") 'Silent uninstall must not prompt for data deletion'
$initialize=[regex]::Match($iss,'(?s)function InitializeUninstall\(\): Boolean;(.*?)procedure CurUninstallStepChanged').Groups[1].Value
Assert ($initialize -and $initialize -notmatch '\bExec\(') 'Cancellation must not occur after destructive backend execution'
Assert ($iss -match 'if CurUninstallStep <> usUninstall then Exit;' -and $iss -match 'RaiseException\(') 'Backend failure must be fatal before Inno removal'
Assert ($iss -notmatch '(?im)^\[UninstallDelete\]') 'Inno must not bypass guarded backend deletion'
Assert ($iss -notmatch '\{app\}') 'Caller-controlled /DIR must not redirect the privileged uninstall helper'
$prepare=[regex]::Match($iss,'(?s)function PrepareToInstall\(var NeedsRestart: Boolean\): String;(.*?)procedure CurStepChanged').Groups[1].Value
Assert ($prepare.Contains('UninstallPending')) 'Pending uninstall must stop Inno before its first file/registration write'
Assert ($iss -match 'if CurStep = ssInstall then' -and $iss -notmatch 'if CurStep = ssPostInstall then') 'Setup backend failures must be fatal before Inno installs files'
Assert ($iss.Contains("ExtractTemporaryFiles('{tmp}\DriverPilotPayload\*')")) 'Backend requires embedded payload before normal copying begins'
[xml]$project=Get-Content -LiteralPath (Join-Path $root 'src/DriverPilot/DriverPilot.csproj')
Assert (@($project.Project.ItemGroup.Content | Where-Object TargetPath -eq 'Uninstall.ps1').Count -eq 1) 'Uninstall backend missing from authenticated package'
'PASS installation ownership and uninstaller packaging contract'
