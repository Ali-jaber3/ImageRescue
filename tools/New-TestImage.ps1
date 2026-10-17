param([string]$OutputPath = (Join-Path $env:TEMP ("ImageRescueDemo_" + [Guid]::NewGuid().ToString('N') + '.img')))
$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path $PSScriptRoot '..\tests\ImageRescue.CoreTests\Fixtures'
$placements = @(
    @{ Name = 'sample.png'; Offset = 1048573 },
    @{ Name = 'baseline.jpg'; Offset = 2097151 },
    @{ Name = 'progressive.jpg'; Offset = 3145726 }
)
$stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
try {
    $stream.SetLength(4194304)
    foreach ($item in $placements) {
        $bytes = [System.IO.File]::ReadAllBytes((Join-Path $fixtureRoot $item.Name))
        $stream.Position = $item.Offset
        $stream.Write($bytes, 0, $bytes.Length)
    }
} finally { $stream.Dispose() }
Write-Host "Created demo IMG: $OutputPath"
Write-Host 'Expected result: 3 images. Choose a destination on another physical disk.'
