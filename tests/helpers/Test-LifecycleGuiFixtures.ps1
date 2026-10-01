# Runs the compiled production reader/startup helpers against the actual lifecycle fixture.
# Never starts App/MainWindow, inventory, tasks or productive Store initialization.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$base=if($env:TMPDIR){$env:TMPDIR}else{[IO.Path]::GetTempPath()}
$scratch=Join-Path $base ('lifecycle-gui-fixtures-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
try{
 $lifecycle=Get-Content -LiteralPath (Join-Path $root 'tests/Test-UninstallLifecycle.ps1') -Raw

 # Extract actual fixture arguments; do not run the destructive lifecycle script.
 foreach($name in @('snapshot','controls')){
  $matches=[regex]::Matches($lifecycle,"(?m)^ Seed \(Join-Path \`$userData '$name\.json'\) '([^']*)'\s*$")
  if($matches.Count -ne 1){throw "Expected one explicit $name GUI fixture"}
  [IO.File]::WriteAllText((Join-Path $scratch ($name+'.json')),$matches[0].Groups[1].Value)
 }
 $sdk=(& dotnet --version)
 if($LASTEXITCODE -ne 0){throw 'dotnet SDK unavailable'}
 $major=([version]($sdk -replace '-.*$','')).Major
 if($major -lt 10){throw 'A .NET 10 or newer SDK is required'}
 foreach($configuration in @('Debug','Release')){
  & dotnet run --project (Join-Path $root 'tests/SnapshotStorage/SnapshotStorage.csproj') -c $configuration -- $scratch
  if($LASTEXITCODE -ne 0){throw "Lifecycle GUI fixture regression failed ($configuration)"}
 }
 Write-Output "PASS compiled production snapshot reader/startup regression (.NET $major; no App, Store, tray, tasks or inventory launched)"
}finally{Remove-Item -LiteralPath $scratch -Recurse -Force}
