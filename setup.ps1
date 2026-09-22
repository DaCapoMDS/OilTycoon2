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

.PARAMETER VerifyOnly
    Skip installing. Mount the image and check an existing install against the
    manifest. Verification needs irsetup.dat and the setup exe, which live
    inside the disc image, so the image is still required.

.EXAMPLE
    .\setup.ps1 -Bin "D:\images\Big Oil.bin"

.EXAMPLE
    .\setup.ps1 -Bin "D:\images\Big Oil.bin" -Target "C:\Games\Big Oil" -VerifyOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Bin,
    [string]$Target = (Join-Path $PSScriptRoot 'game'),
    [switch]$Decrypt,
    [switch]$VerifyOnly
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
$expected = [math]::Floor((Get-Item -LiteralPath $Bin).Length / 2352) * 2048
if ((Test-Path $iso) -and ((Get-Item -LiteralPath $iso).Length -eq $expected)) {
    Step 'Reusing existing ISO'
    Write-Host ("    {0:N0} bytes" -f $expected)
} else {
    Step "Converting $([IO.Path]::GetFileName($Bin)) to ISO"
    & (Join-Path $root 'tools\bin2iso.ps1') -In $Bin -Out $iso
}

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
    if ($VerifyOnly) {
        Step 'Verify only - skipping install'
        if (-not (Test-Path -LiteralPath (Join-Path $Target 'game.exe'))) {
            throw "No game.exe at $Target - pass -Target with the install folder."
        }
    }
    else {
    Step 'Running the installer engine'
    Write-Host '    A UAC prompt will appear (Windows flags irsetup.exe as an installer).'
    Write-Host "    Install anywhere you like - this script finds it afterwards."
    Write-Host "    (suggested: $Target)"
    $p = Start-Process -FilePath $engine `
        -ArgumentList "`"__IRAFN:$setupExe`" __IRAOFF:541197" -PassThru -Wait
    Write-Host "    installer exited with $($p.ExitCode)"

    # Where did it actually go? The wizard lets the user pick, and its own
    # default is C:\Tri Synergy\Big Oil - so never assume $Target.
    $install = $null
    if (Test-Path -LiteralPath (Join-Path $Target 'game.exe')) {
        $install = $Target
    }
    else {
        # The installer logs every file it writes to %WINDIR%\<product> Setup Log.txt
        $log = Join-Path $env:WINDIR 'Big Oil Setup Log.txt'
        if (Test-Path -LiteralPath $log) {
            # -Last: the log survives across runs, so trust the newest entry
            $line = Select-String -LiteralPath $log -Pattern 'Archive file:\s*(.+\\game\.exe)' |
                    Select-Object -Last 1
            if ($line) {
                $candidate = Split-Path $line.Matches[0].Groups[1].Value.Trim()
                if (Test-Path -LiteralPath (Join-Path $candidate 'game.exe')) { $install = $candidate }
            }
        }
    }
    if (-not $install) {
        throw @"
Could not find the installed game.
Checked: $Target
     and: $(Join-Path $env:WINDIR 'Big Oil Setup Log.txt')
If you installed somewhere else, re-run with -Target "<that folder>".
"@
    }
    if ($install -ne $Target) {
        Write-Host "    installed to $install (not the suggested path) - continuing there" -ForegroundColor Yellow
    }
    $Target = $install
    }   # end install branch

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
