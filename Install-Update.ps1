param([string]$RepoPath = '')
if (!$RepoPath) {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = 'Select your existing ImageRescue Git repository (the folder containing .git).'
    $dialog.ShowNewFolderButton = $false
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { exit 0 }
    $RepoPath = $dialog.SelectedPath
}
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath $RepoPath).Path
$bundleProject = Join-Path $PSScriptRoot 'src\ImageRescue.App\ImageRescue.App.csproj'
if (!(Test-Path -LiteralPath $bundleProject)) { throw 'Extract the full ZIP before running this installer.' }
if (!(Test-Path -LiteralPath (Join-Path $repo '.git'))) { throw 'RepoPath must be your existing ImageRescue Git repository.' }
if ($repo.TrimEnd('\') -eq $PSScriptRoot.TrimEnd('\')) { throw 'Extract this update outside your repository first.' }
$parent = Split-Path $repo -Parent
$backup = Join-Path $parent ('ImageRescue-backup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup | Out-Null
$oldSrc = Join-Path $repo 'src'
if (Test-Path -LiteralPath $oldSrc) {
    Move-Item -LiteralPath $oldSrc -Destination (Join-Path $backup 'src')
}
try {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src') -Destination $repo -Recurse
} catch {
    if (Test-Path -LiteralPath $oldSrc) {
        Move-Item -LiteralPath $oldSrc -Destination (Join-Path $backup 'incomplete-new-src')
    }
    if (Test-Path -LiteralPath (Join-Path $backup 'src')) {
        Move-Item -LiteralPath (Join-Path $backup 'src') -Destination $repo
    }
    throw
}
# Other repository files are backed up and replaced individually; .git is never touched.
foreach ($folder in @('tests', 'tools', 'docs', '.github', 'installer')) {
    $sourceFolder = Join-Path $PSScriptRoot $folder
    if (!(Test-Path -LiteralPath $sourceFolder)) { continue }
    foreach ($file in (Get-ChildItem -LiteralPath $sourceFolder -Recurse -File)) {
        $relative = $file.FullName.Substring($PSScriptRoot.Length + 1)
        $target = Join-Path $repo $relative
        if (Test-Path -LiteralPath $target) {
            $saved = Join-Path $backup $relative
            New-Item -ItemType Directory -Path (Split-Path $saved -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $target -Destination $saved
        }
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}
foreach ($name in @('README.md', '.gitignore', 'Build-Release.ps1', 'Install-Update.ps1', 'Update-Source.cmd', 'Test-And-Run.ps1', 'Test-And-Run.cmd', 'Create-Test-IMG.cmd')) {
    $target = Join-Path $repo $name
    if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination (Join-Path $backup $name) }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $target -Force
}
Write-Host 'Update installed. Your .git history was preserved.'
Write-Host "Previous files are saved at: $backup"
Write-Host 'Next: dotnet build .\src\ImageRescue.App\ImageRescue.App.csproj -c Release'
