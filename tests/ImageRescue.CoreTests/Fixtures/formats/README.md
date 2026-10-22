# Additional format fixtures

All content is synthetic and generated for this project, not user data.

- BMP/GIF/TIFF/WebP: Pillow, 24 × 18 flat-color image.
- PDF: ReportLab, one page of test text, invariant metadata.
- DOCX/XLSX/PPTX: python-docx/openpyxl/python-pptx, minimal test content.
- ZIP: Python zipfile, one text entry.
- MP3/M4A/WAV and MP4/MOV/AVI: FFmpeg-generated 0.3-second sine wave or flat-color video. Independent FFprobe format recognition checked.
- DOC/XLS/PPT: synthetic CFB header/FAT/directory ranges. Not valid user documents and not an Office compatibility test.
- DNG/CR2: synthetic classic-TIFF IFD/range parser samples. Not real camera images and not a RAW compatibility test.

The source tests verify parser boundaries and original bytes. Actual .NET tests have not been run in the current build environment. The GUI test-IMG generator deliberately excludes synthetic CFB/RAW files.
