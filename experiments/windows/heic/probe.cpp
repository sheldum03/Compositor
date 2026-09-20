#include <libheif/heif.h>
#include <algorithm>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

namespace fs = std::filesystem;
void need(bool value, const char *message) { if (!value) throw std::runtime_error(message); }
void check(heif_error error) { if (error.code != heif_error_Ok) throw std::runtime_error(error.message); }
void write(const fs::path &path, const void *bytes, size_t count) {
    std::ofstream file(path, std::ios::binary); file.write(static_cast<const char *>(bytes), std::streamsize(count)); file.close(); need(bool(file), "Write output");
}
struct Library { Library() { check(heif_init(nullptr)); } ~Library() { heif_deinit(); } };
#ifdef _WIN32
int wmain(int argc, wchar_t **argv) {
#else
int main(int argc, char **argv) {
#endif
    try {
        need(argc == 3 || argc == 4, "Usage: heic_probe <input.heic> <new-output-directory> [pixel-budget]");
        Library library; need(std::string(heif_get_version()) == "1.23.4", "Pinned libheif");
        const heif_decoder_descriptor *descriptors[8];
        int count = heif_get_decoder_descriptors(heif_compression_undefined, descriptors, 8);
        need(count == 1 && std::string(heif_decoder_descriptor_get_id_name(descriptors[0])) == "libde265", "Only libde265 decoder available");
        const heif_encoder_descriptor *encoders[8];
        int encoderCount = heif_get_encoder_descriptors(heif_compression_undefined, nullptr, encoders, 8);
        need(encoderCount == 1 && std::string(heif_encoder_descriptor_get_id_name(encoders[0])) == "mask", "Only internal mask encoder remains");
        need(heif_get_encoder_descriptors(heif_compression_HEVC, nullptr, nullptr, 0) == 0, "No HEVC encoder");
        fs::path input(argv[1]), output(argv[2]);
        need(!fs::exists(output), "Output must be new");
        uint64_t budget = argc == 4 ? std::stoull(argv[3]) : 100'000'000;
        need(budget > 0 && budget <= 100'000'000, "Pixel budget range");
        auto length = fs::file_size(input); need(length > 0 && length <= 256 * 1024 * 1024, "Screening compressed-input limit");
        std::vector<char> bytes(static_cast<size_t>(length)); std::ifstream file(input, std::ios::binary);
        file.read(bytes.data(), std::streamsize(bytes.size())); need(bool(file), "Read HEIC bytes");
        auto start = std::chrono::steady_clock::now();
        std::unique_ptr<heif_context, decltype(&heif_context_free)> context(heif_context_alloc(), heif_context_free); need(bool(context), "Allocate context");
        auto limits = *heif_context_get_security_limits(context.get()); limits.max_image_size_pixels = budget;
        check(heif_context_set_security_limits(context.get(), &limits));
        check(heif_context_read_from_memory_without_copy(context.get(), bytes.data(), bytes.size(), nullptr));
        need(heif_context_get_number_of_top_level_images(context.get()) == 1, "Screening supports one primary image");
        heif_image_handle *rawHandle = nullptr; check(heif_context_get_primary_image_handle(context.get(), &rawHandle));
        std::unique_ptr<heif_image_handle, decltype(&heif_image_handle_release)> handle(rawHandle, heif_image_handle_release);
        int width = heif_image_handle_get_width(handle.get()), height = heif_image_handle_get_height(handle.get());
        need(width > 0 && height > 0 && width <= 30'000 && height <= 30'000 && uint64_t(width) * uint64_t(height) <= budget, "Decoded size budget");
        std::unique_ptr<heif_decoding_options, decltype(&heif_decoding_options_free)> options(heif_decoding_options_alloc(), heif_decoding_options_free);
        need(bool(options), "Allocate options"); options->strict_decoding = 1; options->decoder_id = "libde265"; options->num_codec_threads = 1;
        heif_image *rawImage = nullptr; check(heif_decode_image(handle.get(), &rawImage, heif_colorspace_RGB, heif_chroma_interleaved_RGBA, options.get()));
        std::unique_ptr<heif_image, decltype(&heif_image_release)> image(rawImage, heif_image_release);
        width = heif_image_get_primary_width(image.get()); height = heif_image_get_primary_height(image.get());
        need(width > 0 && height > 0 && uint64_t(width) * uint64_t(height) <= budget, "Transformed size budget");
        need(heif_image_get_decoding_warnings(image.get(), 0, nullptr, 0) == 0, "No decode warnings");
        int stride = 0; auto plane = heif_image_get_plane_readonly(image.get(), heif_channel_interleaved, &stride);
        need(plane && stride >= width * 4 && heif_image_get_bits_per_pixel_range(image.get(), heif_channel_interleaved) == 8, "RGBA8 row contract");
        std::vector<uint8_t> pixels(size_t(width) * size_t(height) * 4);
        for (int y = 0; y < height; ++y) std::copy_n(plane + size_t(y) * stride, size_t(width) * 4, pixels.data() + size_t(y) * width * 4);
        auto elapsed = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count();
        size_t profileSize = heif_image_handle_get_raw_color_profile_size(handle.get()); std::vector<uint8_t> profile(profileSize);
        if (profileSize) check(heif_image_handle_get_raw_color_profile(handle.get(), profile.data()));
        need(fs::create_directory(output), "Create output"); write(output / "rgba.bin", pixels.data(), pixels.size());
        if (profileSize) write(output / "source.icc", profile.data(), profile.size());
        std::ofstream report(output / "decode.json");
        report << "{\n  \"libheif\": \"" << heif_get_version() << "\",\n  \"decoder\": \"" << heif_decoder_descriptor_get_name(descriptors[0])
            << "\",\n  \"width\": " << width << ",\n  \"height\": " << height << ",\n  \"sourceRowStride\": " << stride
            << ",\n  \"hasAlpha\": " << (heif_image_handle_has_alpha_channel(handle.get()) ? "true" : "false")
            << ",\n  \"premultiplied\": " << (heif_image_is_premultiplied_alpha(image.get()) ? "true" : "false")
            << ",\n  \"sourceIccBytes\": " << profileSize << ",\n  \"exifBlocks\": " << heif_image_handle_get_number_of_metadata_blocks(handle.get(), "Exif")
            << ",\n  \"readDecodeCopyMilliseconds\": " << elapsed << ",\n  \"warnings\": 0,\n  \"decoderCount\": 1,\n  \"encoderCount\": 1,\n  \"encoderId\": \"mask\"\n}\n";
        report.close(); need(bool(report), "Write report"); return 0;
    } catch (const std::exception &error) { std::cerr << error.what() << '\n'; return 1; }
}
