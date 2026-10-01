# Extract only the productive process gate. Never execute Setup or alter an installation.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source=[IO.File]::ReadAllText((Join-Path $root 'installer/Setup.ps1'))
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
if($errors){throw ($errors | Out-String)}
$gate=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Assert-SetupRuntimeIdle'},$false))
if($gate.Count -eq 1){Invoke-Expression $gate[0].Extent.Text;$invoke={Assert-SetupRuntimeIdle}}
else{
 # RED control: execute the old productive inline gate, not a reimplementation.
 $old=[regex]::Match($source,'(?m)^\s*\$running=@\(Get-Process DriverPilot[^\r\n]+\r?\n\s*if\(\$running.Count\)\{[^\r\n]+')
 if(-not $old.Success){throw 'Productive setup runtime gate missing'}
 $invoke=[scriptblock]::Create($old.Value)
}
$exe='C:\Program Files\DriverPilot\DriverPilot.exe'
function Get-Process {param($Name,$ErrorAction) if($script:queryFails){throw 'query-unavailable'};$script:snapshot}
function Get-CimInstance {
 param($ClassName,$Filter,$ErrorAction)
 if($ClassName -ne 'Win32_Process' -or $Filter -cne "Name='DriverPilot.exe'" -or $ErrorAction -ne 'Stop'){throw 'Wrong process query contract'}
 if($script:queryFails){throw 'query-unavailable'}
 $script:snapshot
}
$script:queryFails=$false
foreach($path in @($null,'',$exe,'C:\Other\DriverPilot.exe')){
 $script:snapshot=@([pscustomobject]@{Name='DriverPilot.exe';ProcessId=123;Path=$path;ExecutablePath=$path})
 $blocked=$false
 try{& $invoke}catch{if($_.Exception.Message -notlike 'DriverPilot laeuft noch*'){throw};$blocked=$true}
 if(-not $blocked){throw "Live DriverPilot escaped setup guard with path '$path'"}
}
$script:snapshot=@();& $invoke
$script:queryFails=$true;$blocked=$false
try{& $invoke}catch{if($_.Exception.Message -notlike '*query-unavailable*'){throw};$blocked=$true}
if(-not $blocked){throw 'Failed process query permitted setup'}
# Check placement as well as isolated behavior: the gate must precede the actual swap.
if($gate.Count -eq 1){
 if($source -notmatch '(?s)if\(\$WaitForPid -gt 0\).*?Assert-SetupRuntimeIdle\s+if\(Test-Path -LiteralPath \$target\).*?Publish-PackageDirectory'){throw 'Runtime guard missing before productive directory publication'}
}
'PASS productive setup guard: live known/unknown/foreign paths rejected, empty succeeds, failed query rejected.'
