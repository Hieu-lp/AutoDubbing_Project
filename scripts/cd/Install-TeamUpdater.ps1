[CmdletBinding()]
param(
    [string]$Distro,
    [string]$Project = 'autodubbing-team',
    [string]$ConfigPath,
    [string]$EnvFile,
    [string]$RegistryPrefix = 'ghcr.io/hieu-lp',
    [string]$WebUrl = 'http://127.0.0.1:18081',
    [ValidateRange(2, 1440)][int]$IntervalMinutes = 5,
    [string]$TaskName,
    [switch]$RepairLauncher,
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $ConfigPath) { $ConfigPath = Join-Path $repoRoot 'artifacts/cd/host.json' }
$ConfigPath = [IO.Path]::GetFullPath($ConfigPath)
if (-not $TaskName) { $TaskName = "AutoDubbing-CD-$Project" }
if ($Remove) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Output "Removed scheduled updater $TaskName; Docker and database were not changed."
    exit 0
}
$invokePath = Join-Path $PSScriptRoot 'Invoke-TeamUpdate.ps1'
if (-not $RepairLauncher) {
    $parameters = @{ Command = 'verify'; ConfigPath = $ConfigPath }
    if ($Distro) { $parameters.Distro = $Distro }
    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        $parameters.Command = 'init'
        $parameters.Project = $Project
        $parameters.RegistryPrefix = $RegistryPrefix
        $parameters.WebUrl = $WebUrl
        if ($EnvFile) { $parameters.EnvFile = $EnvFile }
    }
    # Child process: Invoke-TeamUpdate uses exit to preserve the real updater exit code.
    $setupArgs = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $invokePath)
    foreach ($entry in $parameters.GetEnumerator()) {
        $setupArgs += ('-' + $entry.Key)
        $setupArgs += [string]$entry.Value
    }
    & powershell.exe @setupArgs
    if ($LASTEXITCODE -ne 0) { throw 'Host adoption/verification failed; no scheduled task was installed.' }
}
$adoptedHost = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($adoptedHost.project -ne $Project) {
    throw 'This config belongs to another Compose project. Select its project or use a separate ConfigPath.'
}
function Quote-TaskArgument([string]$Value) {
    if ($Value.Contains('"')) { throw 'A task argument contains an unsupported quote.' }
    return '"' + $Value + '"'
}
$launcherSource = Join-Path $PSScriptRoot 'RunTeamUpdateHidden.cs'
$launcherDigest = (Get-FileHash -LiteralPath $launcherSource -Algorithm SHA256).Hash.Substring(0, 16)
$launcherDirectory = Split-Path -Parent $ConfigPath
$launcherPath = Join-Path $launcherDirectory "RunTeamUpdateHidden-$launcherDigest.exe"
if (-not (Test-Path -LiteralPath $launcherPath)) {
    Add-Type -Path $launcherSource -OutputAssembly $launcherPath -OutputType WindowsApplication
}
$launcherLog = Join-Path $launcherDirectory 'launcher.log'
$taskArgs = (Quote-TaskArgument $launcherLog) + ' ' + (Quote-TaskArgument $invokePath) +
    ' -Command check -ConfigPath ' + (Quote-TaskArgument $ConfigPath)
if ($Distro) { $taskArgs += ' -Distro ' + (Quote-TaskArgument $Distro) }
$taskAction = New-ScheduledTaskAction -Execute $launcherPath -Argument $taskArgs -WorkingDirectory $repoRoot
if ($RepairLauncher) {
    # Preserve trigger, user, enabled/disabled state and timeout; don't invoke Docker.
    Set-ScheduledTask -TaskName $TaskName -Action $taskAction | Out-Null
    Write-Output "Repaired hidden launcher for $TaskName; existing schedule and enabled state preserved."
    exit 0
}
$taskTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes)
$taskSettings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 35) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName $TaskName -Action $taskAction -Trigger $taskTrigger -Settings $taskSettings -Principal $taskPrincipal -Description 'Update only AutoDubbing API/web from the atomic GHCR release; preserve SQL and local configuration.' -Force | Out-Null
Write-Output "Installed $TaskName; checks every $IntervalMinutes minutes while this Windows user is signed in."
