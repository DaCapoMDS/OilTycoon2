<#
    Compiles the C# tools with the .NET Framework compiler that ships with
    Windows - no SDK, no NuGet, nothing to install.
#>
[CmdletBinding()]
param([string]$OutDir = (Join-Path $PSScriptRoot 'bin'))

$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw 'Could not find csc.exe (.NET Framework 4).' }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$targets = @(
    @{ Src = 'Extract.cs';  Out = 'Extract.exe';  Refs = @();                 Kind = 'exe' },
    @{ Src = 'Ot2Crypt.cs'; Out = 'Ot2Crypt.exe'; Refs = @('System.Xml.dll'); Kind = 'exe' },
    @{ Src = 'Solve.cs';    Out = 'Solve.exe';    Refs = @();                 Kind = 'exe' },
    @{ Src = 'Launcher.cs'; Out = 'OilTycoon2Launcher.exe';
       Refs = @('System.Windows.Forms.dll', 'System.Drawing.dll');            Kind = 'winexe' },
    @{ Src = 'Keybinds.cs'; Out = 'Keybinds.exe';
       Refs = @('System.Windows.Forms.dll');                                  Kind = 'exe' },
    @{ Src = 'Ot2Mod.cs';   Out = 'Ot2Mod.exe';   Refs = @();                 Kind = 'exe' },
    @{ Src = 'Ot2Anim.cs';  Out = 'Ot2Anim.exe';  Refs = @();                 Kind = 'exe' },
    # x86 deliberately: it reads the thread context of game.exe, a 32-bit process
    @{ Src = 'Sampler.cs';  Out = 'Sampler.exe';  Refs = @();                 Kind = 'exe'; Platform = 'x86' }
)

foreach ($t in $targets) {
    $src = Join-Path $PSScriptRoot $t.Src
    if (-not (Test-Path $src)) { continue }
    # NB: not $args - that is an automatic variable in PowerShell
    $plat = if ($t.ContainsKey('Platform')) { $t.Platform } else { 'x64' }
    $cscArgs = @('/nologo', '/o+', "/platform:$plat", "/target:$($t.Kind)",
                 "/out:$(Join-Path $OutDir $t.Out)")
    foreach ($r in $t.Refs) { $cscArgs += "/r:$r" }
    $cscArgs += $src
    & $csc @cscArgs
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile $($t.Src)" }
    Write-Host "  built $($t.Out)"
}
