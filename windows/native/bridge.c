#include "bridge.h"
#include <stdlib.h>

void compositor_heal_bounds(const uint8_t *gray, size_t width, size_t height,
                            size_t stride, int64_t bounds[4]) {
    long nativeBounds[4];
    heal_coverage_bounds(gray, width, height, stride, nativeBounds);
    for (int i = 0; i < 4; ++i) bounds[i] = (int64_t)nativeBounds[i];
}

int64_t compositor_wand_mask(const uint8_t *rgba, size_t width, size_t height,
                             size_t stride, size_t seedX, size_t seedY,
                             size_t radius, int tolerance, int contiguous, uint8_t *mask) {
    return (int64_t)wand_mask(rgba, width, height, stride, seedX, seedY,
                              radius, tolerance, contiguous, mask);
}

void compositor_free(void *allocation) { free(allocation); }
