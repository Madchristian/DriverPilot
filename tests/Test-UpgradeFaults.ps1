# Safe local helper test: scratch files only; never executes Setup or changes certificates.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $PSScriptRoot 'UpgradeFaults.cs')
Add-Type -Path (Join-Path $root 'src/DriverPilot/Security/PackageTrust.cs')
$temporary=if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}
$base=Join-Path $temporary ('DriverPilot-copy-lock-'+[guid]::NewGuid().ToString('N'))
$source=Join-Path $base 'source';$data=Join-Path $base 'data'
New-Item -ItemType Directory -Path $source,$data | Out-Null
try {
 $first=Join-Path $source 'A-copy-window.txt'
 $f=[IO.File]::Create($first);try{$f.SetLength(128MB)}finally{$f.Dispose()}
 [IO.File]::WriteAllText((Join-Path $source 'Z-last.txt'),'last file')
 $package=[DriverPilot.Security.VerifiedPackage]::new()
 $package.Files=[Collections.Generic.Dictionary[string,string]]::new()
 $package.Files.Add('A-copy-window.txt','unused');$package.Files.Add('Z-last.txt','unused')
 # Isolated CopyVerified unit fixture, not fed to any install/authentication path.
 $flags=[Reflection.BindingFlags]'Instance,NonPublic'
 $package.GetType().GetField('ManifestBytes',$flags).SetValue($package,[byte[]](123,125))
 $package.GetType().GetField('SignatureBytes',$flags).SetValue($package,[byte[]](1,2))
 $control=Join-Path $data 'control';[void][IO.Directory]::CreateDirectory($control)
 [DriverPilot.Security.PackageTrust]::CopyVerified($source,$control,$package)
 'PASS control: real CopyVerified succeeds without fault injector'
 for($i=0;$i -lt 5;$i++) {
  $stage=Join-Path $data ('AppStaging-'+$i)
  $lock=[UpgradeCopyLock]::new($data,(Join-Path $source 'Z-last.txt'))
  try {
   [void][IO.Directory]::CreateDirectory($stage)
   $failed=$false
   try{[DriverPilot.Security.PackageTrust]::CopyVerified($source,$stage,$package)}catch{
    if($_.Exception.ToString() -notmatch 'another process|anderen Prozess|being used'){throw}
    $failed=$true
   }
   if(-not $failed -or -not $lock.Acquired.WaitOne(0)){throw 'Expected real sharing violation not observed'}
   "PASS real CopyVerified sharing violation $i"
  } finally {$lock.Dispose()}
 }
 # Read function definitions only. Never dot-source the integration installation body.
 $tokens=$null;$errors=$null
 $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Test-AppUpgrade.ps1'),[ref]$tokens,[ref]$errors)
 if($errors){throw ($errors|Out-String)}
 foreach($function in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Run-PowerShell'},$false)){Invoke-Expression $function.Extent.Text}
 $evidence=Join-Path $base 'wrapper';[void][IO.Directory]::CreateDirectory($evidence)
 $windowsPowerShell=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
 $ok=Run-PowerShell 'success' "Write-Output 'wrapper marker'" 10
 if($null -eq $ok.ExitCode -or $ok.ExitCode -ne 0 -or $ok.Text -notmatch 'wrapper marker'){throw 'Subprocess success/exit-code capture failed'}
 $bad=Run-PowerShell 'failure' "throw 'expected rejection marker'" 10
 if($null -eq $bad.ExitCode -or $bad.ExitCode -eq 0 -or $bad.Text -notmatch 'expected rejection marker'){throw 'Subprocess failure capture failed'}
 'PASS real child exit codes and success/failure logs'
 $p=Start-Process -FilePath $windowsPowerShell -ArgumentList '-NoProfile -Command Start-Sleep -Seconds 20' -PassThru
 try{[UpgradeProcessHold]::Suspend($p);if($p.HasExited){throw 'Process hold failed'};'PASS native process hold'}finally{$p.Kill();$p.WaitForExit();$p.Dispose()}
}finally{Remove-Item -LiteralPath $base -Recurse -Force}
