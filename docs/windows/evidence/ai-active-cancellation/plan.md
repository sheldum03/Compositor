# Active cancellation implementation plan

Goal: prove native in-flight ONNX CPU cancellation and same-session recovery without selecting a production model/framework.
Architecture: opt-in probe branch and independent profile/output reviewer; existing default probe remains unchanged.
Tech stack: existing C++17 / ONNX Runtime 1.30.0; Python standard-library review.

- [x] Read existing probe, actual cancellation gap, frozen assets and runtime API contract.
- [x] Add `experiments/windows/ai/review-active-cancel.py`; run it on the unchanged probe output and observe `Only pre-termination was tested`.
- [x] Extend `probe.cpp` with optional `--active-cancel`, a separate CPU session/profile, worker Run, cross-thread termination, joined ownership and exact recovery. Link Threads in existing CMake target.
- [x] Compile with warnings as errors; run five actual active trials. Require CPU kernels in the first canceled model_run, a second recovery model_run and bitwise output equality.
- [x] Run unchanged default path and compare raw mask with the pre-change result. Remove canceled-run kernel events from a copied profile and require review rejection.
- [x] Archive source/asset identities, raw profiles, reports and commands; update scope/status. Windows and product lifecycle remain unexecuted until separately evidenced.
