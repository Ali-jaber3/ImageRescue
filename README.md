# ImageRescue

An experimental, open-source Windows photo-recovery application built with C#,
.NET 10 and WPF. Includes Arabic/English interfaces and dark, light and soft-gray themes.

## Current features

- Select an accessible USB/SD drive-letter volume, or a raw `.img` file.
- Read-only signature carving of contiguous JPEG (baseline/progressive) and PNG images.
- Parse JPEG segments and entropy markers; verify PNG chunk CRCs.
- Export images to a unique folder on another physical disk; record SHA-256 hashes.
- Background scan, progress, cancellation and retained completed results.
- Automatically write `scan-session.json` on completion, cancellation or scan failure.
- Scrollable page and virtualized, independently scrollable results.

## Build and run

Requires Windows x64 and .NET 10 SDK. No third-party NuGet dependencies.

```powershell
dotnet build .\src\ImageRescue.App\ImageRescue.App.csproj -c Release
Start-Process '.\src\ImageRescue.App\bin\Release\net10.0-windows\ImageRescue.App.exe' -Verb RunAs
```

The application manifest requires administrator access for raw-volume reading.
Launching the EXE directly triggers UAC. Do not use `dotnet run` from a non-elevated
terminal: Windows can reject that launch with error 740.

1. Connect a test USB/SD, then click Refresh devices.
2. Select its volume; verify the drive letter, label and capacity.
3. Choose an output folder on a different physical disk (e.g. your internal drive).
4. Click Scan and recover. Images are saved as they are found.
5. Open the results folder. The JSON report includes offsets and hashes.

Keep the app and repository on the computer's internal disk, not on the USB being scanned.
No disk formatting, repair, deletion or source-write operations are performed by this application.
Windows and other applications may still write to an attached, mounted device.

## Scope and limits

This is **not** deleted-only recovery: it finds both existing and deleted images.
The direct device scan covers the selected mounted **volume**, not the complete physical
device, lost partitions or space outside that volume. Unformatted, inaccessible and
non-drive-letter devices are outside this version's scope. Some SD readers may not be enumerated.
The Windows system physical disk is excluded from direct scans.

Fragmented, overwritten, TRIM-discarded and encrypted image data may not be recoverable.
Carving does not restore original names, folder paths or deletion status. JPEG support
focuses on SOF0/SOF1/SOF2. Structural validation does not guarantee full pixel integrity.
Limits: 64 MiB and 40 megapixels per image; 10,000 results per scan.
A read error stops the scan and preserves completed output; damaged-media imaging is
not implemented. Live-device changes can affect results.

Output on the source physical disk is refused, even if a different partition letter is
chosen. Unverifiable disk layouts and folder-mounted volumes are refused. The session
report is a record of the run; session reopening/resume is **not** implemented yet.

## Verification

```powershell
dotnet run --project .\tests\ImageRescue.CoreTests\ImageRescue.CoreTests.csproj
```

The dependency-free test runner performs 45 checks, including boundary-spanning
signatures, exact recovered bytes/hashes, baseline/progressive JPEG and PNG fixtures,
PNG CRC corruption, truncation, false signatures, short reads, cancellation, I/O errors
and session reports. Fixtures are synthetic images generated for this project.
The Windows application was cross-built successfully on Linux; native USB/SD I/O,
UAC and visual behavior still require manual Windows validation.

### Optional IMG demo

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\New-TestImage.ps1
```

This creates a unique 4 MiB IMG in the temporary folder with three known images.
Scan it with the application and choose another physical disk for the output.
Alternatively create the IMG directly on a test USB by passing `-OutputPath` with
an unused filename on that USB, and use an internal disk for recovery output.
The script creates an ordinary file; it does not format or write raw sectors.

## GitHub

A Windows GitHub Actions workflow builds the app and runs the recovery checks.
MIT licensed. This repository is under active development; it is not a forensic
or professional data-recovery guarantee.

## Version 0.2 additions

- Always-visible, wrapping destination path, copy-path button and actual output directory.
- Quick scan checks only 512-byte-aligned candidate starts. Deep scan checks every byte.
  Both read the whole source; quick reduces candidate-search CPU work and may miss files.
  Deep is the default. Neither mode reconstructs fragmented files.
- Cooperative pause/resume (acknowledged at a worker checkpoint), plus cancellation while paused.
- Filename prefix before scanning and selected-image rename after scanning, with session report updates.
  Original deleted filenames cannot be reconstructed by this signature-only scanner.
- Explicit name/type/numeric-byte-size sorting in ascending or descending order.
- Exact byte counts, MiB sizes, parsed pixel dimensions and SHA-256 session records.

The application never re-encodes or compresses recovered images. Byte-for-byte recovery
is tested against baseline/progressive JPEG and PNG fixtures. A small recovered file
can be a pre-existing embedded thumbnail, a different image, or incomplete recovery;
matching visual subject matter alone does not prove that it is the full original.
Use original-vs-recovered SHA-256 comparisons when the original is available. Structural
checks cannot guarantee that corrupted or fragmented data was reconstructed correctly.

### Clean update installation

Close ImageRescue. Extract the update ZIP outside the repository. Run its installer:

```powershell
powershell -ExecutionPolicy Bypass -File 'C:\path\to\extracted\Install-Update.ps1' -RepoPath (Get-Location).Path
```

Run that command from the existing repository root. The installer moves the old `src`
to a unique sibling backup and copies a clean `src`; it preserves `.git`, and backs up
replaced documentation/test files. This avoids duplicate `(2)` source files. It does
not commit or push anything. Build and test before pushing.
