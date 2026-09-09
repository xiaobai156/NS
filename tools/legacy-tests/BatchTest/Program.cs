using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OcrLineTool;

int issue = CredentialSchedule.IssueForDate(CredentialSchedule.TodayInBeijing());
string imageFolder = args.Length > 0 ? args[0] : @"C:\Users\Administrator\Downloads\Telegram Desktop";
string outputFolder = args.Length > 1 ? args[1] : @"C:\Users\Administrator\Desktop\每天工具\飞机抓到的分类\outputs";
bool localOnly = args.Contains("--local-only", StringComparer.OrdinalIgnoreCase);
bool freshLocal = args.Contains("--fresh-local", StringComparer.OrdinalIgnoreCase);
int issueArgument = Array.FindIndex(args, value => value.Equals("--issue", StringComparison.OrdinalIgnoreCase));
if (issueArgument >= 0 && issueArgument + 1 < args.Length && int.TryParse(args[issueArgument + 1], out int configuredIssue))
    issue = configuredIssue;
IReadOnlyList<OcrRule> rules = RuleCatalog.Load(RuleCatalog.PathForFolder(outputFolder, imageFolder));
string[] images = ImageFolderScanner.Scan(imageFolder);

var stopwatch = Stopwatch.StartNew();
var localClient = new PaddleLocalOcrClient();
IReadOnlyDictionary<string, IReadOnlyList<string>> localResults = await localClient.RecognizeBatchAsync(
    images,
    titleRatio: PaddleLocalOcrClient.TitleRatioFor(imageFolder),
    detectionMaxSide: PaddleLocalOcrClient.DetectionMaxSideFor(imageFolder),
    useCache: !freshLocal);
var candidates = new Dictionary<string, IReadOnlyList<OcrRule>>(StringComparer.OrdinalIgnoreCase);
bool isYanran = RuleCatalog.IsGroupFolder(imageFolder, "嫣然心水");
if (localOnly && isYanran)
{
    foreach (LocalCandidatePlan plan in LocalCandidatePlanner.Build(images, localResults, rules)
        .Where(plan => plan.IsPrimary))
        candidates[plan.Path] = plan.Rules;
}
else
{
    foreach (string image in images)
    {
        if (localResults.TryGetValue(image, out IReadOnlyList<string>? lines))
        {
            IReadOnlyList<OcrRule> matched = RuleEngine.FindMatches(image, lines, rules);
            if (matched.Count > 0)
                candidates[image] = matched;
        }
    }
}
TimeSpan localElapsed = stopwatch.Elapsed;

if (localOnly)
{
    string[] matchedIds = candidates.Values.SelectMany(items => items).Select(rule => rule.Id).Distinct().ToArray();
    Console.WriteLine($"图片={images.Length} 候选={candidates.Count} 规则={matchedIds.Length}");
    Console.WriteLine($"本地={localElapsed.TotalSeconds:F2}秒");
    Console.WriteLine(string.Join(Environment.NewLine, matchedIds));
    return;
}

OcrCredential credential = CredentialSchedule.Today();
IOcrClient cloudClient = OcrClientFactory.Create(credential);
OcrCredential fallbackCredential = CredentialSchedule.FallbackFor(credential);
IOcrClient fallbackClient = OcrClientFactory.Create(fallbackCredential);
var values = new Dictionary<string, string>(StringComparer.Ordinal);
var diagnostics = new List<object>();
int fallbackCalls = 0;
var fallbackSpacing = Stopwatch.StartNew();
bool fallbackStarted = false;
foreach (KeyValuePair<string, IReadOnlyList<OcrRule>> candidate in candidates)
{
    IReadOnlyList<string> cloudLines = await cloudClient.RecognizeAsync(candidate.Key);
    var matches = new List<string>();
    var missingRules = new List<OcrRule>();
    foreach (OcrRule rule in candidate.Value)
    {
        string? value = RuleEngine.ExtractFinalValue(cloudLines, issue, rule);
        if (value is not null)
        {
            values.TryAdd(rule.Id, value);
            matches.Add($"{value} {rule.OutputLabel}");
        }
        else if (!values.ContainsKey(rule.Id))
        {
            missingRules.Add(rule);
        }
    }

    IReadOnlyList<string>? fallbackLines = null;
    if (missingRules.Count > 0)
    {
        if (fallbackStarted)
        {
            TimeSpan delay = CloudOcrPolicy.MinimumInterval(fallbackCredential.Provider) - fallbackSpacing.Elapsed;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);
        }
        fallbackSpacing.Restart();
        fallbackStarted = true;
        fallbackCalls++;
        fallbackLines = await fallbackClient.RecognizeAsync(candidate.Key);
        foreach (OcrRule rule in missingRules)
        {
            string? value = RuleEngine.ExtractFinalValue(fallbackLines, issue, rule);
            if (value is null)
                continue;
            values.TryAdd(rule.Id, value);
            matches.Add($"{value} {rule.OutputLabel}");
        }
    }

    diagnostics.Add(new
    {
        file = Path.GetFileName(candidate.Key),
        keywords = candidate.Value.Select(x => x.Keyword),
        matches,
        primary_provider = credential.DisplayName,
        primary_lines = cloudLines,
        fallback_provider = fallbackLines is null ? null : fallbackCredential.DisplayName,
        fallback_lines = fallbackLines
    });
}
stopwatch.Stop();

string[] outputLines = RuleEngine.FormatOutput(rules, values);
string resultPath = Path.Combine(outputFolder, $"OCR测试结果_{issue}期.txt");
string diagnosticPath = Path.Combine(outputFolder, $"OCR测试诊断_{issue}期.json");
await File.WriteAllLinesAsync(resultPath, outputLines, new UTF8Encoding(true));
await File.WriteAllTextAsync(diagnosticPath, JsonSerializer.Serialize(new
{
    issue,
    provider = credential.DisplayName,
    image_count = images.Length,
    candidate_image_count = candidates.Count,
    cloud_call_count = candidates.Count + fallbackCalls,
    fallback_call_count = fallbackCalls,
    matched_rule_count = values.Count,
    local_seconds = Math.Round(localElapsed.TotalSeconds, 2),
    total_seconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 2),
    candidate_files = candidates.Keys.Select(Path.GetFileName),
    missing_keywords = rules.Select(rule => rule.Id).Where(id => !values.ContainsKey(id)),
    images = diagnostics
}, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(true));

Console.WriteLine($"图片={images.Length} 候选={candidates.Count} 云调用={candidates.Count + fallbackCalls} 备用调用={fallbackCalls} 结果={values.Count}");
Console.WriteLine($"本地={localElapsed.TotalSeconds:F2}秒 总计={stopwatch.Elapsed.TotalSeconds:F2}秒");
