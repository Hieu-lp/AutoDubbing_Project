[CmdletBinding()]
param(
    [ValidateSet('init', 'check', 'verify', 'status', 'release', 'exercise', 'start')]
    [string]$Command = 'check',
    [string]$Distro,
    [string]$Project = 'autodubbing-team',
    [string]$ConfigPath,
    [string]$EnvFile,
    [string]$RegistryPrefix = 'ghcr.io/hieu-lp',
    [string]$WebUrl = 'http://127.0.0.1:18081',
    [switch]$RetryFailed
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$updaterPath = Join-Path $PSScriptRoot 'updater.py'
if (-not $ConfigPath) { $ConfigPath = Join-Path $repoRoot 'artifacts/cd/host.json' }
$ConfigPath = [IO.Path]::GetFullPath($ConfigPath)

function Convert-ToLinuxPath([string]$Value) {
    $converted = & wsl.exe -d $Distro --exec wslpath -a -u $Value
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the path in the selected WSL distribution.' }
    return ($converted | Out-String).Trim()
}

if ($Distro) {
    $scriptArgument = Convert-ToLinuxPath $updaterPath
    $configArgument = Convert-ToLinuxPath $ConfigPath
    $envArgument = if ($EnvFile) { Convert-ToLinuxPath ([IO.Path]::GetFullPath($EnvFile)) } else { $null }
} else {
    $scriptArgument = $updaterPath
    $configArgument = $ConfigPath
    $envArgument = $EnvFile
}
$updaterArgs = @($scriptArgument, $Command, '--config', $configArgument)
if ($Command -eq 'init') {
    $updaterArgs += @('--project', $Project, '--registry-prefix', $RegistryPrefix, '--web-url', $WebUrl)
    if ($envArgument) { $updaterArgs += @('--env-file', $envArgument) }
}
if ($RetryFailed) { $updaterArgs += '--retry-failed' }
if ($Distro) {
    # One noninteractive invocation, bounded even when Docker is not responding.
    & wsl.exe -d $Distro --exec timeout --signal=TERM --kill-after=10s 1800s python3 @updaterArgs
} else {
    & python.exe @updaterArgs
}
exit $LASTEXITCODE
