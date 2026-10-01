# Run the real static contract against isolated LF/CRLF workflows and a write-permission sabotage.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$base=if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}
$scratch=Join-Path $base ('workflow-lines-'+[guid]::NewGuid().ToString('N'))
$utf8=[Text.UTF8Encoding]::new($false)
try{
 [void][IO.Directory]::CreateDirectory((Join-Path $scratch 'tests'))
 [void][IO.Directory]::CreateDirectory((Join-Path $scratch '.github/workflows'))
 $test=Join-Path $scratch 'tests/Test-UninstallWorkflowContract.ps1'
 Copy-Item -LiteralPath (Join-Path $root 'tests/Test-UninstallWorkflowContract.ps1') -Destination $test
 $workflow=[IO.File]::ReadAllText((Join-Path $root '.github/workflows/uninstall-validation.yml')).Replace("`r`n","`n")
 foreach($ending in @("`n","`r`n")){
  foreach($sabotage in @($false,$true)){
   $candidate=$workflow
   if($sabotage){$candidate=$candidate.Replace('  contents: read',"  contents: read`n  issues: write")}
   [IO.File]::WriteAllText((Join-Path $scratch '.github/workflows/uninstall-validation.yml'),$candidate.Replace("`n",$ending),$utf8)
   $ErrorActionPreference='Continue'
   try{$output=& "$PSHOME/powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $test 2>&1;$code=$LASTEXITCODE}
   finally{$ErrorActionPreference='Stop'}
   if(-not $sabotage -and $code -ne 0){throw "Valid workflow rejected (line ending length $($ending.Length)): $output"}
   if($sabotage -and ($code -eq 0 -or "$output" -notlike '*Write permissions forbidden*')){throw 'Write-permission sabotage was not specifically rejected'}
  }
 }
 'PASS actual workflow contract accepts LF/CRLF and rejects write permissions for both.'
}finally{if(Test-Path -LiteralPath $scratch){Remove-Item -LiteralPath $scratch -Recurse -Force}}
