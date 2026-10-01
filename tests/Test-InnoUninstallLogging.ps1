# Real production uninstall callbacks; only filesystem constants map to scratch.
# Generates a lowest-privilege fixture, with no registry entry or live payload.
# Requires Inno Setup 6.3.0+ (ExecAndLogOutput), tested with 6.7.3.
param([string]$Compiler='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not $Compiler){$Compiler=Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'}
if(-not(Test-Path -LiteralPath $Compiler)){throw 'Inno Setup compiler required for isolated callback test.'}
$base=if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}
$scratch=Join-Path $base ('DriverPilot-inno-uninstall-'+[guid]::NewGuid().ToString('N'))
$target=Join-Path $scratch 'DriverPilot.Uninstall'
$utf8=[Text.UTF8Encoding]::new($true)
function Assert($Condition,$Message){if(-not $Condition){throw $Message}}
function Run-Fixture([string]$Executable,[string]$Label){
 $log=Join-Path $scratch ($Label+'.log')
 $p=Start-Process -FilePath $Executable -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+$log+'"')) -WorkingDirectory $scratch -PassThru
 $null=$p.Handle
 try{if(-not $p.WaitForExit(60000)){$p.Kill();throw 'Inno uninstall fixture timed out'};return $p.ExitCode}finally{$p.Dispose()}
}
try{
 [void][IO.Directory]::CreateDirectory($target)
 $ran=Join-Path $scratch 'backend-ran.txt'
 $backend=Join-Path $scratch 'Uninstall.ps1'
 [IO.File]::WriteAllText($backend,('[IO.File]::WriteAllText('''+$ran.Replace("'","''")+''',''fixture uninstall backend ran''); [Console]::Out.WriteLine(''DP_UNINSTALL_STDOUT_SENTINEL''); [Console]::Error.WriteLine(''DP_UNINSTALL_STDERR_SENTINEL''); exit 23'),$utf8)
 $production=[IO.File]::ReadAllText((Join-Path $root 'installer/Installer.iss'))
 $start=$production.IndexOf('[Code]')+('[Code]'.Length)
 $end=$production.IndexOf('function PrepareToInstall(')
 Assert ($start -ge 6 -and $end -gt $start) 'Production uninstall callback section missing'
 # Preserve InitializeUninstall, usUninstall, exception handling and subprocess
 # invocation verbatim; never include the production setup backend or [Run].
 $callbacks=$production.Substring($start,$end-$start).Replace('{autopf}',$scratch.Replace("'","''"))
 $fixture=@"
[Setup]
AppId=DriverPilot.IsolatedUninstallLoggingFixture
AppName=DriverPilot isolated uninstall logging fixture
AppVersion=0.0.0
PrivilegesRequired=lowest
DefaultDirName=$target
Uninstallable=yes
UninstallFilesDir=$target
CreateUninstallRegKey=no
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=$scratch
OutputBaseFilename=fixture
Compression=none
[Files]
Source: "$backend"; DestDir: "$target"; Flags: ignoreversion
[Code]
$callbacks
"@
 $source=Join-Path $scratch 'fixture.iss';[IO.File]::WriteAllText($source,$fixture,$utf8)
 & $Compiler '/Q' $source
 if($LASTEXITCODE -ne 0){throw 'Uninstall callback fixture compile failed'}
 Assert ((Run-Fixture (Join-Path $scratch 'fixture.exe') 'fixture-setup') -eq 0) 'Scratch-only fixture preparation failed'
 Assert (-not(Test-Path -LiteralPath $ran)) 'Uninstall backend ran during fixture setup'
 $helper=Join-Path $target 'Uninstall.ps1'
 $uninstaller=Join-Path $target 'unins000.exe'
 $before=[Convert]::ToBase64String([IO.File]::ReadAllBytes($helper))
 $exitCode=Run-Fixture $uninstaller 'backend-failure'
 Assert ($exitCode -ne 0) 'Inno swallowed uninstall backend error'
 Assert (Test-Path -LiteralPath $ran) 'Uninstall backend never executed; wrong refusal path'
 Assert (Test-Path -LiteralPath $uninstaller) 'Failed backend deleted retryable uninstaller'
 Assert (Test-Path -LiteralPath (Join-Path $target 'unins000.dat')) 'Failed backend deleted uninstall metadata'
 Assert ([Convert]::ToBase64String([IO.File]::ReadAllBytes($helper)) -ceq $before) 'Failed backend deleted or changed recovery helper'
 $log=[IO.File]::ReadAllText((Join-Path $scratch 'backend-failure.log'))
 Assert ($log.Contains('DriverPilot uninstall backend failed with code 23')) 'Backend exit code not recorded'
 Assert (-not $log.Contains('DriverPilot removed.')) 'Failed uninstall reported completion'
 Assert ($log.Contains('DP_UNINSTALL_STDOUT_SENTINEL')) 'Uninstall backend stdout missing from /LOG'
 Assert ($log.Contains('DP_UNINSTALL_STDERR_SENTINEL')) 'Uninstall backend stderr missing from /LOG'
 "PASS real usUninstall callback: Inno exit $exitCode; backend exit 23; /LOG contains stdout and stderr sentinels; helper/uninstaller/metadata retained"
}finally{if(Test-Path -LiteralPath $scratch){Remove-Item -LiteralPath $scratch -Recurse -Force}}
