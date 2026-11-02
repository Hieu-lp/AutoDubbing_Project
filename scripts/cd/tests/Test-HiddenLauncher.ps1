[CmdletBinding()]
param([Parameter(Mandatory)][string]$LauncherPath, [string]$Distro)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$testDirectory = Join-Path $repoRoot ('artifacts/cd/hidden launcher test ' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDirectory | Out-Null
$probePath = Join-Path $testDirectory 'probe.ps1'
$logPath = Join-Path $testDirectory 'probe.log'
@'
param([string]$Sample, [string]$Distro)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class HiddenLauncherProbe {
    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
}
"@
if ($Distro) {
    & wsl.exe -d $Distro --exec timeout 20s /bin/true
    if ($LASTEXITCODE -ne 0) { throw 'WSL probe failed.' }
}
$console = [HiddenLauncherProbe]::GetConsoleWindow()
[pscustomobject]@{
    visible_console = [HiddenLauncherProbe]::IsWindowVisible($console)
    sample = $Sample
    wsl_probe = [bool]$Distro
} | ConvertTo-Json -Compress
[Console]::Error.WriteLine('stderr capture verified')
exit 23
'@ | Set-Content -LiteralPath $probePath -Encoding UTF8

# PE subsystem 2 means Windows GUI; it does not allocate a startup console.
$binary = [IO.File]::ReadAllBytes($LauncherPath)
$peOffset = [BitConverter]::ToInt32($binary, 60)
$subsystem = [BitConverter]::ToUInt16($binary, $peOffset + 24 + 68)
if ($subsystem -ne 2) { throw 'Launcher was not compiled as WindowsApplication.' }
$arguments = '"' + $logPath + '" "' + $probePath + '" -Sample "value with spaces"'
if ($Distro) { $arguments += ' -Distro "' + $Distro + '"' }
$child = Start-Process -FilePath $LauncherPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($child.ExitCode -ne 23) { throw "Launcher lost the child exit code: $($child.ExitCode)" }
$lines = Get-Content -LiteralPath $logPath -Encoding UTF8
$jsonLine = $lines | Where-Object { $_ -match '\{"visible_console"' } | Select-Object -First 1
if (-not $jsonLine) { throw 'No child console verification was logged.' }
$probe = $jsonLine.Substring($jsonLine.IndexOf('{')) | ConvertFrom-Json
if ($probe.visible_console -or $probe.sample -ne 'value with spaces') {
    throw 'Child console is visible or spaced arguments were not preserved.'
}
if (-not ($lines | Where-Object { $_ -match 'stderr capture verified' })) { throw 'Child stderr was not captured.' }
Write-Output ('Hidden launcher PASS: GUI subsystem, child console invisible, paths/arguments with spaces, stdout/stderr capture, exit 23 preserved, WSL=' + [bool]$Distro)
