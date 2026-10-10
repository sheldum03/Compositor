"""Load the actual library via ctypes; this is not a substitute for C# P/Invoke."""
import ctypes as c
import json
import platform
import sys
from pathlib import Path

library = c.CDLL(str(Path(sys.argv[1]).resolve()))
byte_pointer = c.POINTER(c.c_uint8)
int_pointer = c.POINTER(c.c_int32)
library.compositor_heal_bounds.argtypes = [byte_pointer, c.c_size_t, c.c_size_t, c.c_size_t, c.POINTER(c.c_int64)]
library.compositor_heal_bounds.restype = None
library.compositor_wand_mask.argtypes = [byte_pointer] + [c.c_size_t] * 6 + [c.c_int, c.c_int, byte_pointer]
library.compositor_wand_mask.restype = c.c_int64
library.wand_trace.argtypes = [byte_pointer, c.c_size_t, c.c_size_t,
                              c.POINTER(int_pointer), c.POINTER(c.c_size_t),
                              c.POINTER(int_pointer), c.POINTER(c.c_size_t)]
library.wand_trace.restype = c.c_int
library.compositor_free.argtypes = [c.c_void_p]
library.compositor_free.restype = None

gray = (c.c_uint8 * 8)(0, 0, 255, 255, 0, 255, 255, 255)
bounds = (c.c_int64 * 4)(*([-1] * 4))
library.compositor_heal_bounds(gray, 2, 2, 4, bounds)
assert list(bounds) == [1, 1, 2, 2], list(bounds)
rgba = (c.c_uint8 * 12)(255, 0, 0, 255, 0, 0, 0, 255, 255, 0, 0, 255)
mask = (c.c_uint8 * 3)()
assert library.compositor_wand_mask(rgba, 3, 1, 12, 0, 0, 0, 0, 0, mask) == 2
assert list(mask) == [255, 0, 255]
for _ in range(1000):
    points, loops = int_pointer(), int_pointer()
    point_count, loop_count = c.c_size_t(), c.c_size_t()
    status = library.wand_trace(mask, 3, 1, c.byref(points), c.byref(point_count),
                                c.byref(loops), c.byref(loop_count))
    assert status == 0, status
    try:
        assert (point_count.value, loop_count.value) == (8, 2)
        assert [loops[i] for i in range(loop_count.value)] == [4, 4]
    finally:
        library.compositor_free(points)
        library.compositor_free(loops)
library.compositor_free(None)
print(json.dumps({"status": "passed", "boundary": "ctypes (not P/Invoke)",
                  "system": platform.system(), "machine": platform.machine(),
                  "pointerBytes": c.sizeof(c.c_void_p), "sizeTBytes": c.sizeof(c.c_size_t),
                  "longBytes": c.sizeof(c.c_long), "traceAllocationReleaseCycles": 1000}))
