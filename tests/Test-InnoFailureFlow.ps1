# Execute the production Pascal install callbacks inside a non-elevated scratch-only
# Inno fixture. This is NOT the signed DriverPilot lifecycle and never runs Setup.ps1.
param([string]$Compiler='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not $Compiler){$Compiler=Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'}
if(-not(Test-Path -LiteralPath $Compiler)){throw 'Inno Setup compiler required for isolated callback test.'}
$base=if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}
$scratch=Join-Path $base ('DriverPilot-inno-failure-'+[guid]::NewGuid().ToString('N'))
$payload=Join-Path $scratch 'payload';$target=Join-Path $scratch 'target';$common=Join-Path $scratch 'common'
$utf8=[Text.UTF8Encoding]::new($true)
function Assert($Condition,$Message){if(-not $Condition){throw $Message}}
function Run-Fixture([string]$Label){
 $log=Join-Path $scratch ($Label+'.log')
 $p=Start-Process -FilePath (Join-Path $scratch 'fixture.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+$log+'"')) -WorkingDirectory $scratch -PassThru
 $null=$p.Handle
 try{if(-not $p.WaitForExit(60000)){$p.Kill();throw 'Inno callback fixture timed out'};return $p.ExitCode}finally{$p.Dispose()}
}
try{
 foreach($path in @($payload,$target,(Join-Path $common 'DriverPilot'))){[void][IO.Directory]::CreateDirectory($path)}
 $ran=Join-Path $scratch 'backend-ran.txt'
 [IO.File]::WriteAllText((Join-Path $payload 'Setup.ps1'),('[IO.File]::WriteAllText('''+$ran.Replace("'","''")+''',''fixture backend ran''); [Console]::Out.WriteLine(''DP_SETUP_STDOUT_SENTINEL''); [Console]::Error.WriteLine(''DP_SETUP_STDERR_SENTINEL''); exit 23'),$utf8)
 [IO.File]::WriteAllText((Join-Path $payload 'Uninstall.ps1'),'replacement helper fixture',$utf8)
 $helper=Join-Path $target 'Uninstall.ps1'
 [IO.File]::WriteAllText($helper,'original recovery helper',$utf8)
 $before=[Convert]::ToBase64String([IO.File]::ReadAllBytes($helper))
 $production=[IO.File]::ReadAllText((Join-Path $root 'installer/Installer.iss'))
 $start=$production.IndexOf('function PrepareToInstall(')
 Assert ($start -gt 0) 'Production callback section missing'
 # Only the common-data namespace is mapped to scratch. Event names, extraction,
 # process execution, result handling and exception ordering are production code.
 $callbacks=$production.Substring($start).Replace('{commonappdata}',$common.Replace("'","''"))
 $fixture=@"
[Setup]
AppId=DriverPilot.IsolatedCallbackFixture
AppName=DriverPilot isolated callback fixture
AppVersion=0.0.0
PrivilegesRequired=lowest
CreateAppDir=no
DefaultDirName=$target
Uninstallable=no
CreateUninstallRegKey=no
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=$scratch
OutputBaseFilename=fixture
Compression=none
[Files]
Source: "$payload\*"; DestDir: "{tmp}\DriverPilotPayload"; Flags: dontcopy
Source: "$payload\Uninstall.ps1"; DestDir: "$target"; Flags: ignoreversion
[Code]
$callbacks
"@
 $source=Join-Path $scratch 'fixture.iss';[IO.File]::WriteAllText($source,$fixture,$utf8)
 & $Compiler '/Q' $source
 if($LASTEXITCODE -ne 0){throw 'Callback fixture compile failed'}
 $pending=Join-Path $common 'DriverPilot\UninstallPending'
 [IO.File]::WriteAllText($pending,'pending removal fixture',$utf8)
 Assert ((Run-Fixture 'pending') -ne 0) 'Pending removal falsely reported installer success'
 Assert (-not(Test-Path -LiteralPath $ran)) 'Backend ran before pending refusal'
 Assert ([Convert]::ToBase64String([IO.File]::ReadAllBytes($helper)) -ceq $before) 'Pending refusal overwrote recovery helper'
 [IO.File]::Delete($pending)
 # No real DriverPilot mutex is created by the fixture. An existing user GUI may
 # hold the production local mutex: omit that read-only early check for this one
 # backend-error case, rather than stopping the real application.
 $fixture=$fixture.Replace("if CheckForMutexes('Local\DriverPilot.Desktop') then",'if False then')
 [IO.File]::WriteAllText($source,$fixture,$utf8)
 & $Compiler '/Q' $source
 if($LASTEXITCODE -ne 0){throw 'Backend callback fixture compile failed'}
 Assert ((Run-Fixture 'backend-failure') -ne 0) 'Inno swallowed backend error'
 Assert (Test-Path -LiteralPath $ran) 'Fixture backend never executed; refusal tested wrong path'
 Assert ([Convert]::ToBase64String([IO.File]::ReadAllBytes($helper)) -ceq $before) 'Backend failure overwrote recovery helper'
 $backendLog=[IO.File]::ReadAllText((Join-Path $scratch 'backend-failure.log'))
 Assert ($backendLog.Contains('DP_SETUP_STDOUT_SENTINEL')) 'Setup backend stdout missing from /LOG'
 Assert ($backendLog.Contains('DP_SETUP_STDERR_SENTINEL')) 'Setup backend stderr missing from /LOG'
 'PASS setup /LOG captured backend stdout and stderr; backend exit 23 remained fatal'
 # Sabotage control in the isolated fixture only: prove the old callback really
 # reports success and overwrites the helper despite the same failing backend.
 $old=$fixture.Replace('if CurStep = ssInstall then','if CurStep = ssPostInstall then')
 Assert ($old -cne $fixture) 'Cannot construct old-event regression control'
 [IO.File]::WriteAllText($source,$old,$utf8)
 & $Compiler '/Q' $source
 if($LASTEXITCODE -ne 0){throw 'Regression control compile failed'}
 Assert ((Run-Fixture 'old-event-control') -eq 0) 'Old callback did not reproduce swallowed failure'
 Assert ([Convert]::ToBase64String([IO.File]::ReadAllBytes($helper)) -cne $before) 'Old callback control did not expose premature helper overwrite'
 'PASS regression control: old ssPostInstall swallows backend failure and overwrites recovery helper'
 'PASS actual non-elevated Inno callbacks: pending refusal before writes; backend error is fatal before helper replacement'
}finally{if(Test-Path -LiteralPath $scratch){Remove-Item -LiteralPath $scratch -Recurse -Force}}
