# ImageRescue v0.4.0 — source preview

- Add selectable recovery categories for PDF, Word/Excel/PowerPoint, video, audio, ZIP and additional image formats.
- Add experimental DNG/CR2 RAW carving, with recovery limitations displayed in the results.
- Replace three appearance buttons with one sun/moon toggle.
- Introduce a charcoal dark theme based on #1B1B1B, purple accents and rounded themed checkboxes.
- Add multi-format test fixtures, regression coverage, a safe test-IMG generator and a one-click test/build launcher.
- Retain original-byte export, changed-hash recovery, rename, sorting and existing language/preferences support.

This source preview still requires a successful .NET build and Windows acceptance testing. See VERIFICATION.md. Supported formats and explicit limitations are documented in FORMAT-SUPPORT.md. Recovery of fragmented or overwritten files is not guaranteed, RAW is experimental, and PDF carving currently retains only the first complete revision.
