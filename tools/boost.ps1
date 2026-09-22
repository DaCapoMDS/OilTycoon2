<#
.SYNOPSIS
    Applies CPU-side tweaks to the running game. No restart needed.

.DESCRIPTION
    The game is single-threaded 2006 code: ~3,500 draw calls costing ~90 ms
    with the GPU at under 10%. That is CPU time, and it walks 14,077 objects
    per frame, which is a cache-bound workload.

    On a dual-CCD Ryzen (7950X3D, 9950X3D and relatives) only one CCD carries
    the 3D V-Cache. Windows may schedule an old single-threaded process on the
    other one, or migrate it between them, which costs more than either choice
    would. Pinning it removes that.

    Affinity and priority apply to a live process, so you can watch the FPS
    counter change as they are set.

.PARAMETER Ccd
    0 pins to the first CCD (the one with V-Cache on X3D parts), 1 to the
    second, All removes pinning.

.PARAMETER Priority
    Process priority. High is usually enough; Realtime is not advisable.

.PARAMETER PowerPlan
    Switch the Windows power plan to High performance. Old games are very
    sensitive to core parking and clock ramping. Use -PowerPlan Balanced to
    put it back.

.EXAMPLE
    .\tools\boost.ps1 -Ccd 0 -Priority High -PowerPlan High
    .\tools\boost.ps1 -Ccd 1              # compare the other CCD
    .\tools\boost.ps1 -Ccd All -Priority Normal -PowerPlan Balanced
#>
[CmdletBinding()]
param(
    [ValidateSet('0','1','All')][string]$Ccd = '0',
    [ValidateSet('Normal','AboveNormal','High')][string]$Priority = 'High',
    [ValidateSet('High','Balanced','None')][string]$PowerPlan = 'None'
)

$ErrorActionPreference = 'Stop'

$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$threads = [int]$cpu.NumberOfLogicalProcessors
$cores = [int]$cpu.NumberOfCores
Write-Host ("CPU: {0}" -f $cpu.Name.Trim())
Write-Host ("     {0} cores / {1} threads" -f $cores, $threads)

# Assume two equal CCDs when the core count suggests it. CCD0 owns the lower
# numbered logical processors, and on X3D parts it is the cache-equipped one.
$perCcd = [int]($threads / 2)
switch ($Ccd) {
    '0'   { $mask = [int64]([math]::Pow(2, $perCcd) - 1) }
    '1'   { $mask = [int64]([math]::Pow(2, $perCcd) - 1) -shl $perCcd }
    'All' { $mask = [int64]([math]::Pow(2, $threads) - 1) }
}

$p = Get-Process -Name 'game' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) {
    Write-Warning 'Oil Tycoon 2 is not running - start it, then run this again.'
} else {
    $p.ProcessorAffinity = [IntPtr]$mask
    $p.PriorityClass = [System.Diagnostics.ProcessPriorityClass]$Priority
    $p.Refresh()
    Write-Host ""
    Write-Host ("applied to pid {0}:" -f $p.Id)
    Write-Host ("  affinity  0x{0:X}  ({1})" -f $mask, $(if ($Ccd -eq 'All') { 'all cores' } else { "CCD$Ccd, $perCcd threads" }))
    Write-Host ("  priority  {0}" -f $p.PriorityClass)
    Write-Host ""
    Write-Host "Watch the FPS counter - this takes effect immediately."
}

if ($PowerPlan -ne 'None') {
    $guid = if ($PowerPlan -eq 'High') { '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c' } else { '381b4222-f694-41f0-9685-ff5bb260df2e' }
    powercfg /setactive $guid 2>$null
    if ($LASTEXITCODE -eq 0) { Write-Host ("power plan -> {0}" -f $PowerPlan) }
    else { Write-Warning "Could not switch power plan (the High performance scheme may be hidden on this machine)." }
}
