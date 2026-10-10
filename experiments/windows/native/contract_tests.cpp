#include "bridge.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>

// Keep checks active in Release, where NDEBUG disables assert().
#define CHECK(value) do { if (!(value)) { \
    std::fprintf(stderr, "%s:%d: %s\n", __FILE__, __LINE__, #value); std::exit(1); \
} } while (0)

struct Bitmap {
    size_t width, height, stride;
    std::vector<uint8_t> bytes;
    Bitmap(size_t w, size_t h) : width(w), height(h), stride(w * 4 + 7), bytes(stride * h, 0xCD) {
        for (size_t y = 0; y < h; ++y)
            for (size_t x = 0; x < w; ++x) set(x, y, {32, 64, 96, 128});
    }
    uint8_t *pixel(size_t x, size_t y) { return bytes.data() + y * stride + x * 4; }
    void set(size_t x, size_t y, std::array<uint8_t, 4> color) {
        std::copy(color.begin(), color.end(), pixel(x, y));
    }
    void valid() {
        for (size_t y = 0; y < height; ++y) {
            for (size_t x = 0; x < width; ++x) {
                auto p = pixel(x, y);
                CHECK(p[0] <= p[3] && p[1] <= p[3] && p[2] <= p[3]);
            }
            for (size_t i = width * 4; i < stride; ++i) CHECK(bytes[y * stride + i] == 0xCD);
        }
    }
};

static void brush() {
    Bitmap image(3, 2);
    image.set(0, 0, {0, 0, 0, 0});
    const auto original = image.bytes;
    std::array<uint8_t, 10> alpha;
    alpha.fill(0xCD);
    layer_extract_alpha(image.bytes.data(), image.stride, alpha.data(), 5, 3, 2);
    CHECK((alpha == std::array<uint8_t, 10>{0,128,128,0xCD,0xCD,128,128,128,0xCD,0xCD}));
    layer_unpremultiply_opaque(image.bytes.data(), image.stride, 3, 2);
    CHECK(image.pixel(1, 0)[3] == 255 && image.pixel(1, 0)[1] == 128);
    layer_restore_alpha(image.bytes.data(), image.stride, alpha.data(), 5, 3, 2);
    CHECK(image.bytes == original);
    image.valid();
    Bitmap sparse(3, 2);
    for (size_t y = 0; y < 2; ++y) for (size_t x = 0; x < 3; ++x) sparse.set(x,y,{0,0,0,0});
    size_t bounds[4] = {99,99,99,99};
    brush_alpha_bounds(sparse.bytes.data(), 3, 2, sparse.stride, bounds);
    CHECK(bounds[0] == 0 && bounds[1] == 0 && bounds[2] == 0 && bounds[3] == 0);
    sparse.set(1, 1, {0,0,0,1});
    brush_alpha_bounds(sparse.bytes.data(), 3, 2, sparse.stride, bounds);
    CHECK(bounds[0] == 1 && bounds[1] == 1 && bounds[2] == 2 && bounds[3] == 2);
}

static void levels() {
    uint8_t pixels[] = {32,64,96,128, 0,0,0,0, 255,128,0,255};
    const std::vector<uint8_t> original(pixels, pixels + sizeof pixels);
    float tables[3 * 256];
    for (int c = 0; c < 3; ++c) for (int i = 0; i < 256; ++i) tables[c*256+i] = i/255.0f;
    levels_apply(pixels, 3, tables);
    CHECK(std::equal(original.begin(), original.end(), pixels));
    double bins[4 * 256] = {};
    const uint8_t coverage[] = {255,255,0};
    levels_histogram(pixels, coverage, 3, bins);
    CHECK(std::abs(bins[256+64] - 128.0/255) < 1e-12);
    CHECK(std::abs(bins[512+128] - 128.0/255) < 1e-12);
    CHECK(std::abs(bins[768+191] - 128.0/255) < 1e-12);
    CHECK(bins[256+255] == 0);
    std::fill(tables, tables + 3 * 256, 1.0f);
    levels_apply(pixels, 3, tables);
    CHECK(pixels[0] == 128 && pixels[1] == 128 && pixels[2] == 128 && pixels[7] == 0);
}

