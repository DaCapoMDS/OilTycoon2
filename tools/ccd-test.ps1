<#
.SYNOPSIS
    Controlled A/B of CPU affinity against the running game.

.DESCRIPTION
    Processor affinity applies to a live process, so this is the one
    comparison in this project that can be made without relaunching - the
    camera never moves and the scene never changes between readings, which
    is what ruined every configuration comparison made by restarting.

    Captures the top strip of the game window (where the engine's own
    profiler draws) once per setting, and stacks them into a single labelled
    image.
#>
[CmdletBinding()]
param(
    [int]$SettleSeconds = 6,
    [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) 'ccd-compare.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W {
    [StructLayout(LayoutKind.Sequential)] public struct PT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RC { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RC r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref PT p);
}
"@

$p = Get-Process -Name 'game' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { throw 'Oil Tycoon 2 is not running.' }

$rc = New-Object W+RC
[void][W]::GetClientRect($p.MainWindowHandle, [ref]$rc)
$origin = New-Object W+PT
[void][W]::ClientToScreen($p.MainWindowHandle, [ref]$origin)
$w = $rc.R - $rc.L
$stripH = 130
Write-Host ("game window client {0}x{1} at screen ({2},{3})" -f $w, ($rc.B - $rc.T), $origin.X, $origin.Y)

$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$threads = [int]$cpu.NumberOfLogicalProcessors
$perCcd = [int]($threads / 2)

$cases = @(
    @{ Label = 'all cores (as Windows scheduled it)'; Mask = [int64]([math]::Pow(2, $threads) - 1) },
    @{ Label = 'CCD0 - the V-Cache die';              Mask = [int64]([math]::Pow(2, $perCcd) - 1) },
    @{ Label = 'CCD1 - the high-clock die';           Mask = [int64]([math]::Pow(2, $perCcd) - 1) -shl $perCcd }
)

$shots = @()
foreach ($c in $cases) {
    $p.ProcessorAffinity = [IntPtr]$c.Mask
    $p.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::High
    Write-Host ("  {0,-38} affinity 0x{1:X} - settling {2}s" -f $c.Label, $c.Mask, $SettleSeconds)
    Start-Sleep -Seconds $SettleSeconds

    $bmp = New-Object System.Drawing.Bitmap($w, $stripH)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($origin.X, $origin.Y, 0, 0, (New-Object System.Drawing.Size($w, $stripH)))
    $g.Dispose()
    $shots += @{ Label = $c.Label; Bmp = $bmp }
}

# stack them with a label above each
$labelH = 22
$total = ($stripH + $labelH) * $shots.Count
$canvas = New-Object System.Drawing.Bitmap($w, $total)
$cg = [System.Drawing.Graphics]::FromImage($canvas)
$cg.Clear([System.Drawing.Color]::FromArgb(20, 20, 20))
$font = New-Object System.Drawing.Font('Consolas', 11, [System.Drawing.FontStyle]::Bold)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Orange)
$y = 0
foreach ($s in $shots) {
    $cg.DrawString($s.Label, $font, $brush, 6, ($y + 3))
    $y += $labelH
    $cg.DrawImage($s.Bmp, 0, $y)
    $y += $stripH
    $s.Bmp.Dispose()
}
$cg.Dispose()
$canvas.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()

Write-Output $Out
