# Brush update pixel regression

The 264 hashes in `baseline.json` were generated from the pre-optimization `SoftBrushStroke.cs` and `TiledRaster.cs` at `e6bcc9f`, using .NET 10.0.401 / SkiaSharp 2.88.9 on macOS arm64. They cover provisional frames and commits for 12/40/520/800 px, 1/40/100% opacity, transparent and previously painted layers, crossings, repeated coordinates and clipping at the document edges. Undo, redo and cancellation are also checked. The current test additionally requires repeated unchanged draws to copy zero pixels and clearing the cache to reconstruct the same image. Coverage accumulation is checked against the original scalar formula for all 65,536 byte pairs, with unaligned spans and lengths around 16/32-byte vector boundaries. Set `DOTNET_EnableHWIntrinsic=0` to run the same checks through the scalar fallback.

Run from the repository root:

```sh
dotnet run --project experiments/windows/brush-update-regression/Regression.csproj -c Release -- experiments/windows/brush-update-regression/baseline.json
```

Rendering uses RGBA8 premultiplied sRGB at 50% with nearest sampling. This is a pixel regression, not a Windows performance or physical-pointer acceptance test. S02 uses the unchanged 4000×4000 workload in `avalonia/PERFORMANCE.md`.

`--record <path>` is for explicit baseline generation. Do not regenerate the baseline to make a failing comparison pass. `-p:BrushSourceDir=<directory>` compiles diagnostic variants of the current brush API without changing production files. The pre-cache baseline was recorded before adding cache assertions; do not use the current cached-call test driver to regenerate it against the older API.
