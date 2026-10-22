param([Parameter(Mandatory=$true)][string]$PayloadPath, [Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PayloadPath).Path.TrimEnd('\')
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('; Generated from the exact published payload. Do not replace with RMDir /r.')
foreach ($file in (Get-ChildItem -LiteralPath $root -Recurse -File)) {
    $relative = $file.FullName.Substring($root.Length + 1).Replace('$', '$$')
    $lines.Add('Delete "$INSTDIR\' + $relative + '"')
}
foreach ($directory in (Get-ChildItem -LiteralPath $root -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending)) {
    $relative = $directory.FullName.Substring($root.Length + 1).Replace('$', '$$')
    $lines.Add('RMDir "$INSTDIR\' + $relative + '"')
}
[System.IO.File]::WriteAllLines($OutputPath, $lines, [System.Text.UTF8Encoding]::new($true))
