<#
.SYNOPSIS
    Dumps bytes at a virtual address (RVA) inside a PE file.

.DESCRIPTION
    The sampler reports hot code as RVAs. To see what that code is, the RVA
    has to be translated to a file offset through the section table, since
    sections sit at different places on disk than in memory.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Rva,
    [int]$Length = 96
)

$ErrorActionPreference = 'Stop'
$target = [Convert]::ToInt64($Rva.TrimStart('0','x','X'), 16)

$fs = [IO.File]::OpenRead($Path)
$br = New-Object IO.BinaryReader($fs)
$fs.Position = 0x3C
$pe = $br.ReadUInt32()
$fs.Position = $pe + 4
$null = $br.ReadUInt16()                 # machine
$numSec = $br.ReadUInt16()
$null = $br.ReadBytes(12)
$optSize = $br.ReadUInt16()
$null = $br.ReadUInt16()
$secTable = $fs.Position + $optSize

$found = $null
for ($i = 0; $i -lt $numSec; $i++) {
    $fs.Position = $secTable + $i * 40
    $name = ([Text.Encoding]::ASCII.GetString($br.ReadBytes(8))).Trim([char]0)
    $vsize = $br.ReadUInt32(); $vaddr = $br.ReadUInt32()
    $rsize = $br.ReadUInt32(); $rptr = $br.ReadUInt32()
    if ($target -ge $vaddr -and $target -lt ($vaddr + [Math]::Max($vsize, $rsize))) {
        $found = [pscustomobject]@{ Name = $name; VAddr = $vaddr; RPtr = $rptr }
    }
}
if (-not $found) { $fs.Close(); throw ("RVA 0x{0:X} is not inside any section" -f $target) }

$off = $target - $found.VAddr + $found.RPtr
Write-Host ("{0}  RVA 0x{1:X}  ->  section {2}, file offset 0x{3:X}" -f
    (Split-Path $Path -Leaf), $target, $found.Name, $off)

$fs.Position = $off
$bytes = $br.ReadBytes($Length)
$fs.Close()

for ($i = 0; $i -lt $bytes.Length; $i += 16) {
    $n = [Math]::Min(16, $bytes.Length - $i)
    $hex = (($bytes[$i..($i + $n - 1)] | ForEach-Object { "{0:x2}" -f $_ }) -join ' ')
    $asc = -join ($bytes[$i..($i + $n - 1)] | ForEach-Object { if ($_ -ge 32 -and $_ -lt 127) { [char]$_ } else { '.' } })
    "{0:X8}  {1,-47}  |{2}|" -f ($target + $i), $hex, $asc
}
