param([string]$NsisCompiler = '')
$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
Push-Location $repo
try {
    dotnet run --project '.\tests\ImageRescue.CoreTests\ImageRescue.CoreTests.csproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Recovery checks failed.' }
    $payload = Join-Path $repo 'artifacts\ImageRescue-v0.4.0-win-x64'
    # Clean only the known build output after confirming its absolute location.
    if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload -Recurse -Force }
    dotnet publish '.\src\ImageRescue.App\ImageRescue.App.csproj' -c Release -r win-x64 --self-contained true -o $payload
    if ($LASTEXITCODE -ne 0) { throw 'Application publishing failed.' }
    Copy-Item -LiteralPath '.\LICENSE' -Destination (Join-Path $payload 'LICENSE.txt')
    Copy-Item -LiteralPath '.\docs\USER-GUIDE.md' -Destination (Join-Path $payload 'USER-GUIDE.md')
    Copy-Item -LiteralPath '.\docs\THIRD-PARTY.md' -Destination (Join-Path $payload 'THIRD-PARTY-ImageRescue.md')
    Copy-Item -LiteralPath '.\docs\licenses' -Destination (Join-Path $payload 'licenses') -Recurse
    & '.\tools\Write-UninstallList.ps1' -PayloadPath $payload -OutputPath (Join-Path $repo 'installer\UninstallFiles.nsh')
    Compress-Archive -Path $payload -DestinationPath '.\artifacts\ImageRescue-v0.4.0-win-x64.zip' -Force
    if (!$NsisCompiler) {
        $command = Get-Command makensis.exe -ErrorAction SilentlyContinue
        if ($command) { $NsisCompiler = $command.Source }
        else {
            foreach ($path in @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe")) {
                if (Test-Path -LiteralPath $path) { $NsisCompiler = $path; break }
            }
        }
    }
    if (!$NsisCompiler -or !(Test-Path -LiteralPath $NsisCompiler)) {
        throw 'Portable ZIP built. Install NSIS from https://nsis.sourceforge.io/Download and run this script again to build Setup.exe.'
    }
    & $NsisCompiler '/V3' '.\installer\ImageRescue.nsi'
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    Get-FileHash '.\artifacts\ImageRescue-v0.4.0-Setup.exe', '.\artifacts\ImageRescue-v0.4.0-win-x64.zip' -Algorithm SHA256 |
        ForEach-Object { $_.Hash + '  ' + (Split-Path $_.Path -Leaf) } |
        Set-Content '.\artifacts\SHA256SUMS.txt' -Encoding ASCII
    Write-Host 'Ready: artifacts\ImageRescue-v0.4.0-Setup.exe'
} finally { Pop-Location }
