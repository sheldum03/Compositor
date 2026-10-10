#ifndef COMPOSITOR_WINDOWS_BRIDGE_H
#define COMPOSITOR_WINDOWS_BRIDGE_H

#ifdef __cplusplus
extern "C" {
#endif
#include "AdjustPixels.h"
#include "BrushPixels.h"
#include "ContentFill.h"
#include "HealPixels.h"
#include "LensPixels.h"
#include "LevelsPixels.h"
#include "NoisePixels.h"
#include "WandPixels.h"

void compositor_heal_bounds(const uint8_t *gray, size_t width, size_t height,
                            size_t stride, int64_t bounds[4]);
int64_t compositor_wand_mask(const uint8_t *rgba, size_t width, size_t height,
                             size_t stride, size_t seedX, size_t seedY,
                             size_t radius, int tolerance, int contiguous, uint8_t *mask);
void compositor_free(void *allocation);
#ifdef __cplusplus
}
#endif
#endif