static void adjustments() {
    Bitmap image(6, 4);
    image.set(0, 0, {0,0,0,0});
    uint8_t table[256 * 3];
    for (int i = 0; i < 256; ++i) { table[i*3]=255; table[i*3+1]=0; table[i*3+2]=0; }
    adjust_gradient_map(image.bytes.data(), 6, 4, image.stride, table);
    CHECK(image.pixel(1, 1)[0] == 128 && image.pixel(1, 1)[1] == 0 && image.pixel(0, 0)[0] == 0);
    image.valid();
    Bitmap whole(6, 4), tile(2, 2);
    adjust_grain(whole.bytes.data(), 6, 4, whole.stride, 70, 2, 45, 42, 10, 20, 0.5);
    adjust_grain(tile.bytes.data(), 2, 2, tile.stride, 70, 2, 45, 42, 11, 20.5, 0.5);
    for (size_t y = 0; y < 2; ++y) for (size_t x = 0; x < 2; ++x)
        CHECK(std::memcmp(tile.pixel(x,y), whole.pixel(x+2,y+1), 4) == 0);
    whole.valid(); tile.valid();
    uint8_t overshoot[] = {255,80,20,64, 1,2,3,0};
    rgba_clamp_premultiplied(overshoot, 2);
    const uint8_t expected[] = {64,64,20,64, 0,0,0,0};
    CHECK(std::memcmp(overshoot, expected, sizeof expected) == 0);
}

static void noiseAndLens() {
    for (int gaussian = 0; gaussian < 2; ++gaussian) for (int mono = 0; mono < 2; ++mono) {
        Bitmap a(5,3);
        a.set(0,0,{0,0,0,0});
        auto b = a, original = a;
        noise_add(a.bytes.data(), 5,3,a.stride,80,gaussian,mono,42);
        noise_add(b.bytes.data(), 5,3,b.stride,80,gaussian,mono,42);
        CHECK(a.bytes == b.bytes && a.bytes != original.bytes);
        for (size_t y=0;y<3;++y) for (size_t x=0;x<5;++x)
            CHECK(a.pixel(x,y)[3] == original.pixel(x,y)[3]);
        a.valid();
    }
    Bitmap source(5,3), destination(5,3);
    source.set(2,1,{0,0,0,0});
    lens_distort(source.bytes.data(), destination.bytes.data(), 5,3,source.stride,0);
    CHECK(source.bytes == destination.bytes);
    lens_distort(source.bytes.data(), destination.bytes.data(), 5,3,source.stride,-0.5);
    // The corner still overlaps one source pixel: weight (7/17)*(12/17).
    const uint8_t corner[] = {9,19,28,37};
    CHECK(std::memcmp(destination.pixel(0,0), corner, 4) == 0);
    destination.valid();
}

