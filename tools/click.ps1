<#
.SYNOPSIS
    Clicks a point inside the Oil Tycoon 2 window, in the game's own UI space.

.DESCRIPTION
    The interface is authored against a 1600x1200 virtual canvas (see the
    Window elements in MainMenu.xml and Interface.xml) and scaled to whatever
    client size the game is running at. Passing virtual coordinates means a
    click lands on the same button regardless of resolution.

.PARAMETER X
.PARAMETER Y
    Position in the 1600x1200 UI space.

.EXAMPLE
    .\tools\click.ps1 -X 804 -Y 794     # Options on the main menu
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][int]$X,
    [Parameter(Mandatory = $true)][int]$Y
)

$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Clk {
    [StructLayout(LayoutKind.Sequential)] public struct PT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RC { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RC r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref PT p);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
}
"@

$p = Get-Process -Name 'game' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { throw 'Oil Tycoon 2 is not running.' }
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { throw 'No window handle yet - give it a moment.' }

$rc = New-Object Clk+RC
[void][Clk]::GetClientRect($h, [ref]$rc)
$cw = $rc.R - $rc.L; $ch = $rc.B - $rc.T
if ($cw -le 0) { throw 'Client area has no size.' }

# the UI canvas is 1600x1200; map into the client area
$cx = [int]([double]$X * $cw / 1600.0)
$cy = [int]([double]$Y * $ch / 1200.0)

$pt = New-Object Clk+PT
$pt.X = $cx; $pt.Y = $cy
[void][Clk]::ClientToScreen($h, [ref]$pt)

[void][Clk]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 600
[void][Clk]::SetCursorPos($pt.X, $pt.Y)
Start-Sleep -Milliseconds 250
[Clk]::mouse_event([Clk]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 60
[Clk]::mouse_event([Clk]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)

Write-Output ("clicked UI ({0},{1}) -> client ({2},{3}) -> screen ({4},{5})  [client {6}x{7}]" -f
    $X, $Y, $cx, $cy, $pt.X, $pt.Y, $cw, $ch)
