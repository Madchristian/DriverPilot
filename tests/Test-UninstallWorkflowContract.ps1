# Safe static checks only: never imports signing keys or executes the workflow.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$path=Join-Path $root '.github/workflows/uninstall-validation.yml'
if(-not(Test-Path -LiteralPath $path)){throw 'Manual uninstall validation workflow is missing.'}
$workflow=(Get-Content -LiteralPath $path -Raw).Replace("`r`n","`n")
function Require([string]$Pattern,[string]$Message){if($workflow -notmatch $Pattern){throw $Message}}
function Forbid([string]$Pattern,[string]$Message){if($workflow -match $Pattern){throw $Message}}
Require '(?m)^  workflow_dispatch:' 'Manual dispatch trigger required'
Forbid '(?m)^  (push|pull_request|pull_request_target|workflow_call|schedule):' 'Automatic or reusable triggers forbidden'
Require '(?m)^  contents: read$' 'Read-only repository permission required'
Forbid '(?m)^\s+\w[\w-]*: write$' 'Write permissions forbidden'
Require "github.repository == 'Madchristian/DriverPilot'" 'Own repository guard required'
Require "github.ref != 'refs/heads/main'" 'Branch-only validation must not use main'
Require 'environment: uninstall-validation' 'Approval environment required'
Require 'ref: \$\{\{ inputs.reviewed_sha \}\}' 'Checkout must bind reviewed SHA'
Require 'persist-credentials: false' 'Checkout credentials must not persist'
Require '\$env:REVIEWED_SHA -cne \$env:GITHUB_SHA' 'Dispatch SHA must equal reviewed SHA'
Require '\$actual -cne \$env:REVIEWED_SHA' 'Checked-out SHA must equal reviewed SHA'
Forbid '(?im)\$env:GITHUB_(REF|SHA|EVENT_NAME)\s*=' 'Never spoof signing helper gates'
Forbid 'gh release|softprops/action-gh-release|Export-PfxCertificate|ExportParameters\(' 'No publishing or private-key export'
Require 'run: ./build/Initialize-CISigning.ps1' 'Use existing import helper without bypass'
Require 'run: ./build/Build.ps1 -Sign' 'Use signed app build helper'
Require 'run: ./build/Build-Release.ps1 -Sign' 'Use signed packaging helper'
foreach($test in @('Test-Pipeline','Test-Signing','Test-UninstallLifecycle')){
 Require ('powershell -NoProfile -ExecutionPolicy Bypass -File ./tests/'+$test+'\.ps1') "Separate PowerShell process required: $test"
}
Require 'powershell -NoProfile -ExecutionPolicy Bypass -File ./tests/Test-UninstallReceiptComposition\.ps1 -RequireProtectedFixtures\s+if\(\$LASTEXITCODE -ne 0\)\{throw' 'Composed receipt regression with real protected ACLs must gate CI'
Require '(?s)name: Remove signing key and temporary trust\s+if: always\(\)\s+run: ./build/Remove-CISigning.ps1' 'Always cleanup signing state'
Require '(?s)name: Upload validation diagnostics\s+if: always\(\)\s+uses: actions/upload-artifact@[0-9a-f]{40}' 'Always upload diagnostics using pinned action'
Require 'artifacts/uninstall-tests/\*\*' 'Lifecycle evidence missing'
Forbid '(?m)^\s+artifacts/\*\*|artifacts/dist/\*|include-hidden-files: true' 'No broad artifact upload'
# Parse every inline run block as PowerShell without evaluating it.
$blocks=[regex]::Matches($workflow,'(?m)^        run: \|\r?\n((?:          .*\r?\n|\r?\n)+)')
if($blocks.Count -lt 6){throw 'Expected inline identity/build/test scripts are missing.'}
foreach($block in $blocks){
 $script=[regex]::Replace($block.Groups[1].Value,'(?m)^          ','')
 $tokens=$null;$errors=$null
 [void][Management.Automation.Language.Parser]::ParseInput($script,[ref]$tokens,[ref]$errors)
 if($errors){throw ($errors | Out-String)}
}
Write-Output 'PASS manual uninstall workflow security contract and inline PowerShell parse (static; no CI or signing executed).'
