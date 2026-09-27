using System.Reflection;
using System.Text;
using System.Text.Json;
using OcrLineTool;
using Xunit.Abstractions;

namespace OcrLineTool.Tests;

// 「手动复抓缺失」本机优先阶段的真实批次验收：真实嫣然心水 269 期全套图片、
// 真实 GPU medium，走程序自己的 SelectCandidatesAsync + RetryMissingByLocalOcrAsync，
// 不发任何云请求。缺失清单取自上一轮全量识别的生产诊断
// （发布\临时文件\诊断\OCR诊断_嫣然心水_269期.json 的 missing_rules）。
public sealed class RetryRealImageTests(ITestOutputHelper output)
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly string[] Issue269MissingRuleIds =
    [
        "依然公主", "输送机", "末日降临", "追踪使者", "君军合", "辣椒炒肉肖肖",
        "钦差大臣公式一", "潮汕陈龙杀三码", "Alice两码", "杰少杀一肖", "杰少禁一尾"
    ];

    [RetryRealImageFact]
    public async Task LocalFirstRetryReportsTheRealIssue269Outcome()
    {
        string root = Environment.GetEnvironmentVariable("OCR_RETRY_SAMPLE_DIRECTORY")!;
        string report = Environment.GetEnvironmentVariable("OCR_RETRY_REPORT_DIRECTORY")!;
        Directory.CreateDirectory(report);
        IReadOnlyList<OcrRule> catalog = RuleCatalog.Load(Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
        OcrRule[] missing = catalog
            .Where(rule => Issue269MissingRuleIds.Contains(rule.Id, StringComparer.Ordinal))
            .ToArray();
        Assert.Equal(Issue269MissingRuleIds.Length, missing.Length);
        Assert.All(missing, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Folder)));

        string[] images = ImageFolderScanner.Scan(root);
        string[] folders = missing.Select(rule => rule.Folder!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        IReadOnlyList<string> scope = MainForm.RetryImageScope(images, missing);
        // 缩小只按缺失资料自己的文件夹：目标资料一张不漏，顺序仍是原图片列表。
        Assert.Equal(
            images.Where(path => folders.Any(folder => RuleCatalog.PathBelongsToGroup(path, folder))),
            scope);
        Assert.True(scope.Count < images.Length);
        output.WriteLine($"图片={images.Length} 缺失资料={folders.Length} 复抓候选范围={scope.Count} 张");

        using var form = new MainForm();
        var values = new ResultValues(StringComparer.Ordinal);
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        SynchronizationContext? context = SynchronizationContext.Current;
        string? cropFolder = null;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            typeof(MainForm).GetField("selectedImageDirectory", Hidden)!.SetValue(form, root);
            typeof(MainForm).GetField("imagePaths", Hidden)!.SetValue(form, images);
            typeof(MainForm).GetField("ocrDevice", Hidden)!.SetValue(form, LocalOcrDevice.Gpu);
            typeof(MainForm).GetField("lastRules", Hidden)!.SetValue(form, catalog);
            typeof(MainForm).GetField("lastIssue", Hidden)!.SetValue(form, 269);
            typeof(MainForm).GetField("lastValues", Hidden)!.SetValue(form, values);
            typeof(MainForm).GetField("lastMissingReasons", Hidden)!.SetValue(form, reasons);
            typeof(MainForm).GetField("lastEvidenceLedger", Hidden)!.SetValue(form, ledger);
            typeof(MainForm).GetField("lastTextRecognizedRuleIds", Hidden)!
                .SetValue(form, new HashSet<string>(StringComparer.Ordinal));

            // 复抓阶段的状态栏与进度条由本机 OCR 的后台线程写；测试不显示窗口，
            // 只有窗口句柄始终没被创建时这种写入才是安全的。
            Assert.False(form.IsHandleCreated, "真实批次复抓测试不应创建窗口句柄");

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(25));
            object limited = typeof(MainForm)
                .GetMethod("LimitedLocalRetryRuleIds", Hidden)!.Invoke(form, [missing])!;
            var select = (Task)typeof(MainForm).GetMethod("SelectCandidatesAsync", Hidden)!.Invoke(
                form,
                [
                    missing, 269, true, catalog.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal),
                    limited, scope
                ])!;
            await select;
            object selection = select.GetType().GetProperty("Result")!.GetValue(select)!;
            object candidates = selection.GetType().GetProperty("Candidates")!.GetValue(selection)!;
            cropFolder = (string?)selection.GetType().GetProperty("TemporaryCropFolder")!.GetValue(selection);

            var retry = (Task)typeof(MainForm)
                .GetMethod("RetryMissingByLocalOcrAsync", Hidden)!.Invoke(form, [candidates, 269, timeout.Token])!;
            await retry;

            string[] recovered = missing.Where(rule => values.ContainsKey(rule.Id))
                .Select(rule => rule.Id).ToArray();
            string[] unresolved = missing.Where(rule => !values.ContainsKey(rule.Id))
                .Select(rule => rule.Id).ToArray();
            output.WriteLine($"补齐={recovered.Length}/{missing.Length}：{string.Join('、', recovered)}");
            output.WriteLine($"仍缺失={unresolved.Length}：{string.Join('、', unresolved)}");

            Assert.Empty(values.Conflicts);
            Assert.All(recovered, id => Assert.True(ledger.Records.ContainsKey(id), $"{id} 有值却没有证据记录"));
            string[] lines = RuleEngine.FormatOutput(catalog, values, reasons);
            string[] reportLines = GroupResultFormatter.Format(catalog, lines);
            await File.WriteAllLinesAsync(
                Path.Combine(report, "复抓真实批次_嫣然心水_269期.txt"), reportLines, new UTF8Encoding(false));
            await File.WriteAllTextAsync(
                Path.Combine(report, "复抓真实批次_嫣然心水_269期.json"),
                JsonSerializer.Serialize(new
                {
                    图片总数 = images.Length,
                    复抓候选范围 = scope.Count,
                    缺失资料 = missing.Select(rule => new { 规则 = rule.Id, 资料 = rule.Folder, 值 = values.GetValueOrDefault(rule.Id), 原因 = reasons.GetValueOrDefault(rule.Id) }),
                    补齐 = recovered,
                    仍缺失 = unresolved
                }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
                new UTF8Encoding(false));
            Assert.True(File.Exists(Path.Combine(report, "复抓真实批次_嫣然心水_269期.json")));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
            if (cropFolder is not null && Directory.Exists(cropFolder))
                Directory.Delete(cropFolder, recursive: true);
        }
    }

    // 全链路验收：真的点一次「手动复抓缺失」（本机优先 → 云兜底），走程序自己的
    // RetryMissingAsync。缺失清单取上一轮生产诊断的 269 期 11 条；为了让
    // MissingRetryRules 恰好得到这 11 条，先把上一轮的群结果 TXT 写进磁盘——生产里
    // RefreshConcludedValueLines 就是靠它把其余 91 条算作已处理（不预置 values：
    // 预置值会和复抓取到的证据撞成冲突，把「补回/仍缺失」计数污染成负数）。
    // 冷起点：删掉本群上一轮的识别状态与云缓存，这样「补回几条」只能来自本轮真发的
    // 云请求，而不是复用旧证据。
    [RetryRealImageCloudFact]
    public async Task ManualRetryReachesTheCloudFallbackOnTheRealIssue269Batch()
    {
        string root = Environment.GetEnvironmentVariable("OCR_RETRY_SAMPLE_DIRECTORY")!;
        string report = Environment.GetEnvironmentVariable("OCR_RETRY_REPORT_DIRECTORY")!;
        Directory.CreateDirectory(report);
        IReadOnlyList<OcrRule> catalog = RuleCatalog.Load(Path.Combine(
            ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory), "嫣然心水.json"));
        OcrRule[] missing = catalog
            .Where(rule => Issue269MissingRuleIds.Contains(rule.Id, StringComparer.Ordinal))
            .ToArray();
        Assert.Equal(Issue269MissingRuleIds.Length, missing.Length);

        string groupFile = ResultFilePaths.ForGroup(AppContext.BaseDirectory, root, 269);
        Directory.CreateDirectory(Path.GetDirectoryName(groupFile)!);
        var concludedSource = new ResultValues(StringComparer.Ordinal);
        foreach (OcrRule rule in catalog)
        {
            if (!Issue269MissingRuleIds.Contains(rule.Id, StringComparer.Ordinal))
                concludedSource[rule.Id] = "上一轮已得值";
        }

        File.WriteAllLines(
            groupFile,
            GroupResultFormatter.Format(catalog, RuleEngine.FormatOutput(
                catalog, concludedSource, new Dictionary<string, string>(StringComparer.Ordinal))),
            new UTF8Encoding(true));
        foreach (string stale in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "重要结果", "识别状态", "嫣然心水_269期"),
                     Path.Combine(ResultFilePaths.ConfigurationDirectory(AppContext.BaseDirectory),
                         "云OCR缓存_嫣然心水.json")
                 })
        {
            if (Directory.Exists(stale))
                Directory.Delete(stale, recursive: true);
            else if (File.Exists(stale))
                File.Delete(stale);
        }

        // 复抓的诊断必须落在主识别用的同一个文件上（同一路径），并且是「本轮复抓」这个
        // 新任务：先放一份上一轮的诊断，跑完断言它被换成了新 task_id 的复抓记录。
        string diagnosticPath = ResultFilePaths.ForDiagnostic(AppContext.BaseDirectory, root, 269);
        Directory.CreateDirectory(Path.GetDirectoryName(diagnosticPath)!);
        File.WriteAllText(
            diagnosticPath,
            JsonSerializer.Serialize(new { task_id = "上一轮任务", mode = "本地主识别", status = "完成" }),
            new UTF8Encoding(true));
        string[] concludedBefore = GroupResultFormatter
            .ReadConcludedValueLines(File.ReadAllLines(groupFile, Encoding.UTF8), catalog)
            .Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();

        var values = new ResultValues(StringComparer.Ordinal);
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        var ledger = new ResultEvidenceLedger();
        var history = new List<string>();
        string final = string.Empty;
        Exception? failure = null;

        // 程序里的复抓跑在 UI 线程上、有消息循环；测试必须照这个来。控件句柄要在
        // 同一个线程上创建，否则句柄会被挂到另一个线程的 parking window 上，
        // 跨线程 SetParent 会永久阻塞（本机验收时 SetBusy 卡死就是这个原因，
        // 不是产品缺陷）。这里用 STA 线程 + Application.DoEvents 泵消息。
        var finished = new TaskCompletionSource();
        var uiThread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm();
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                ((NumericUpDown)typeof(MainForm).GetField("issueInput", Hidden)!.GetValue(form)!).Value = 269;
                typeof(MainForm).GetField("selectedImageDirectory", Hidden)!.SetValue(form, root);
                typeof(MainForm).GetField("imagePaths", Hidden)!.SetValue(form, ImageFolderScanner.Scan(root));
                typeof(MainForm).GetField("ocrDevice", Hidden)!.SetValue(form, LocalOcrDevice.Gpu);
                typeof(MainForm).GetField("retryUsesLocalOcr", Hidden)!.SetValue(form, true);
                typeof(MainForm).GetField("lastRules", Hidden)!.SetValue(form, catalog);
                typeof(MainForm).GetField("lastIssue", Hidden)!.SetValue(form, 269);
                typeof(MainForm).GetField("lastValues", Hidden)!.SetValue(form, values);
                typeof(MainForm).GetField("lastMissingReasons", Hidden)!.SetValue(form, reasons);
                typeof(MainForm).GetField("lastEvidenceLedger", Hidden)!.SetValue(form, ledger);
                typeof(MainForm).GetField("lastTextRecognizedRuleIds", Hidden)!
                    .SetValue(form, new HashSet<string>(StringComparer.Ordinal));

                var status = (Control)typeof(MainForm).GetField("statusLabel", Hidden)!.GetValue(form)!;
                if (!(bool)typeof(MainForm).GetMethod("CanRetryMissing", Hidden)!.Invoke(form, null)!)
                    throw new InvalidOperationException("预置状态没有让「手动复抓缺失」变成可点");

                // async void 事件处理器：状态栏的终态文案就是结束信号。
                typeof(MainForm).GetMethod("RetryMissingAsync", Hidden)!.Invoke(form, [null, EventArgs.Empty]);
                // 同步段跑完时状态栏正是「复抓候选范围：N/M 张」，先记下来：泵一次消息
                // 就会被本机阶段的进度文案顶掉。
                if (status.Text.Length > 0)
                    history.Add(status.Text);

                DateTime deadline = DateTime.UtcNow.AddMinutes(30);
                DateTime fastUntil = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline)
                {
                    string text = status.Text;
                    if (text.Length > 0 && (history.Count == 0 || history[^1] != text))
                        history.Add(text);
                    if (IsRetryTerminal(text))
                        break;
                    Application.DoEvents();
                    Thread.Sleep(DateTime.UtcNow < fastUntil ? 20 : 100);
                }

                final = status.Text;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                finished.TrySetResult();
            }
        })
        { IsBackground = true };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        await finished.Task;

        if (failure is not null)
            throw new InvalidOperationException("复抓线程失败", failure);

        string[] recovered = missing.Where(rule => values.ContainsKey(rule.Id))
            .Select(rule => rule.Id).ToArray();
        string[] unresolved = missing.Where(rule => !values.ContainsKey(rule.Id))
            .Select(rule => rule.Id).ToArray();
        string? scopeLine = history.FirstOrDefault(line => line.Contains("复抓候选范围", StringComparison.Ordinal));
        int primaryCloudCalls = history.Count(line => line.Contains("主云", StringComparison.Ordinal));
        int fallbackCloudCalls = history.Count(line => line.Contains("备用", StringComparison.Ordinal));
        output.WriteLine($"候选范围行：{scopeLine}");
        output.WriteLine($"终态：{final}");
        output.WriteLine($"补齐={recovered.Length}/{missing.Length}：{string.Join('、', recovered)}");
        output.WriteLine($"仍缺失={unresolved.Length}：{string.Join('、', unresolved)}");
        output.WriteLine($"云请求：主云 {primaryCloudCalls} 次、备用 {fallbackCloudCalls} 次");
        output.WriteLine($"冲突：{(values.Conflicts.Count == 0 ? "无" : string.Join('、', values.Conflicts))}");
        output.WriteLine($"证据记录：{ledger.Records.Count} 条（{string.Join('、', ledger.Records.Keys)}）");

        // 复抓的诊断必须落在主识别用的同一个文件上，并且是「本轮复抓」这个新任务。
        Assert.True(File.Exists(diagnosticPath), $"复抓未写诊断：{diagnosticPath}");
        using (JsonDocument diagnostic = JsonDocument.Parse(File.ReadAllText(diagnosticPath, Encoding.UTF8)))
        {
            JsonElement state = diagnostic.RootElement;
            Assert.Equal("复抓（本机优先 → 云兜底）", state.GetProperty("mode").GetString());
            Assert.Equal("完成", state.GetProperty("status").GetString());
            Assert.True(state.GetProperty("published").GetBoolean());
            Assert.NotEqual("上一轮任务", state.GetProperty("task_id").GetString());
            Assert.Equal(269, state.GetProperty("issue").GetInt32());
            Assert.Equal(missing.Length, state.GetProperty("missing_before").GetInt32());
            Assert.Equal(recovered.Length, state.GetProperty("recovered").GetInt32());
            Assert.Equal(unresolved.Length, state.GetProperty("missing_after").GetInt32());
            string[] confirmed = state.GetProperty("manual_confirmed_rules")
                .EnumerateArray().Select(item => item.GetString()!).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Assert.Equal(concludedBefore, confirmed);
            Assert.DoesNotContain(missing.Select(rule => rule.Id), id => confirmed.Contains(id, StringComparer.Ordinal));
            Assert.True(state.GetProperty("cloud_request_count").GetInt32() >= primaryCloudCalls + fallbackCloudCalls);
            Assert.Equal(JsonValueKind.Object, state.GetProperty("stage_seconds").ValueKind);
            Assert.True(state.GetProperty("excluded_wait_seconds").GetDouble() >= 0);
            output.WriteLine(
                $"诊断：task_id={state.GetProperty("task_id").GetString()} 补回={state.GetProperty("recovered").GetInt32()} " +
                $"仍缺失={state.GetProperty("missing_after").GetInt32()} 云请求={state.GetProperty("cloud_request_count").GetInt32()}");
        }

        string? copiedGroupFile = null;
        if (File.Exists(groupFile))
        {
            copiedGroupFile = Path.Combine(report, "复抓真实批次_嫣然心水_269期_云兜底_群结果.txt");
            File.Copy(groupFile, copiedGroupFile, overwrite: true);
        }

        await File.WriteAllTextAsync(
            Path.Combine(report, "复抓真实批次_嫣然心水_269期_云兜底.json"),
            JsonSerializer.Serialize(new
            {
                场景 = "手动复抓缺失全链路（本机优先 → 云兜底），真实嫣然心水 269 期图片",
                图片总数 = ImageFolderScanner.Scan(root).Length,
                缺失清单来源 = "上一轮群结果 TXT（91 条已得值）+ 生产诊断的 11 条缺失",
                冷起点 = "已删除 重要结果\\识别状态\\嫣然心水_269期 与 配置文件\\云OCR缓存_嫣然心水.json",
                候选范围状态行 = scopeLine,
                终态 = final,
                状态历史 = history,
                缺失资料 = missing.Select(rule => new
                {
                    规则 = rule.Id,
                    资料 = rule.Folder,
                    值 = values.GetValueOrDefault(rule.Id),
                    原因 = reasons.GetValueOrDefault(rule.Id)
                }),
                补齐 = recovered,
                仍缺失 = unresolved,
                主云请求次数 = primaryCloudCalls,
                备用请求次数 = fallbackCloudCalls,
                冲突 = values.Conflicts.ToArray(),
                证据记录 = ledger.Records.Keys.ToArray(),
                群结果文件 = copiedGroupFile
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }),
            new UTF8Encoding(false));

        // 复抓会顺带重抽同一资料夹里的兄弟规则（RetryEvidenceRules）。新证据与已有值
        // 不一致时，那条在内存里会被记成冲突（ResultValues.AddTo 里 Remove + Conflicts），
        // 但写群结果时会用 ReapplyConcludedValueLines 把 TXT 里已有的结论补回去
        // （MainForm.cs:2405-2410）。所以验收点是「冲突不等于丢值」：每个冲突条目在
        // 写出的群结果里仍然有值。
        IReadOnlyDictionary<string, string> writtenConcluded =
            GroupResultFormatter.ReadConcludedValueLines(File.ReadAllLines(groupFile, Encoding.UTF8), catalog);
        output.WriteLine($"冲突条目在群结果里仍有值：{string.Join('、', values.Conflicts.Where(writtenConcluded.ContainsKey))}");
        Assert.All(values.Conflicts, id => Assert.True(writtenConcluded.ContainsKey(id),
            $"{id} 被记成冲突后在群结果里丢了值"));
        Assert.All(recovered, id => Assert.True(ledger.Records.ContainsKey(id), $"{id} 有值却没有证据记录"));
        Assert.True(final.Contains("复抓完成", StringComparison.Ordinal),
            $"复抓没有走到终态，最后状态：{final}");
    }

    private static bool IsRetryTerminal(string text) =>
        text.Contains("复抓完成", StringComparison.Ordinal)
        || text.Contains("复抓失败", StringComparison.Ordinal)
        || text.Contains("复抓未找到", StringComparison.Ordinal)
        || text.Contains("已取消", StringComparison.Ordinal);
}

public sealed class RetryRealImageFactAttribute : FactAttribute
{
    public RetryRealImageFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_RETRY_SAMPLE_DIRECTORY"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_RETRY_REPORT_DIRECTORY")))
            Skip = "Opt-in: provide OCR_RETRY_SAMPLE_DIRECTORY and an isolated OCR_RETRY_REPORT_DIRECTORY.";
    }
}

public sealed class RetryRealImageCloudFactAttribute : FactAttribute
{
    public RetryRealImageCloudFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_RETRY_SAMPLE_DIRECTORY"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_RETRY_REPORT_DIRECTORY")))
        {
            Skip = "Opt-in: provide OCR_RETRY_SAMPLE_DIRECTORY and an isolated OCR_RETRY_REPORT_DIRECTORY.";
            return;
        }

        // 云兜底会真的发云请求、消耗额度，所以本机阶段之外再要一个显式开关。
        if (Environment.GetEnvironmentVariable("OCR_RETRY_ALLOW_CLOUD") != "1")
            Skip = "Opt-in: set OCR_RETRY_ALLOW_CLOUD=1 to allow real cloud OCR requests.";
    }
}
