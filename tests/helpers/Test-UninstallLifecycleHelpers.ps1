# Safe local unit tests: no application, installer, registry or desktop interaction.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$helper=Join-Path $PSScriptRoot 'UninstallLifecycleHelpers.ps1'
if(Test-Path $helper){. $helper}
if(-not(Get-Command Get-UninstallChoices -ErrorAction SilentlyContinue)){throw 'FAIL: independent uninstall choice generator missing'}
$cases=@(Get-UninstallChoices)
if($cases.Count -ne 8){throw 'Expected eight choices'}
$expected=@('','/REMOVESETTINGS','/REMOVELOGS','/REMOVESETTINGS /REMOVELOGS','/REMOVEBACKUPS','/REMOVESETTINGS /REMOVEBACKUPS','/REMOVELOGS /REMOVEBACKUPS','/REMOVESETTINGS /REMOVELOGS /REMOVEBACKUPS')
for($i=0;$i -lt 8;$i++){
 if(($cases[$i].Flags -join ' ') -cne $expected[$i]){throw "Wrong flags for case $i"}
 if($cases[$i].Settings -ne [bool]($i -band 1) -or $cases[$i].Logs -ne [bool]($i -band 2) -or $cases[$i].Backups -ne [bool]($i -band 4)){throw "Wrong selection for case $i"}
}
Write-Output 'PASS eight independent selection/flag combinations'
if(-not(Get-Command Select-LifecycleQuitItem -ErrorAction SilentlyContinue)){throw 'FAIL: PID-bound tray Quit selector missing'}
$valid=[pscustomobject]@{Name='Beenden';ProcessId=123;Enabled=$true;Element='correct'}
$other=[pscustomobject]@{Name='Beenden';ProcessId=456;Enabled=$true;Element='wrong process'}
$scan=[pscustomobject]@{Name='Jetzt pr'+[char]0xfc+'fen';ProcessId=123;Enabled=$true;Element='scan'}
if((Select-LifecycleQuitItem @($other,$scan,$valid) 123) -ne 'correct'){throw 'Quit must bind to exact PID and label'}
foreach($items in @(@($other,$scan),@($valid,$valid),@([pscustomobject]@{Name='Beenden';ProcessId=123;Enabled=$false;Element='disabled'}))){
 $rejected=$false
 try{$null=Select-LifecycleQuitItem $items 123}catch{$rejected=$true}
 if(-not $rejected){throw 'Missing, ambiguous or disabled Quit must fail closed'}
}
Write-Output 'PASS tray Quit selector: PID, exact label, uniqueness, enabled'
if(-not(Get-Command Assert-LifecycleDataOutcome -ErrorAction SilentlyContinue)){throw 'FAIL: category retention verifier missing'}
$base=if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}
$scratch=Join-Path $base ('uninstall-helper-unit-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
try{
 # Execute the productive lifecycle fixture's parser, without invoking any installer.
 $lifecycle=Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Test-UninstallLifecycle.ps1') -Raw
 $parseOwner=[regex]::Match($lifecycle,'(?m)^\s*\$wrongOwner=.+ConvertFrom-Json\s*$').Value.Trim()
 if(-not $parseOwner){throw 'Missing lifecycle owner parser'}
 $ownerPath=Join-Path $scratch 'owner.json'
 foreach($bom in @($false,$true)){
  [IO.File]::WriteAllText($ownerPath,'{"OwnerSid":"synthetic-owner"}',[Text.UTF8Encoding]::new($bom))
  $ownerBytes=[IO.File]::ReadAllBytes($ownerPath)
  . ([scriptblock]::Create($parseOwner))
  if($wrongOwner.OwnerSid -cne 'synthetic-owner'){throw 'Owner JSON parse changed value'}
  $after=[IO.File]::ReadAllBytes($ownerPath)
  if([Convert]::ToBase64String($after) -cne [Convert]::ToBase64String($ownerBytes)){throw 'Owner parse changed original bytes'}
 }
 Remove-Item -LiteralPath $ownerPath
 Write-Output 'PASS lifecycle owner parser handles UTF-8 with/without BOM without changing original bytes'
 foreach($choice in $cases){
  $hashes=@{};$categories=@{}
  foreach($category in @('Settings','Logs','Backups','Unselected')){
   $path=Join-Path $scratch $category
   [IO.File]::WriteAllBytes($path,[byte[]](0,1,127,255))
   $hashes[$path]=(Get-FileHash $path -Algorithm SHA256).Hash;$categories[$path]=$category
   if($category -ne 'Unselected' -and $choice.$category){Remove-Item -LiteralPath $path}
  }
  Assert-LifecycleDataOutcome $choice $hashes $categories
 }
 $path=Join-Path $scratch 'Unselected'
 [IO.File]::WriteAllText($path,'changed bytes')
 $rejected=$false;try{Assert-LifecycleDataOutcome $cases[7] $hashes $categories}catch{$rejected=$true}
 if(-not $rejected){throw 'Changed retained bytes accepted'}
 Remove-Item -LiteralPath $path
 $rejected=$false;try{Assert-LifecycleDataOutcome $cases[7] $hashes $categories}catch{$rejected=$true}
 if(-not $rejected){throw 'Missing retained bytes accepted'}
 [IO.File]::WriteAllBytes($path,[byte[]](0,1,127,255))
 [IO.File]::WriteAllText((Join-Path $scratch 'Settings'),'survivor')
 $rejected=$false;try{Assert-LifecycleDataOutcome $cases[7] $hashes $categories}catch{$rejected=$true}
 if(-not $rejected){throw 'Selected survivor accepted'}
 Write-Output 'PASS retention verifier: all eight filesystem outcomes; changed/missing retained bytes and selected survivor rejected'
}finally{Remove-Item -LiteralPath $scratch -Recurse -Force}