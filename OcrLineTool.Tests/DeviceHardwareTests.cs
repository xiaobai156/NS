using System.Diagnostics;
using System.Runtime.InteropServices;
using OcrLineTool;
using Xunit.Abstractions;

namespace OcrLineTool.Tests;

public sealed class DeviceHardwareTests(ITestOutputHelper output)
{
    private static string Fixture => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "leifeng.jpg"));

    [LocalOcrHardwareFact]
    [Trait("Category", "LocalOCR")]
    public async Task RealCpuAndGpuRecognizeSmallAndMediumWithoutSharedCache()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ocr-device-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var catalog = VisualTemplateMatcher.Load(VisualTemplateMatcher.ConfigPath(AppContext.BaseDirectory));
            var rule = RuleCatalog.Load(Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"))
                .Single(rule => rule.Id == "雷锋");
            foreach (LocalOcrDevice device in Enum.GetValues<LocalOcrDevice>())
            {
                var match = Assert.Single(VisualTemplateMatcher.Match([Fixture], catalog, device: device));
                Assert.Equal("雷锋", match.Template.Id);
                string crop = Path.Combine(folder, device + ".png");
                VisualTemplateMatcher.CreateCrop(match, crop, includeRemainingRows: true);
                var client = new PaddleLocalOcrClient(new HardwareRunner(device), Path.Combine(folder, "unused-cache.json"), device);
                foreach (PaddleOcrModel model in Enum.GetValues<PaddleOcrModel>())
                {
                    var watch = Stopwatch.StartNew();
                    var results = await client.RecognizeBatchAsync([crop], useCache: false, model: model);
                    Assert.NotEmpty(results[crop]);
                    string? value = RuleEngine.ExtractFinalValue(results[crop], 248, rule);
                    output.WriteLine($"{device}/{model}: {watch.Elapsed.TotalSeconds:F1}s, lines={results[crop].Count}, value={value}");
                    Assert.Equal("32 28 24 35", value);
                }
                Assert.False(File.Exists(Path.Combine(folder, "unused-cache.json")));
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    private sealed class HardwareRunner(LocalOcrDevice device) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessStartInfo info, Action<string>? progress, CancellationToken token)
        {
            // Hide the GPU in a fresh CPU worker, without touching the parent process or other tasks.
            if (device == LocalOcrDevice.Cpu) info.Environment["CUDA_VISIBLE_DEVICES"] = "-1";
            return new SystemProcessRunner().RunAsync(info, progress, token);
        }
    }

    [GpuHiddenFact]
    public void NativeGpuFailureIsUnwrappedAndNeverFallsBackToCpu()
    {
        var catalog = VisualTemplateMatcher.Load(VisualTemplateMatcher.ConfigPath(AppContext.BaseDirectory));
        OcrException error = Assert.Throws<OcrException>(() => VisualTemplateMatcher.Match([Fixture], catalog));
        Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, error.Code);
        Assert.Contains("CPU", error.Message);
    }

    [DeviceFaultFact]
    public async Task SimulatedGpuFaultStopsGpuAndCpuStillRecognizesRealImage()
    {
        bool missingDll = Environment.GetEnvironmentVariable("OCR_SIMULATE_CUDA_DLL_FAILURE") == "1";
        if (missingDll)
        {
            // This resolver belongs only to this dedicated test process. No DLL is moved or deleted.
            NativeLibrary.SetDllImportResolver(typeof(VisualTemplateMatcher).Assembly, (name, assembly, path) =>
            {
                if (name == "cuda_hash.dll") throw new DllNotFoundException("Simulated missing CUDA dependency");
                return IntPtr.Zero;
            });
        }
        string folder = Path.Combine(Path.GetTempPath(), "ocr-fault-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            var catalog = VisualTemplateMatcher.Load(VisualTemplateMatcher.ConfigPath(AppContext.BaseDirectory));
            OcrException nativeError = Assert.Throws<OcrException>(() => VisualTemplateMatcher.Match([Fixture], catalog));
            Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, nativeError.Code);
            Assert.Contains("CPU", nativeError.Message);
            output.WriteLine($"GPU template: stopped, code={nativeError.Code}; scenario={(missingDll ? "DLL load failure injection" : "GPU hidden")}");

            if (!missingDll)
            {
                var gpu = new PaddleLocalOcrClient(new SystemProcessRunner(), Path.Combine(folder, "gpu-cache.json"), LocalOcrDevice.Gpu);
                OcrException preflight = await Assert.ThrowsAsync<OcrException>(() => gpu.EnsureCudaAvailableAsync(timeout.Token));
                Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, preflight.Code);
                output.WriteLine($"GPU preflight: stopped, code={preflight.Code}");
                foreach (PaddleOcrModel model in Enum.GetValues<PaddleOcrModel>())
                {
                    OcrException error = await Assert.ThrowsAsync<OcrException>(() =>
                        gpu.RecognizeBatchAsync([Fixture], useCache: false, model: model, cancellationToken: timeout.Token));
                    Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, error.Code);
                    Assert.Empty(gpu.LastEvidence);
                    output.WriteLine($"GPU/{model}: stopped, code={error.Code}, no successful evidence");
                }
            }

            var match = Assert.Single(VisualTemplateMatcher.Match([Fixture], catalog, device: LocalOcrDevice.Cpu));
            Assert.Equal("雷锋", match.Template.Id);
            string crop = Path.Combine(folder, "cpu.png");
            VisualTemplateMatcher.CreateCrop(match, crop, includeRemainingRows: true);
            var rule = RuleCatalog.Load(Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "新澳六合彩资料.json"))
                .Single(rule => rule.Id == "雷锋");
            var cpu = new PaddleLocalOcrClient(new HardwareRunner(LocalOcrDevice.Cpu), Path.Combine(folder, "cpu-cache.json"), LocalOcrDevice.Cpu);
            foreach (PaddleOcrModel model in Enum.GetValues<PaddleOcrModel>())
            {
                var watch = Stopwatch.StartNew();
                var result = await cpu.RecognizeBatchAsync([crop], useCache: false, model: model, cancellationToken: timeout.Token);
                string? value = RuleEngine.ExtractFinalValue(result[crop], 248, rule);
                Assert.Equal("32 28 24 35", value);
                output.WriteLine($"CPU/{model}: {watch.Elapsed.TotalSeconds:F1}s, value={value}, GPU hidden in worker");
            }
            Assert.False(File.Exists(Path.Combine(folder, "cpu-cache.json")));
            Assert.False(File.Exists(Path.Combine(folder, "gpu-cache.json")));
        }
        finally { Directory.Delete(folder, true); }
    }
}

public sealed class DeviceFaultFactAttribute : FactAttribute
{
    public DeviceFaultFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OCR_RUN_DEVICE_FAULT_TESTS") != "1")
            Skip = "Dedicated process only: OCR_RUN_DEVICE_FAULT_TESTS=1 plus CUDA_VISIBLE_DEVICES=-1 or OCR_SIMULATE_CUDA_DLL_FAILURE=1.";
        else if (Environment.GetEnvironmentVariable("CUDA_VISIBLE_DEVICES") != "-1"
            && Environment.GetEnvironmentVariable("OCR_SIMULATE_CUDA_DLL_FAILURE") != "1")
            Skip = "Fault scenario was not configured.";
    }
}

public sealed class LocalOcrHardwareFactAttribute : FactAttribute
{
    public LocalOcrHardwareFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OCR_RUN_HARDWARE_TESTS") != "1")
            Skip = "Opt-in: OCR_RUN_HARDWARE_TESTS=1; project Python, local models, and CUDA required.";
    }
}

public sealed class GpuHiddenFactAttribute : FactAttribute
{
    public GpuHiddenFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CUDA_VISIBLE_DEVICES") != "-1")
            Skip = "Run in a fresh process with CUDA_VISIBLE_DEVICES=-1.";
    }
}
