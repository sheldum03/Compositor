#include <onnxruntime_cxx_api.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <future>
#include <iomanip>
#include <iostream>
#include <stdexcept>
#include <vector>

namespace fs = std::filesystem;
using Clock = std::chrono::steady_clock;
#ifdef COMPOSITOR_BIREFNET
constexpr int64_t imageSide = 1024;
constexpr size_t outputCount = 1;
constexpr bool outputIsLogits = true;
constexpr const char *predictionFile = "logits.f32";
#else
constexpr int64_t imageSide = 320;
constexpr size_t outputCount = 7;
constexpr bool outputIsLogits = false;
constexpr const char *predictionFile = "mask.f32";
#endif
void check(bool value, const char *message) { if (!value) throw std::runtime_error(message); }
double milliseconds(Clock::time_point start) { return std::chrono::duration<double, std::milli>(Clock::now() - start).count(); }
#ifdef _WIN32
int wmain(int argc, wchar_t **argv) {
#else
int main(int argc, char **argv) {
#endif
    try {
        const bool activeCancellation = argc == 5 && fs::path(argv[4]) == "--active-cancel";
        check(argc == 4 || activeCancellation, "Usage: <model-specific probe> <verified-model.onnx> <NCHW-f32le> <new-output-directory> [--active-cancel]");
        check(std::string(Ort::GetVersionString()) == "1.30.0", "Pinned ONNX Runtime version");
        const fs::path model(argv[1]), inputPath(argv[2]), output(argv[3]);
        uint32_t endian = 1; check(*reinterpret_cast<unsigned char *>(&endian) == 1, "Little-endian float fixture");
        static_assert(sizeof(float) == 4, "Float32 input");
        std::vector<float> input(3 * imageSide * imageSide);
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
        check(session.GetInputCount() == 1 && session.GetOutputCount() == outputCount, "Fixed model IO counts");
        auto inputType = session.GetInputTypeInfo(0); auto inputInfo = inputType.GetTensorTypeAndShapeInfo();
        check(inputInfo.GetElementType() == ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT && inputInfo.GetShape() == std::vector<int64_t>({1, 3, imageSide, imageSide}), "NCHW float input contract");
        Ort::AllocatorWithDefaultOptions allocator; auto inputName = session.GetInputNameAllocated(0, allocator);
        std::vector<Ort::AllocatedStringPtr> ownedNames; std::vector<const char *> outputNames;
        for (size_t i = 0; i < outputCount; ++i) { ownedNames.push_back(session.GetOutputNameAllocated(i, allocator)); outputNames.push_back(ownedNames.back().get()); }
        const char *inputs[] = {inputName.get()}; std::array<int64_t, 4> dimensions{1, 3, imageSide, imageSide};
        auto memory = Ort::MemoryInfo::CreateCpu(OrtArenaAllocator, OrtMemTypeDefault);
        auto tensor = Ort::Value::CreateTensor<float>(memory, input.data(), input.size(), dimensions.data(), dimensions.size());
        Ort::RunOptions run; std::vector<float> first; std::array<double, 3> times;
        for (size_t n = 0; n < times.size(); ++n) {
            start = Clock::now(); auto predictions = session.Run(run, inputs, &tensor, 1, outputNames.data(), outputNames.size()); times[n] = milliseconds(start);
            for (auto &prediction : predictions) {
                check(prediction.IsTensor(), "Output tensor"); auto info = prediction.GetTensorTypeAndShapeInfo();
                check(info.GetElementType() == ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT && info.GetShape() == std::vector<int64_t>({1, 1, imageSide, imageSide}), "Fixed float output shape");
                const auto *data = prediction.GetTensorData<float>();
                check(std::all_of(data, data + imageSide * imageSide, [](float f) {
                    return std::isfinite(f) && (outputIsLogits || (f >= 0 && f <= 1));
                }), "Finite output with model-specific semantics");
            }
            const auto *data = predictions[0].GetTensorData<float>();
            if (first.empty()) first.assign(data, data + imageSide * imageSide);
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
        if (activeCancellation) {
            auto activeOptions = options.Clone();
            auto activePrefix = output / "active-profile"; activeOptions.EnableProfiling(activePrefix.c_str());
            Ort::Session activeSession(env, model.c_str(), activeOptions);
            Ort::RunOptions activeRun;
            std::promise<void> entered; auto entry = entered.get_future();
            auto worker = std::async(std::launch::async, [&] {
                entered.set_value();
                try { activeSession.Run(activeRun, inputs, &tensor, 1, outputNames.data(), outputNames.size()); }
                catch (const Ort::Exception &error) {
                    if (std::string(error.what()).find("terminate") != std::string::npos) return true;
                    throw;
                }
                return false;
            });
            entry.get();
            check(worker.wait_for(std::chrono::milliseconds(10)) == std::future_status::timeout,
                  "Inference finished before cancellation could be requested");
            auto cancelStart = Clock::now(); activeRun.SetTerminate();
            const bool rejected = worker.get(); // Join before resetting options or releasing borrowed storage.
            const double cancelToJoinMs = milliseconds(cancelStart);
            check(rejected, "Active inference did not return a termination error");
            activeRun.UnsetTerminate();
            auto activeRecovered = activeSession.Run(activeRun, inputs, &tensor, 1, outputNames.data(), outputNames.size());
            check(std::memcmp(first.data(), activeRecovered[0].GetTensorData<float>(), first.size() * sizeof(float)) == 0,
                  "Session output changed after active cancellation");
            auto activeProfile = activeSession.EndProfilingAllocated(allocator);
            check(fs::is_regular_file(fs::u8path(activeProfile.get())), "Active cancellation profile emitted");
            std::ofstream recoveredMask(output / "active-recovered.f32", std::ios::binary);
            recoveredMask.write(reinterpret_cast<const char *>(activeRecovered[0].GetTensorData<float>()), std::streamsize(first.size() * sizeof(float)));
            recoveredMask.close(); check(bool(recoveredMask), "Write recovery prediction");
            std::ofstream activeReport(output / "active-cancellation.json");
            activeReport << std::setprecision(12) << "{\n  \"terminationErrorObserved\": true,\n  \"workerJoined\": true,\n"
                << "  \"cancelledCallReturnedOutput\": false,\n  \"sessionRecovered\": true,\n"
                << "  \"cancelToJoinMilliseconds\": " << cancelToJoinMs << "\n}\n";
            activeReport.close(); check(bool(activeReport), "Write active cancellation observations");
        }
        auto extrema = std::minmax_element(first.begin(), first.end()); check(*extrema.second - *extrema.first > .5f, "Nonconstant foreground prediction");
        std::ofstream mask(output / predictionFile, std::ios::binary); mask.write(reinterpret_cast<const char *>(first.data()), std::streamsize(first.size() * sizeof(float))); mask.close(); check(bool(mask), "Write raw prediction");
        std::ofstream report(output / "inference.json");
        report << std::setprecision(12) << "{\n  \"onnxruntime\": \"" << Ort::GetVersionString() << "\",\n  \"execution\": \"CPU default; one intra/inter-op thread; sequential\",\n"
            << "  \"imageSide\": " << imageSide << ",\n  \"outputCount\": " << outputCount << ",\n  \"outputSemantics\": \"" << (outputIsLogits ? "logits" : "sigmoid") << "\",\n"
            << "  \"sessionLoadMilliseconds\": " << loadMs << ",\n  \"inferenceMilliseconds\": [" << times[0] << ", " << times[1] << ", " << times[2] << "],\n"
            << "  \"outputMinimum\": " << *extrema.first << ",\n  \"outputMaximum\": " << *extrema.second << ",\n"
            << "  \"repeatedPredictionsExact\": true,\n  \"preTerminatedRunRejected\": true,\n  \"sessionRecovered\": true,\n  \"activeInferenceCancellationTested\": " << (activeCancellation ? "true" : "false") << "\n}\n";
        report.close(); check(bool(report), "Write inference report");
        std::cout << "CPU inference and pre-termination checks passed; inspect profile and mask; not Windows or quality acceptance\n";
        return 0;
    } catch (const std::exception &error) { std::cerr << error.what() << '\n'; return 1; }
}
