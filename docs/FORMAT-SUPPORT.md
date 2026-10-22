# ImageRescue 0.4.0 format support

This is a source preview pending a .NET build and Windows acceptance testing. A recovered range is a candidate, not a promise that its contents decode. The scanner never recompresses data and never writes to its source.

| Selection | Extensions | Detection and limits |
|---|---|---|
| JPEG, PNG | jpg, png | Existing marker/chunk parser, PNG CRC, dimensions; maximum 64 MiB |
| Other images | bmp, gif, webp, tif | BMP declared length, GIF block terminator, RIFF chunks, TIFF IFD/data extents |
| PDF | pdf | Header plus first EOF with a plausible startxref; only the first complete revision, not subsequent incremental changes |
| Office | docx, xlsx, pptx | ZIP central directory, matching local headers and required package filenames; no extraction/decompression during scanning |
| Legacy Office | doc, xls, ppt | CFB sector allocation and directory stream name; single-DIFAT-header files, at most 109 FAT sectors; synthetic parser fixtures only |
| Video | mp4, mov, avi | Recognized ftyp brand + bounded boxes and moov/mdat, or RIFF AVI header/movie chunks; no codec/frame decoding |
| Audio | mp3, m4a, m4b, wav | At least 3 consecutive compatible Layer III frames, optional ID3; or bounded M4A/B media boxes; or WAVE chunks |
| Archives | zip | EOCD, central/local entry correspondence; ordinary single-disk ZIP, no ZIP64 |
| RAW (experimental) | dng, cr2 | Classic TIFF IFDs and strip/tile/JPEG data extents; vendor-private data may be incomplete; not validated with real camera files |

Additional formats have a 512 MiB candidate size limit. JPEG and PNG retain a 64 MiB / 40-megapixel limit. TIFF is experimental, shares the RAW boundary parser, and does not support BigTIFF. RAW does not mean every camera format: NEF, ARW, RAF, ORF, RW2 and CR3 are not supported. Unsupported types are not renamed into supported types. Word files normally use `.docx` or `.doc`, not `.word`.

Quick mode tests candidate starts on 512-byte boundaries and can miss unaligned files. Deep mode checks every byte. Both recover contiguous data only. Fragmented, truncated, overwritten, encrypted or unsupported containers may be skipped or produce candidates that do not open. MP4 size-zero boxes are deliberately rejected because their EOF cannot be inferred inside a disk image. MP3 without a clear following boundary can include adjacent compatible audio frames. Office/ZIP content CRC and media decoding are not validated by the recovery engine.

A recognized outer container is recovered once; its embedded previews and ZIP entries are not separately carved. The result detail column describes the boundary/RAW/PDF limitations. It remains possible to recover original embedded JPEG previews if RAW is disabled, but they are not the full RAW image.

## Format references

- ISO BMFF overview: https://www.loc.gov/preservation/digital/formats/fdd/fdd000079.shtml
- MP3 and ID3 overview: https://www.loc.gov/preservation/digital/formats/fdd/fdd000105.shtml
- ZIP APPNOTE: https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
- Microsoft CFB: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-cfb/
- TIFF 6.0: https://www.itu.int/itudoc/itu-t/com16/tiff-fx/docs/tiff6.pdf
- Adobe DNG specification: https://helpx.adobe.com/camera-raw/digital-negative.html
- PDF specification: https://opensource.adobe.com/dc-acrobat-sdk-docs/standards/pdfstandards/pdf/PDF32000_2008.pdf
