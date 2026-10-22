# Version 0.4.0 verification status

This is a source preview. No 0.4.0 application or installer binary has been built here.

Completed in this session:

- Static XML/XAML parsing, named event-handler and translation-reference checks.
- All ten language dictionaries have identical nonempty key sets.
- Twenty additional-format fixtures generated. Fifteen are ordinary files produced by Pillow, ReportLab, python-docx, openpyxl, python-pptx, Python ZIP and FFmpeg. Five are synthetic CFB/TIFF/RAW parser fixtures, not usable production documents/camera images.
- Independent fixture checks for ZIP integrity, RIFF extents, BMFF box extents, MP3 frame extent, and FFprobe recognition of the six audio/video fixtures.
- Source ZIP archive integrity and exclusion of build outputs.

Added .NET regression coverage: original byte/hash equality and exact boundaries for twenty additional types, scan-block overlap, quick mode, catalogue-only Office versus ZIP classification, type filtering, malformed ZIP/media boxes, truncated headers and random signature rejection. The test IMG generator excludes synthetic Office/RAW fixtures.

Not executed: .NET compilation, .NET recovery tests, WPF UI, Windows installation/uninstallation, shortcuts, live USB/SD scanning, actual camera RAW files or large real-world video recovery. The SDK is not installed in this environment and the download endpoint timed out. Do not treat static checks or fixture generation as a successful .NET test run.

Run Test-And-Run.cmd or the commands in START_AR.md on Windows. Build-Release.ps1 and the release-build GitHub workflow require passing .NET tests before packaging. Additional format results are boundary candidates, not proof of decode integrity; see FORMAT-SUPPORT.md.
