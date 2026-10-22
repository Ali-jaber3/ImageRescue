param([string]$OutputPath = '')
$ErrorActionPreference = 'Stop'
if (!$OutputPath) {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.SaveFileDialog
    $dialog.Title = 'Save test IMG on a USB drive; recover results to another physical disk'
    $dialog.Filter = 'Disk image (*.img)|*.img'
    $dialog.FileName = 'ImageRescue-MultiFormat-Test.img'
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { exit 0 }
    $OutputPath = $dialog.FileName
}
$fixtures = Join-Path $PSScriptRoot '..\tests\ImageRescue.CoreTests\Fixtures'
$files = @(Get-ChildItem -LiteralPath $fixtures -File | Where-Object { $_.Extension -in @('.jpg','.png') })
$files += @(Get-ChildItem -LiteralPath (Join-Path $fixtures 'formats') -File | Where-Object { $_.Extension -in @('.bmp','.gif','.tif','.webp','.pdf','.docx','.xlsx','.pptx','.mp3','.m4a','.wav','.mp4','.mov','.avi','.zip') })
if ($files.Count -eq 0) { throw 'Test fixtures missing. Extract the full source ZIP.' }
$stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
$manifest = @()
try {
    foreach ($file in $files) {
        $gap = 512 - ($stream.Position % 512)
        $stream.Write((New-Object byte[] $gap),0,$gap)
        $offset = $stream.Position
        $input = [System.IO.File]::OpenRead($file.FullName)
        try { $input.CopyTo($stream) } finally { $input.Dispose() }
        $manifest += [PSCustomObject]@{ Name = $file.Name; Offset = $offset; Bytes = $file.Length; SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    }
    $stream.Write((New-Object byte[] 512),0,512)
} finally { $stream.Dispose() }
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath ($OutputPath + '.expected.json') -Encoding UTF8
Write-Host "Created ordinary test file: $OutputPath"
Write-Host "Expected files: $($files.Count). No drive was formatted or accessed as a raw device."
Write-Host 'In ImageRescue: Open IMG, select All types, Deep, and recover to a DIFFERENT physical disk.'
