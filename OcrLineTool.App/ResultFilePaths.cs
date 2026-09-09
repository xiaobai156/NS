namespace OcrLineTool;

public static class ResultFilePaths
{
    public static string ConfigurationDirectory(string appDirectory) =>
        Path.Combine(appDirectory, "配置文件");

    public static string RuntimeDirectory(string appDirectory) =>
        Path.Combine(appDirectory, "运行组件");

    public static string ImportantResultsDirectory(string appDirectory) =>
        Path.Combine(appDirectory, "重要结果");

    public static string GroupResultsDirectory(string appDirectory) =>
        Path.Combine(ImportantResultsDirectory(appDirectory), "群结果");

    public static string RecognitionStateDirectory(string appDirectory) =>
        Path.Combine(ImportantResultsDirectory(appDirectory), "识别状态");

    public static string ForRecognitionState(string appDirectory, string selectedDirectory, int issue) =>
        Path.Combine(
            RecognitionStateDirectory(appDirectory),
            $"{RuleCatalog.GroupNameForFolder(appDirectory, selectedDirectory)}_{issue}期.state.json");

    public static string ForMissingSummary(string appDirectory) =>
        Path.Combine(GroupResultsDirectory(appDirectory), "缺失及未分流汇总.txt");

    public static string TemporaryFilesDirectory(string appDirectory) =>
        Path.Combine(appDirectory, "临时文件");

    public static string ForOperationLog(string appDirectory) =>
        Path.Combine(appDirectory, "本地日志", "操作日志.txt");

    public static string ForGroup(string outputDirectory, string selectedDirectory, int issue) =>
        Path.Combine(
            GroupResultsDirectory(outputDirectory),
            $"{RuleCatalog.GroupNameForFolder(outputDirectory, selectedDirectory)}_{issue}期.txt");

    public static string ForDiagnostic(string outputDirectory, int issue) =>
        Path.Combine(TemporaryFilesDirectory(outputDirectory), "诊断", $"OCR诊断_{issue}期.json");

    public static string ForDiagnostic(string outputDirectory, string selectedDirectory, int issue) =>
        Path.Combine(
            TemporaryFilesDirectory(outputDirectory),
            "诊断",
            $"OCR诊断_{RuleCatalog.GroupNameForFolder(outputDirectory, selectedDirectory)}_{issue}期.json");

    public static string ForCloudOcrCache(string outputDirectory, string groupName) =>
        Path.Combine(ConfigurationDirectory(outputDirectory), $"云OCR缓存_{groupName}.json");

    public static void EnsureOutputDirectories(string outputDirectory)
    {
        Directory.CreateDirectory(GroupResultsDirectory(outputDirectory));
        Directory.CreateDirectory(RecognitionStateDirectory(outputDirectory));
        Directory.CreateDirectory(Path.Combine(TemporaryFilesDirectory(outputDirectory), "诊断"));
    }
}
