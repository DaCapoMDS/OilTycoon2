param([string]$In,[string]$Out)
$fs = [System.IO.File]::OpenRead($In)
$os = [System.IO.File]::Create($Out)
$sectorsPerChunk = 1024
$buf = New-Object byte[] (2352*$sectorsPerChunk)
$outBuf = New-Object byte[] (2048*$sectorsPerChunk)
while (($read = $fs.Read($buf,0,$buf.Length)) -gt 0) {
    $n = [int]($read/2352)
    for ($i=0; $i -lt $n; $i++) {
        [System.Buffer]::BlockCopy($buf, $i*2352+16, $outBuf, $i*2048, 2048)
    }
    $os.Write($outBuf,0,$n*2048)
}
$os.Close(); $fs.Close()
Write-Output ("Done: " + (Get-Item $Out).Length + " bytes")
