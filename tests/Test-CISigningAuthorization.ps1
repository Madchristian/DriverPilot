# Pure authorization tests: never import a key, alter trust, or spoof process environment.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'build/Initialize-CISigning.ps1'),[ref]$tokens,[ref]$errors)
if($errors){throw 'Signing initializer syntax invalid'}
$function=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Test-DriverPilotCISigningAuthorization'},$false))
if($function.Count -ne 1){throw 'Missing independently testable CI signing authorization gate'}
Invoke-Expression $function[0].Extent.Text
$sha='a'*40
$base=@{GITHUB_ACTIONS='true';RUNNER_ENVIRONMENT='github-hosted';GITHUB_EVENT_NAME='workflow_dispatch';GITHUB_REF='refs/heads/reviewed-uninstall';GITHUB_REPOSITORY='Madchristian/DriverPilot';GITHUB_SHA=$sha;GITHUB_WORKFLOW_REF='Madchristian/DriverPilot/.github/workflows/uninstall-validation.yml@refs/heads/reviewed-uninstall';DRIVERPILOT_SIGN_UNINSTALL_TEST='true';DRIVERPILOT_REVIEWED_SHA=$sha;DRIVERPILOT_SIGN_RELEASE='false'}
function Assert-Allowed($Environment,[string]$Head,[bool]$Expected,[string]$Label){
 $actual=Test-DriverPilotCISigningAuthorization $Environment $Head
 if($actual -ne $Expected){throw "Signing authorization mismatch: $Label"}
}
Assert-Allowed $base $sha $true 'reviewed manual branch'
foreach($change in @(
 @{GITHUB_ACTIONS='false'},@{RUNNER_ENVIRONMENT='self-hosted'},@{GITHUB_EVENT_NAME='pull_request'},
 @{GITHUB_EVENT_NAME='pull_request_target'},@{GITHUB_EVENT_NAME='push'},@{GITHUB_REF='refs/pull/1/merge'},
 @{GITHUB_REPOSITORY='other/DriverPilot'},@{GITHUB_WORKFLOW_REF='Madchristian/DriverPilot/.github/workflows/release.yml@refs/heads/reviewed-uninstall'},
 @{DRIVERPILOT_SIGN_UNINSTALL_TEST='false'},@{DRIVERPILOT_SIGN_RELEASE='true'},
 @{DRIVERPILOT_REVIEWED_SHA=''},@{DRIVERPILOT_REVIEWED_SHA=('b'*40)},@{DRIVERPILOT_REVIEWED_SHA=('a'*39)},
 @{GITHUB_SHA=('A'*40);DRIVERPILOT_REVIEWED_SHA=('A'*40)},@{GITHUB_REPOSITORY='';GITHUB_WORKFLOW_REF='/.github/workflows/uninstall-validation.yml@refs/heads/reviewed-uninstall'}
)){
 $candidate=$base.Clone();foreach($key in $change.Keys){$candidate[$key]=$change[$key]}
 Assert-Allowed $candidate $sha $false ($change | ConvertTo-Json -Compress)
}
Assert-Allowed $base ('b'*40) $false 'checkout differs from approved SHA'
Assert-Allowed $base '' $false 'checkout unavailable'
$legacy=$base.Clone();$legacy.DRIVERPILOT_SIGN_UNINSTALL_TEST='false';$legacy.GITHUB_REF='refs/heads/main';$legacy.DRIVERPILOT_SIGN_RELEASE='true'
Assert-Allowed $legacy '' $true 'existing manual main build'
$legacy.GITHUB_EVENT_NAME='push';$legacy.GITHUB_REF='refs/tags/v0.3.4'
Assert-Allowed $legacy '' $true 'existing release tag'
$legacy.RUNNER_ENVIRONMENT='self-hosted'
Assert-Allowed $legacy '' $false 'legacy modes still require disposable runner'
'PASS isolated CI signing authorization: exact branch SHA/workflow, hostile events/refs, legacy modes; no key or trust access.'