static void fillAndHeal() {
    Bitmap image(3,3);
    for (size_t y=0;y<3;++y) for (size_t x=0;x<3;++x) image.set(x,y,{20,40,60,255});
    image.set(1,1,{0,0,0,0});
    const uint8_t mask[] = {0,0,0,0xCD, 0,255,0,0xCD, 0,0,0,0xCD};
    CHECK(content_fill(image.bytes.data(), image.stride, mask, 4, 3,3) == 1);
    CHECK(std::memcmp(image.pixel(1,1), image.pixel(0,0), 4) == 0);
    image.valid();
    uint8_t all[9]; std::fill(all, all+9, 255);
    CHECK(content_fill(image.bytes.data(), image.stride, all, 3, 3,3) == 0);
    Bitmap flat(16,16);
    for (size_t y=0;y<16;++y) for (size_t x=0;x<16;++x) flat.set(x,y,{20,40,60,255});
    std::vector<uint8_t> coverage(16*16,0);
    for (int mode=0;mode<3;++mode) {
        auto healed = flat;
        CHECK(spot_heal(healed.bytes.data(), coverage.data(),16,16,healed.stride,1,mode,42) == 0);
        CHECK(healed.bytes == flat.bytes);
        coverage[8*16+8] = 255;
        CHECK(spot_heal(healed.bytes.data(), coverage.data(),16,16,healed.stride,1,mode,42) == 0);
        CHECK(healed.bytes == flat.bytes);
        healed.set(8,8,{200,200,200,255});
        auto repeated = healed;
        CHECK(spot_heal(healed.bytes.data(), coverage.data(),16,16,healed.stride,1,mode,42) == 0);
        CHECK(spot_heal(repeated.bytes.data(), coverage.data(),16,16,repeated.stride,1,mode,42) == 0);
        CHECK(healed.bytes == repeated.bytes);
        // Create Texture measures noise around the defect; it need not produce a flat color.
        if (mode != 1) CHECK(healed.bytes == flat.bytes);
        CHECK(healed.pixel(8,8)[0] != 200 && healed.pixel(8,8)[3] == 255);
        for (size_t y=0;y<16;++y) for (size_t x=0;x<16;++x)
            if (x!=8 || y!=8) CHECK(std::memcmp(healed.pixel(x,y), flat.pixel(x,y), 4) == 0);
        healed.valid();
        coverage[8*16+8] = 0;
    }
    int64_t bounds[4] = {};
    compositor_heal_bounds(mask,3,3,4,bounds);
    CHECK(bounds[0]==1 && bounds[1]==1 && bounds[2]==2 && bounds[3]==2);
}

static void wand() {
    Bitmap image(3,2);
    image.set(0,0,{255,0,0,255}); image.set(1,0,{0,0,0,255}); image.set(2,0,{255,0,0,255});
    image.set(0,1,{0,0,0,255}); image.set(1,1,{255,0,0,255}); image.set(2,1,{0,0,0,255});
    uint8_t mask[6] = {};
    CHECK(compositor_wand_mask(image.bytes.data(),3,2,image.stride,0,0,0,0,1,mask) == 1);
    const uint8_t connected[] = {255,0,0,0,0,0}, disconnected[] = {255,0,255,0,255,0};
    CHECK(std::memcmp(mask, connected, sizeof mask) == 0);
    CHECK(compositor_wand_mask(image.bytes.data(),3,2,image.stride,0,0,0,0,0,mask) == 3);
    CHECK(std::memcmp(mask, disconnected, sizeof mask) == 0);
    int32_t *points = nullptr, *loops = nullptr;
    size_t pointCount = 0, loopCount = 0;
    CHECK(wand_trace(mask,3,2,&points,&pointCount,&loops,&loopCount) == 0);
    CHECK(loopCount == 3 && pointCount == 12 && loops[0] == 4 && loops[1] == 4 && loops[2] == 4);
    for (size_t i=0;i<pointCount;++i) CHECK(points[i*2]>=0 && points[i*2]<=3 && points[i*2+1]>=0 && points[i*2+1]<=2);
    compositor_free(points); compositor_free(loops); compositor_free(nullptr);
    CHECK(compositor_wand_mask(image.bytes.data(),3,2,image.stride,3,0,0,0,0,mask) == 0);
    CHECK(wand_trace(mask,3,2,&points,&pointCount,&loops,&loopCount) == 0);
    CHECK(pointCount == 0 && loopCount == 0);
    compositor_free(points); compositor_free(loops);
}

int main() {
    static_assert(sizeof(int) == 4 && sizeof(int64_t) == 8, "FFI integer widths");
    CHECK(sizeof(size_t) == 8 && sizeof(void *) == 8);
#ifdef _WIN64
    CHECK(sizeof(long) == 4);
#endif
    brush(); levels(); adjustments(); noiseAndLens(); fillAndHeal(); wand();
    std::printf("PASS: 8 C algorithms through shared-library C++ linkage; pointer=%zu size_t=%zu long=%zu\n",
                sizeof(void *), sizeof(size_t), sizeof(long));
}
