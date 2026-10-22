$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 10 SDK, then run this script again.' }
    dotnet run --project '.\tests\ImageRescue.CoreTests\ImageRescue.CoreTests.csproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Recovery tests failed. Copy the error before continuing.' }
    dotnet build '.\src\ImageRescue.App\ImageRescue.App.csproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. Copy the error before continuing.' }
    $app = Join-Path $PSScriptRoot 'src\ImageRescue.App\bin\Release\net10.0-windows\ImageRescue.App.exe'
    Start-Process -FilePath $app -Verb RunAs
} finally { Pop-Location }
