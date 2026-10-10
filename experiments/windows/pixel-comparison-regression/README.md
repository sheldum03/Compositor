# Pixel comparison regression

Runs the actual `Program.Compare` method from the Avalonia probe. It checks exact equality, a one-channel difference, Alpha and premultiplied color statistics, an odd-width image, heatmap pixels, and rejected dimensions. The 4000×4000 check requires less than 1 MiB of managed allocation after warm-up, catching the previous two full managed pixel copies (about 128 MB). This is a comparison-helper allocation bound, not an S05 process-memory or native-memory acceptance threshold.

```sh
dotnet restore experiments/windows/pixel-comparison-regression
dotnet run --project experiments/windows/pixel-comparison-regression -c Release -- <NEW_OUTPUT_DIRECTORY>
```

Use the pinned SDK and the probe's pinned transitive dependencies. No C native library or window is required. Native Skia decoding still allocates image storage; this test measures managed allocations only. The baseline fails the allocation assertion while passing the pixel-statistics checks. Outputs retain the test PNGs for inspection. No Windows CI execution is implied by a local pass.
