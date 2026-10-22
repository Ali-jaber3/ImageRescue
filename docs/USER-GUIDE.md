# ImageRescue 0.4.0 user guide

For the complete Arabic setup, testing and update steps, see [START_AR.md](START_AR.md).

1. Build the source on Windows with .NET 10 SDK, or use an installer produced by Build-Release.ps1 after all tests pass.
2. Choose a supported USB/SD volume or raw IMG file. The app opens it read-only.
3. Choose an existing destination folder on another physical disk.
4. Select the desired file categories. JPEG/PNG are selected initially. All types and Clear selection adjust the entire type selection.
5. Choose Quick (512-byte candidate starts) or Deep (every byte). Both read the whole source; Deep can find unaligned files.
6. Choose automatic export of every candidate or scan first then select files. In selection mode, keep the same source connected.
7. Start scanning. You can pause, resume or stop; completed files and the session report remain.
8. Select files with their checkboxes, then Recover selected. Sort by name/type/size and rename one or multiple selected entries as needed.
9. Inspect the recovery-details column and open the results folder to verify recovered files. Container boundaries are not a guarantee of decode integrity. RAW is experimental and PDF currently includes only the first complete revision.

The changed-hash option is enabled by default: if selected file bytes changed after scanning, the current bytes are retained and marked changed, with both hashes in scan-session.json. Turning it off rejects changed exports. Never deliberately modify the live source to test this; the automated tests use in-memory fixtures.

The single appearance button shows the action available: sun in dark mode switches to light; moon in light mode switches to charcoal dark. Language, appearance and destination are remembered. Settings from the preceding soft-gray mode are still readable and switch to dark on the next appearance-button click.

Test safely with Create-Test-IMG.cmd. It creates an ordinary file from bundled fixtures and writes an expected JSON manifest. It does not format a disk. Synthetic old Office/RAW parser fixtures are excluded. Save the IMG on a USB drive and results on the computer's separate physical disk.

See [FORMAT-SUPPORT.md](FORMAT-SUPPORT.md) for supported extensions and limits. No recovery of fragmented or overwritten data is promised. Files are copied exactly as read; they are not recompressed.
