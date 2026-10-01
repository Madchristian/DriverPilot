# Static checks only: no signing, dispatch, installation or publication.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$workflow=(Get-Content -LiteralPath (Join-Path $root '.github/workflows/release.yml') -Raw).Replace("`r`n","`n")
$release=[regex]::Match($workflow,'(?ms)^  release:\n(?<job>.*?)(?=^  [a-z][a-z-]*:|\z)').Groups['job'].Value
$needs=[regex]::Match($release,'(?m)^    needs: \[(?<jobs>[^\]]+)\]$')
if(-not $needs.Success){throw 'Release requires an explicit needs list.'}
$jobs=@($needs.Groups['jobs'].Value.Split(',') | ForEach-Object {$_.Trim()})
foreach($required in @('build','app-upgrade','contract')){
 if($jobs -cnotcontains $required){throw "Release publication must depend on $required."}
 if($workflow -notmatch ('(?m)^  '+[regex]::Escape($required)+':$')){throw "Required job missing: $required"}
}
if($release -match '(?m)^    if:.*(always\(|failure\(|cancelled\()'){throw 'Release must not override failed dependencies.'}
Write-Output 'PASS release publication depends on build, app-upgrade and contract (static only).'
