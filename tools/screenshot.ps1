<#
.SYNOPSIS
    Captures the screen to a PNG.

.DESCRIPTION
    Used while tuning the renderer: launch the game, wait, grab the frame, and
    read the on-screen profiler and DXVK HUD without needing someone to take a
    picture each time.

.PARAMETER Out
    Output path. Defaults to a timestamped file in the scratch folder.

.PARAMETER Delay
    Seconds to wait before capturing.
#>
[CmdletBinding()]
param(
    [string]$Out,
    [int]$Delay = 0
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if ($Delay -gt 0) { Start-Sleep -Seconds $Delay }

if (-not $Out) {
    $Out = Join-Path ([IO.Path]::GetTempPath()) ("ot2-" + (Get-Date -Format "HHmmss") + ".png")
}

$b = [System.Windows.Forms.SystemInformation]::VirtualScreen
$bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Output $Out
