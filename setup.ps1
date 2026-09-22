<#
.SYNOPSIS
    Reproduce a verified Big Oil / Oil Tycoon 2 install from a retail disc image.

.DESCRIPTION
    Converts the .bin to .iso, mounts it, drives the original installer engine
    (the 2006 bootstrap crashes on Windows 10/11, so we bypass it), then
    verifies every extracted file against CRC32 values read out of the
    installer's own manifest.

    No game content ships with this repository. You need your own disc.

.PARAMETER Bin
    Path to "Big Oil.bin" from the retail disc.

.PARAMETER Target
    Where to install. Defaults to .\game

.PARAMETER Decrypt
    Also write a plaintext copy of the 1,377 encrypted data files to .\decrypted

.EXAMPLE
    .\setup.ps1 -Bin "D:\images\Big Oil.bin"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Bin,
    [string]$Target = (Join-Path $PSScriptRoot 'game'),
    [switch]$Decrypt
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$iso  = Join-Path $root 'BigOil.iso'

function Step($m) { Write-Host "`n==> $m" -ForegroundColor Cyan }

# --- 1. build the tools -----------------------------------------------------
Step 'Building tools'
& (Join-Path $root 'tools\build.ps1')
$extract = Join-Path $root 'tools\bin\Extract.exe'
$crypt   = Join-Path $root 'tools\bin\Ot2Crypt.exe'

# --- 2. bin -> iso ----------------------------------------------------------
if (-not (Test-Path $Bin)) { throw "Disc image not found: $Bin" }
Step "Converting $([IO.Path]::GetFileName($Bin)) to ISO"
& (Join-Path $root 'tools\bin2iso.ps1') -In $Bin -Out $iso

# --- 3. mount ---------------------------------------------------------------
Step 'Mounting ISO'
$img = Mount-DiskImage -ImagePath $iso -PassThru
$drive = ($img | Get-Volume).DriveLetter
if (-not $drive) { throw 'Could not determine the mounted drive letter.' }
$D = "${drive}:"
Write-Host "    mounted at $D"

try {
    $setupExe = Join-Path $D 'Big Oil Setup Release.exe'
    $engine   = Join-Path $D 'irsetup.exe'
    $datFile  = Join-Path $D 'irsetup.dat'
    foreach ($f in @($setupExe, $engine, $datFile)) {
        if (-not (Test-Path -LiteralPath $f)) { throw "Missing on disc: $f" }
    }

    # --- 4. run the installer ------------------------------------------------
    # The bootstrap "Big Oil Setup Release.exe" access-violates on modern
    # Windows. The engine itself is fine, so point it straight at the archive.
    # 541197 (0x8420D) is where the support-file section begins.
    Step 'Running the installer engine'
    Write-Host '    A UAC prompt will appear (Windows flags irsetup.exe as an installer).'
    Write-Host "    In the wizard, set the install folder to:  $Target"
    $p = Start-Process -FilePath $engine `
        -ArgumentList "`"__IRAFN:$setupExe`" __IRAOFF:541197" -PassThru -Wait
    Write-Host "    installer exited with $($p.ExitCode)"

    if (-not (Test-Path $Target)) {
        throw "Nothing at $Target - re-run and set that path in the wizard's install-folder step."
    }

    # --- 5. verify -----------------------------------------------------------
    Step 'Verifying against the archive manifest'
    & $extract $datFile $setupExe '' --verify $Target
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'Verification found problems. Attempting repair of stored files...'
        & $extract $datFile $setupExe '' --repair $Target
        & $extract $datFile $setupExe '' --verify $Target
    }

    # --- 6. optional decrypt -------------------------------------------------
    if ($Decrypt) {
        Step 'Decrypting data files'
        & $crypt decrypt (Join-Path $Target 'core.dll') `
                         (Join-Path $Target 'DATA') `
                         (Join-Path $root 'decrypted\DATA')
    }
}
finally {
    Step 'Dismounting ISO'
    Dismount-DiskImage -ImagePath $iso | Out-Null
}

Step 'Done'
Write-Host @"
Launch the game with its working directory set to the install folder,
otherwise it scatters config and logs wherever it was started from:

    Start-Process -FilePath "$Target\game.exe" -WorkingDirectory "$Target"
"@
