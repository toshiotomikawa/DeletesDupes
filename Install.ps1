#requires -Version 5.1
<#
.SYNOPSIS
    Installs or removes the "Delete duplicates" Explorer folder-background verb.

.DESCRIPTION
    Machine-wide installation copies DeleteDuplicates.exe to
    %ProgramFiles%\DeleteDuplicates and registers a static verb under
    HKLM\SOFTWARE\Classes\Directory\Background\shell. Elevation is requested
    only for installation; the tool itself always runs unelevated.

    -PerUser installs to %LOCALAPPDATA%\Programs\DeleteDuplicates and registers
    under HKCU\Software\Classes instead, which needs no elevation at all.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -PerUser

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -Uninstall
#>
param(
    [switch]$Uninstall,
    [switch]$PerUser,
    [switch]$Elevated
)

$ErrorActionPreference = 'Stop'

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not $PerUser -and -not (Test-Administrator)) {
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath),
        '-Elevated'
    )

    if ($Uninstall) { $arguments += '-Uninstall' }

    Start-Process -FilePath $powershell -Verb RunAs -ArgumentList $arguments
    exit
}

if ($PerUser) {
    $installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\DeleteDuplicates'
    $classesRoot = 'Registry::HKEY_CURRENT_USER\Software\Classes'
}
else {
    $installDirectory = Join-Path $env:ProgramFiles 'DeleteDuplicates'
    $classesRoot = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes'
}

$installedExe = Join-Path $installDirectory 'DeleteDuplicates.exe'
$verbKey = "$classesRoot\Directory\Background\shell\DeleteDuplicates"

if ($Uninstall) {
    Remove-Item -LiteralPath $verbKey -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $installedExe -PathType Leaf) {
        Remove-Item -LiteralPath $installedExe -Force
    }
    if ((Test-Path -LiteralPath $installDirectory -PathType Container) -and
        -not (Get-ChildItem -LiteralPath $installDirectory -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $installDirectory -Force
    }

    Write-Host "Removed the Delete duplicates verb and $installDirectory."
    exit
}

# The executable is looked for beside this script first, then in the usual
# build output folder so the script works straight from the repository.
$sourceCandidates = @(
    (Join-Path $PSScriptRoot 'DeleteDuplicates.exe'),
    (Join-Path $PSScriptRoot 'bin\Release\DeleteDuplicates.exe')
)

$sourceExe = $sourceCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1

if (-not $sourceExe) {
    throw "DeleteDuplicates.exe was not found beside Install.ps1 or in bin\Release."
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $installedExe -Force

New-Item -Path $verbKey -Force | Out-Null
New-Item -Path "$verbKey\command" -Force | Out-Null

New-ItemProperty -Path $verbKey -Name 'MUIVerb' -PropertyType String `
    -Value 'Delete duplicates' -Force | Out-Null

New-ItemProperty -Path $verbKey -Name 'Icon' -PropertyType String `
    -Value "$installedExe,0" -Force | Out-Null

New-ItemProperty -Path $verbKey -Name 'Position' -PropertyType String `
    -Value 'Bottom' -Force | Out-Null

New-ItemProperty -Path $verbKey -Name 'NoWorkingDirectory' -PropertyType String `
    -Value '' -Force | Out-Null

# %V is the folder-background substitution. It is quoted so that folder names
# containing spaces arrive as a single argument.
$command = '"{0}" "%V"' -f $installedExe
Set-Item -Path "$verbKey\command" -Value $command

Write-Host "Installed to $installedExe"
Write-Host "Registered $verbKey"
Write-Host ""
Write-Host "Right-click the empty area inside a folder to use it."
Write-Host "On Windows 11 the entry appears under 'Show more options'."
