<#
.SYNOPSIS
    Fetches DXVK and stages it as a mod.

.DESCRIPTION
    DXVK translates Direct3D 9 to Vulkan. It is third-party (zlib licence,
    github.com/doitsujin/dxvk) and its binary is deliberately NOT committed to
    this repository, so this downloads it on demand - the same arrangement as
    the generated animation mods.

    game.exe is a 32-bit binary, so the x32 build is the one that matters.
    The architecture is checked before the file is staged.

.PARAMETER Version
    A specific tag such as 3.1.1. Defaults to the latest release.

.PARAMETER Arch
    x32 (default, correct for this game) or x64.

.EXAMPLE
    .\tools\get-dxvk.ps1
    .\tools\bin\Ot2Mod.exe apply dxvk
#>
[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('x32', 'x64')][string]$Arch = 'x32'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$modDir = Join-Path $repo 'mods\dxvk'

function Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }

if (-not $Version) {
    Step 'Looking up the latest DXVK release'
    $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/doitsujin/dxvk/releases/latest' `
                             -Headers @{ 'User-Agent' = 'ot2-revival' }
    $Version = $rel.tag_name -replace '^v', ''
}
Write-Host "    DXVK $Version ($Arch)"

$url = "https://github.com/doitsujin/dxvk/releases/download/v$Version/dxvk-$Version.tar.gz"
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("dxvk-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

try {
    Step 'Downloading'
    $tar = Join-Path $tmp 'dxvk.tar.gz'
    Invoke-WebRequest -Uri $url -OutFile $tar -UseBasicParsing
    Write-Host ("    {0:N0} bytes" -f (Get-Item $tar).Length)

    Step 'Extracting d3d9.dll'
    & tar -xzf $tar -C $tmp "dxvk-$Version/$Arch/d3d9.dll"
    $dll = Join-Path $tmp "dxvk-$Version\$Arch\d3d9.dll"
    if (-not (Test-Path $dll)) { throw "d3d9.dll not found in the archive for $Arch" }

    # Confirm the architecture rather than trusting the folder name: loading a
    # 64-bit dll into a 32-bit process fails in confusing ways.
    $fs = [IO.File]::OpenRead($dll)
    $br = New-Object IO.BinaryReader($fs)
    $fs.Position = 0x3C
    $fs.Position = $br.ReadUInt32() + 4
    $machine = $br.ReadUInt16()
    $fs.Close()
    $expected = if ($Arch -eq 'x32') { 0x014C } else { 0x8664 }
    if ($machine -ne $expected) {
        throw ("Architecture mismatch: expected 0x{0:X4}, got 0x{1:X4}" -f $expected, $machine)
    }
    Write-Host ("    machine 0x{0:X4} - correct" -f $machine)

    New-Item -ItemType Directory -Force -Path $modDir | Out-Null
    Copy-Item $dll (Join-Path $modDir 'd3d9.dll') -Force
    Write-Host "    staged in mods\dxvk\"
}
finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

Step 'Done'
Write-Host @"
Apply it with:

    tools\bin\Ot2Mod.exe apply dxvk

d3d9.dll does not exist in the install, so it is recorded as absent and
reverting deletes it - putting the game back on the system D3D9.
"@
