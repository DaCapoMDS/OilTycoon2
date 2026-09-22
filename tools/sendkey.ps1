<#
.SYNOPSIS
    Sends a keystroke to the Oil Tycoon 2 window.

.DESCRIPTION
    Used for A/B measurements inside a single session: toggling the F1
    profiler without closing the game means the camera does not move, so two
    readings are directly comparable. Restarting between settings does not
    give that - the camera sits wherever the last session left it.

    Sends a SCANCODE, not a virtual key. The game reads the keyboard through
    DirectInput, which works from scancodes and ignores virtual-key
    injection - the same reason the WASD remapper had to be rewritten.

.PARAMETER Key
    F1..F12, or a two-hex-digit scancode.
#>
[CmdletBinding()]
param([string]$Key = 'F1')

$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class SK {
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; public int p1, p2; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public KEYBDINPUT ki; }
    [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] i, int cb);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();

    public static void Tap(ushort scan) {
        var a = new INPUT[2];
        a[0].type = 1; a[0].ki.wScan = scan; a[0].ki.dwFlags = 0x0008;              // SCANCODE
        a[1].type = 1; a[1].ki.wScan = scan; a[1].ki.dwFlags = 0x0008 | 0x0002;     // + KEYUP
        SendInput(2, a, Marshal.SizeOf(typeof(INPUT)));
    }
}
"@

# F-key scancodes: F1..F10 are 0x3B..0x44, F11 0x57, F12 0x58
$map = @{}
for ($i = 1; $i -le 10; $i++) { $map["F$i"] = 0x3A + $i }
$map['F11'] = 0x57; $map['F12'] = 0x58

$scan = if ($map.ContainsKey($Key)) { $map[$Key] } else { [Convert]::ToInt32($Key, 16) }

$p = Get-Process -Name 'game' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { throw 'Oil Tycoon 2 is not running.' }

[void][SK]::SetForegroundWindow($p.MainWindowHandle)
Start-Sleep -Milliseconds 700
[void][SK]::Tap([uint16]$scan)
Write-Output ("sent {0} (scancode 0x{1:X2}) to pid {2}" -f $Key, $scan, $p.Id)
