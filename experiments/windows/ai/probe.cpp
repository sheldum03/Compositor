#include <onnxruntime_cxx_api.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <stdexcept>
#include <vector>

namespace fs = std::filesystem;
using Clock = std::chrono::steady_clock;
void check(bool value, const char *message) { if (!value) throw std::runtime_error(message); }
double milliseconds(Clock::time_point start) { return std::chrono::duration<double, std::milli>(Clock::now() - start).count(); }
int main(int argc, char **argv) {
    try {
        check(argc == 4, "Usage: ai_probe <verified-u2netp.onnx> <1x3x320x320-f32le> <new-output-directory>");
        check(std::string(Ort::GetVersionString()) == "1.30.0", "Pinned ONNX Runtime version");
        const fs::path model = fs::u8path(argv[1]), inputPath = fs::u8path(argv[2]), output = fs::u8path(argv[3]);
        uint32_t endian = 1; check(*reinterpret_cast<unsigned char *>(&endian) == 1, "Little-endian float fixture");
        static_assert(sizeof(float) == 4, "Float32 input");
        std::vector<float> input(3 * 320 * 320);
        check(fs::file_size(inputPath) == input.size() * sizeof(float), "Exact input tensor size");
        std::ifstream reader(inputPath, std::ios::binary); reader.read(reinterpret_cast<char *>(input.data()), std::streamsize(input.size() * sizeof(float)));
        check(bool(reader), "Read input tensor");
        check(std::all_of(input.begin(), input.end(), [](float f) { return std::isfinite(f); }), "Finite input tensor");
        check(!fs::exists(output) && fs::create_directory(output), "Output must be new");
        Ort::Env env(ORT_LOGGING_LEVEL_WARNING, "CompositorAiProbe"); env.DisableTelemetryEvents();
        Ort::SessionOptions options; options.SetIntraOpNumThreads(1); options.SetInterOpNumThreads(1);
        options.SetExecutionMode(ExecutionMode::ORT_SEQUENTIAL); options.SetGraphOptimizationLevel(GraphOptimizationLevel::ORT_ENABLE_ALL);
        auto profilePrefix = output / "cpu-profile"; options.EnableProfiling(profilePrefix.c_str());
        auto start = Clock::now(); Ort::Session session(env, model.c_str(), options); double loadMs = milliseconds(start);
        check(session.GetInputCount() == 1 && session.GetOutputCount() == 7, "Fixed U2NetP IO counts");
        auto inputType = session.GetInputTypeInfo(0); auto inputInfo = inputType.GetTensorTypeAndShapeInfo();
        check(inputInfo.GetElementType() == ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT && inputInfo.GetShape() == std::vector<int64_t>({1, 3, 320, 320}), "NCHW float input contract");
        Ort::AllocatorWithDefaultOptions allocator; auto inputName = session.GetInputNameAllocated(0, allocator);
        std::vector<Ort::AllocatedStringPtr> ownedNames; std::vector<const char *> outputNames;
        for (size_t i = 0; i < 7; ++i) { ownedNames.push_back(session.GetOutputNameAllocated(i, allocator)); outputNames.push_back(ownedNames.back().get()); }
        const char *inputs[] = {inputName.get()}; std::array<int64_t, 4> dimensions{1, 3, 320, 320};
        auto memory = Ort::MemoryInfo::CreateCpu(OrtArenaAllocator, OrtMemTypeDefault);
        auto tensor = Ort::Value::CreateTensor<float>(memory, input.data(), input.size(), dimensions.data(), dimensions.size());
        Ort::RunOptions run; std::vector<float> first; std::array<double, 3> times;
        for (size_t n = 0; n < times.size(); ++n) {
            start = Clock::now(); auto predictions = session.Run(run, inputs, &tensor, 1, outputNames.data(), outputNames.size()); times[n] = milliseconds(start);
            for (auto &prediction : predictions) {
                check(prediction.IsTensor(), "Output tensor"); auto info = prediction.GetTensorTypeAndShapeInfo();
                check(info.GetElementType() == ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT && info.GetShape() == std::vector<int64_t>({1, 1, 320, 320}), "Seven float mask outputs");
                const auto *data = prediction.GetTensorData<float>();
                check(std::all_of(data, data + 320 * 320, [](float f) { return std::isfinite(f) && f >= 0 && f <= 1; }), "Finite sigmoid mask");
            }
            const auto *data = predictions[0].GetTensorData<float>();
            if (first.empty()) first.assign(data, data + 320 * 320);
            else check(std::memcmp(first.data(), data, first.size() * sizeof(float)) == 0, "Repeated CPU output exact");
        }
        // This checks cancellation before a run, not interruption of an active inference.
        run.SetTerminate(); bool terminated = false;
        try { session.Run(run, inputs, &tensor, 1, outputNames.data(), outputNames.size()); }
        catch (const Ort::Exception &error) { terminated = std::string(error.what()).find("terminate") != std::string::npos; }
        check(terminated, "Pre-terminated run rejected"); run.UnsetTerminate();
        auto recovered = session.Run(run, inputs, &tensor, 1, outputNames.data(), outputNames.size());
        check(std::memcmp(first.data(), recovered[0].GetTensorData<float>(), first.size() * sizeof(float)) == 0, "Session usable after termination reset");
        auto profile = session.EndProfilingAllocated(allocator); check(fs::is_regular_file(fs::u8path(profile.get())), "CPU profile emitted");
        auto extrema = std::minmax_element(first.begin(), first.end()); check(*extrema.second - *extrema.first > .5f, "Nonconstant foreground prediction");
        std::ofstream mask(output / "mask.f32", std::ios::binary); mask.write(reinterpret_cast<const char *>(first.data()), std::streamsize(first.size() * sizeof(float))); mask.close(); check(bool(mask), "Write raw prediction");
        std::ofstream report(output / "inference.json");
        report << std::setprecision(12) << "{\n  \"onnxruntime\": \"" << Ort::GetVersionString() << "\",\n  \"execution\": \"CPU default; one intra/inter-op thread; sequential\",\n"
            << "  \"sessionLoadMilliseconds\": " << loadMs << ",\n  \"inferenceMilliseconds\": [" << times[0] << ", " << times[1] << ", " << times[2] << "],\n"
            << "  \"outputMinimum\": " << *extrema.first << ",\n  \"outputMaximum\": " << *extrema.second << ",\n"
            << "  \"repeatedPredictionsExact\": true,\n  \"preTerminatedRunRejected\": true,\n  \"sessionRecovered\": true,\n  \"activeInferenceCancellationTested\": false\n}\n";
        report.close(); check(bool(report), "Write inference report");
        std::cout << "CPU inference and pre-termination checks passed; inspect profile and mask; not Windows or quality acceptance\n";
        return 0;
    } catch (const std::exception &error) { std::cerr << error.what() << '\n'; return 1; }
}
