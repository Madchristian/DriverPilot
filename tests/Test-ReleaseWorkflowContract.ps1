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
$build=[regex]::Match($workflow,'(?ms)^  build:\n(?<job>.*?)(?=^  [a-z][a-z-]*:|\z)').Groups['job'].Value
$install=$build.IndexOf('run: ./tests/Test-Pipeline.ps1 -Installer',[StringComparison]::Ordinal)
$app=[regex]::Match($build,'(?m)^        run: ./tests/Test-Pipeline.ps1$')
$upload=$build.IndexOf('- name: Upload release files',[StringComparison]::Ordinal)
if($install -lt 0 -or -not $app.Success -or $upload -lt 0 -or $install -ge $app.Index -or $app.Index -ge $upload){
 throw 'Fresh-runner installer checks must precede stateful app self-tests; both must gate release artifact upload.'
}
Write-Output 'PASS release dependencies and fresh-runner installer-before-self-test ordering (static only).'
