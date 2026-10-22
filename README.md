# ImageRescue 0.4.0

Open-source Windows file-recovery workspace for USB/SD volumes and raw IMG files. WPF, C#, .NET 10, x64. Read-only source access, output on another physical disk, selected/all export, pause/stop, sorting, batch rename, EXIF, progress/ETA, ten languages and saved preferences.

**Source preview:** this update has not yet been compiled or run on Windows in the current session. See [verification status](docs/VERIFICATION.md).

## New in 0.4

- Categories before scanning: photos, documents, audio, video, archives and experimental camera RAW.
- One sun/moon appearance button and a charcoal #1B1B1B dark theme.
- Visible recovery details explaining boundary candidates and RAW/PDF limits.
- Twenty additional-format test fixtures and a safe ordinary IMG test-file generator.
- Double-click `Test-And-Run.cmd` to run checks, build and launch on Windows.

## Formats

| Category | Extensions |
|---|---|
| Images | JPG, PNG, BMP, GIF, WEBP, TIFF |
| Documents | PDF, DOCX, XLSX, PPTX; limited legacy DOC/XLS/PPT |
| Video | MP4, MOV, AVI |
| Audio | MP3, M4A, M4B, WAV |
| Archives | ZIP |
| RAW, experimental | DNG, CR2 |

[Detailed format support and limitations](docs/FORMAT-SUPPORT.md). The scanner carves contiguous data; it does not reconstruct fragmentation. JPEG/PNG candidates are limited to 64 MiB and other formats to 512 MiB. TIFF/RAW is experimental. PDF first complete revision only. Boundaries do not guarantee that a file opens. Recovered bytes are never recompressed.

## Build and test

Windows with .NET 10 SDK:

```powershell
dotnet run --project .\tests\ImageRescue.CoreTests\ImageRescue.CoreTests.csproj -c Release
dotnet build .\src\ImageRescue.App\ImageRescue.App.csproj -c Release
```

Or double-click `Test-And-Run.cmd`. Run `Create-Test-IMG.cmd` to generate a safe test file, then recover to another physical disk. Old Office/RAW structural fixtures are excluded from that IMG.

[Arabic setup, testing and GitHub update instructions](docs/START_AR.md).

## Installer

`Build-Release.ps1` runs checks, publishes the self-contained Windows x64 app and builds `artifacts/ImageRescue-v0.4.0-Setup.exe`. Requires NSIS. The installer supports desktop/Start shortcuts and uninstall. The GitHub Actions `release-build.yml` workflow builds installer artifacts on demand; it does not publish a release automatically.

## Changed hashes

Selected export can save current bytes even when their SHA-256 differs from the scan. The enabled-by-default UI option flags changed exports and records scanned/current hashes. It does not repair damaged signatures or overwritten data.

## License

MIT; see LICENSE and [third-party notices](docs/THIRD-PARTY.md).
