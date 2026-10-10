# HEIC screening corpus

Sixteen self-generated geometric HEIC files cover EXIF orientations 1–8, opaque/alpha variants and non-square dimensions. The 16 Mac reference PNGs come from decoding those same HEIC files with ImageIO, applying reported EXIF orientation with CoreImage, and rendering sRGB. They are not the uncompressed source pixels.

`cases.json` records requested and observed Mac metadata. `checksums.json` freezes those 33 data files. Generation code, provenance, decoder commands and limits: [HEIC experiment](../../../../experiments/windows/heic/README.md). Generator host: macOS 26.5.1 / Xcode 26.6, 2026-09-21. Re-encoding on another system may change bytes; do not replace frozen files without recording new evidence.

This supplements the earlier 242-file corpus. It does not complete photographic/ICC/HDR/DPI/large-input or Windows IO validation.
