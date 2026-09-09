from apply_audit_fixes import ROOT, MANIFEST, changed, write, replace
import json

if MANIFEST.exists():
    changed.extend(json.loads(MANIFEST.read_text(encoding='utf-8')))

if not (ROOT / 'docs/audit-phase4-applied.md').exists():
    write('OcrLineTool.App/OwnedResultWriter.cs', '''using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OcrLineTool;

/// <summary>Updates only rows this tool previously wrote. Unknown same-label rows are never deleted.</summary>
internal static class OwnedResultWriter
{
    private sealed record OwnedLine(string SourceGroup, string Label, string Line);

    internal static async Task<IReadOnlySet<string>> ApplyAsync(string targetPath, string sourceGroup,
        string[] configuredLines, string? marker, bool blankLineBeforeMarker)
    {
        string ownerPath = targetPath + ".ocr-owners.json";
        try
        {
            // Cross-process lock for cooperating instances; a busy target is reported, not silently retried.
            using FileStream gate = new(targetPath + ".ocr-write.lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            byte[] original = await File.ReadAllBytesAsync(targetPath);
            byte[] originalHash = SHA256.HashData(original);
            var rows = (await File.ReadAllLinesAsync(targetPath)).ToList();
            List<OwnedLine> owners = File.Exists(ownerPath)
                ? JsonSerializer.Deserialize<List<OwnedLine>>(await File.ReadAllTextAsync(ownerPath))
                    ?? throw new OcrException("分流归属记录无效，未修改目标文件。")
                : [];
            var pending = new List<string>();
            bool modified = false;
            foreach (IGrouping<string, string> group in configuredLines.GroupBy(Label, StringComparer.Ordinal))
            {
                string[] distinct = group.Distinct(StringComparer.Ordinal).ToArray();
                if (distinct.Length != 1)
                    throw new OcrException($"分流结果冲突：{group.Key}。未修改目标文件。");
                string line = distinct[0];
                string label = group.Key;
                OwnedLine[] matchingOwners = owners.Where(item => item.SourceGroup == sourceGroup && item.Label == label).ToArray();
                if (matchingOwners.Length > 1)
                    throw new OcrException("分流归属记录重复，未修改目标文件。");
                OwnedLine? owned = matchingOwners.SingleOrDefault();
                int[] sameLabel = Enumerable.Range(0, rows.Count).Where(index => Label(rows[index]) == label).ToArray();
                if (owned is not null && owned.Line != line && sameLabel.Length == 1 && rows[sameLabel[0]] == owned.Line
                    && !owners.Any(item => item.SourceGroup != sourceGroup && item.Label == label))
                {
                    rows[sameLabel[0]] = line;
                    owners.Remove(owned);
                    owners.Add(new(sourceGroup, label, line));
                    modified = true;
                    continue;
                }
                if (sameLabel.Any(index => rows[index] != line))
                    throw new OcrException($"分流目标存在不同值且归属不确定：{label}。保留原文件，请核对。");
                if (rows.Contains(line, StringComparer.Ordinal))
                    continue; // Idempotent, but do not claim ownership of someone else's existing row.
                pending.Add(line);
                if (owned is not null) owners.Remove(owned);
                owners.Add(new(sourceGroup, label, line));
                modified = true;
            }
            if (!modified) return configuredLines.ToHashSet(StringComparer.Ordinal);
            int markerIndex = marker is null ? -1 : rows.FindIndex(line => line == marker);
            if (marker is not null && markerIndex < 0)
                markerIndex = rows.FindIndex(line =>
                {
                    string compact = string.Concat(line.Where(c => !char.IsWhiteSpace(c)));
                    return compact.Contains("内容") && compact.Contains("次数") && compact.Contains("排名");
                });
            if (pending.Count > 0 && markerIndex >= 0)
            {
                while (markerIndex > 0 && string.IsNullOrWhiteSpace(rows[markerIndex - 1]))
                    rows.RemoveAt(--markerIndex);
                rows.InsertRange(markerIndex, pending);
                if (blankLineBeforeMarker) rows.Insert(markerIndex + pending.Count, string.Empty);
            }
            else rows.AddRange(pending);

            // Detect edits by non-cooperating tools before replacement. Such tools should share the lock protocol.
            if (!SHA256.HashData(await File.ReadAllBytesAsync(targetPath)).SequenceEqual(originalHash))
                throw new OcrException("分流目标在写入前被其他程序修改，请重试。");
            Encoding encoding = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF
                ? new UTF8Encoding(true)
                : original.Length >= 2 && original[0] == 0xFF && original[1] == 0xFE ? Encoding.Unicode
                : original.Length >= 2 && original[0] == 0xFE && original[1] == 0xFF ? Encoding.BigEndianUnicode
                : new UTF8Encoding(false);
            await AtomicFile.WriteAllLinesAsync(targetPath, rows, encoding);
            // Separate atomic files, not a multi-file transaction. Failure here is reported, never marked distributed.
            await AtomicFile.WriteAllTextAsync(ownerPath, JsonSerializer.Serialize(owners));
            return configuredLines.ToHashSet(StringComparer.Ordinal);
        }
        catch (OcrException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new OcrException($"分流目标忙、不可写或归属记录损坏：{Path.GetFileName(targetPath)}。请检查后重试。");
        }
    }

    private static string Label(string line)
    {
        if (line.StartsWith("缺失", StringComparison.Ordinal)) return string.Empty;
        int separator = line.LastIndexOf(' ');
        return separator >= 0 ? line[(separator + 1)..] : string.Empty;
    }
}
''')
    path = 'OcrLineTool.App/ResultDistributor.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '''            catch (OcrException exception)
            {
                errors.Add(exception.Message);
            }''', '''            catch (OcrException exception)
            {
                errors.Add(exception.Message);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                errors.Add($"分流配置或目标不可用：{Path.GetFileName(configPath)}。");
            }''')
    text = replace(text, '''        string[] configuredLines = outputLines
            .Where''', '''        string[] configuredLines = outputLines
            .Select(line => line.EndsWith("（已分流）", StringComparison.Ordinal) ? line[..^5] : line)
            .Where''')
    text = replace(text, '        string targetFile = config.TargetFile.Replace("{issue}", issue.ToString(), StringComparison.Ordinal);', '''        if (issue <= 0 || !config.TargetFile.Contains("{issue}", StringComparison.Ordinal))
            throw new OcrException("分发目标必须包含动态 {issue} 期号。");
        string targetFile = config.TargetFile.Replace("{issue}", issue.ToString(), StringComparison.Ordinal);
        if (Path.GetFileName(targetFile) != targetFile || Path.IsPathRooted(targetFile))
            throw new OcrException("分发 targetFile 必须是目标目录内的文件名。");''')
    start = text.index('        string[] existingLines = await File.ReadAllLinesAsync(targetPath);')
    end = text.index('    private static readonly IReadOnlySet<string> EmptyResult', start)
    text = text[:start] + '''        string? marker = config.Placement?.Mode == "beforeLine"
            ? config.Placement.Marker.Replace("{issue}", issue.ToString(), StringComparison.Ordinal)
            : null;
        return await OwnedResultWriter.ApplyAsync(targetPath, sourceGroup!, configuredLines,
            marker, config.Placement?.BlankLineBeforeMarker == true);
    }

''' + text[end:]
    text = replace(text, '        return numbers.Length == expectedCount && numbers.All(number => int.TryParse(number, out _));', '''        return numbers.Length == expectedCount && numbers.Distinct(StringComparer.Ordinal).Count() == expectedCount
            && numbers.All(number => number.Length == 2 && number.All(c => c is >= '0' and <= '9')
                && int.TryParse(number, out int value) && value is >= 1 and <= 49);''')
    text = replace(text, "            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);", "            .Split(',', StringSplitOptions.TrimEntries);")
    text = text.replace('File.WriteAllLines(targetPath, updated, new UTF8Encoding(hasUtf8Bom));',
        'AtomicFile.WriteAllLines(targetPath, updated, new UTF8Encoding(hasUtf8Bom));')
    write(path, text)

    # Prefer an identified current-issue candidate over an otherwise preferred historical summary.
    path = 'OcrLineTool.App/LocalCandidatePlanner.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, '        IReadOnlyList<OcrRule> rules)\n    {', '        IReadOnlyList<OcrRule> rules, int? issue = null)\n    {')
    text = replace(text, '                    RuleEngine.HasValueForAnyIssue(lines, rule),',
        '                    RuleEngine.HasValueForAnyIssue(lines, rule),\n                    issue is int target && RuleEngine.ExtractFinalValue(lines, target, rule) is not null,')
    text = replace(text, '                .OrderByDescending(option => option.Rule.Type == "生肖" && option.IsSummary)',
        '                .OrderByDescending(option => option.HasTargetValue)\n                .ThenByDescending(option => option.Rule.Type == "生肖" && option.IsSummary)')
    text = replace(text, '        bool HasAnyValue,', '        bool HasAnyValue,\n        bool HasTargetValue,')
    write(path, text)
    path = 'OcrLineTool.App/MainForm.cs'
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    text = replace(text, 'LocalCandidatePlanner.Build(imagePaths, localResults, rules)',
        'LocalCandidatePlanner.Build(imagePaths, localResults, rules, issue)')
    write(path, text)

    write('OcrLineTool.Tests/AuditPipelineRegressionTests.cs', '''using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class AuditPipelineRegressionTests
{
    [Fact]
    public void ConflictsAreStickyAndNotFormattedAsSuccess()
    {
        var values = new ResultValues(StringComparer.Ordinal);
        ResultValues.AddTo(values, "测试", "鸡");
        ResultValues.AddTo(values, "测试", "狗");
        ResultValues.AddTo(values, "测试", "鸡");
        Assert.False(values.ContainsKey("测试"));
        Assert.Contains("冲突", Assert.Single(RuleEngine.FormatOutput([new("测试", "生肖")], values)));
    }

    [Fact]
    public void ReorderedIdenticalNumberSetsAreNotConflicts()
    {
        var values = new ResultValues(StringComparer.Ordinal);
        ResultValues.AddTo(values, "测试", "01 02 03");
        ResultValues.AddTo(values, "测试", "03 02 01");
        Assert.Empty(values.Conflicts);
        Assert.Equal("01 02 03", values["测试"]);
    }

    [Fact]
    public void ReplacedImageInvalidatesCloudCacheEvenWithSameMetadata()
    {
        using var files = new TemporaryFiles();
        string image = files.File("image.jpg", "abc");
        DateTime stamp = File.GetLastWriteTimeUtc(image);
        CloudOcrCacheStore.SaveEntry(files.Root, "群", 251, image, ["251期 狗"]);
        Assert.Single(CloudOcrCacheStore.Load(files.Root, "群", 251));
        File.WriteAllText(image, "xyz");
        File.SetLastWriteTimeUtc(image, stamp);
        Assert.Empty(CloudOcrCacheStore.Load(files.Root, "群", 251));
    }

    [Fact]
    public void CroppedInputCannotImpersonateAWholeImageCacheEntry()
    {
        using var files = new TemporaryFiles();
        string image = files.File("image.jpg", "original");
        string crop = files.File("crop.jpg", "crop");
        CloudOcrCacheStore.SaveEntry(files.Root, "群", 251, image, ["251期 狗"], crop);
        Assert.Empty(CloudOcrCacheStore.Load(files.Root, "群", 251));
    }

    [Fact]
    public void EmptyCloudResultsAreNotSuccessfulCacheEntries()
    {
        using var files = new TemporaryFiles();
        string image = files.File("image.jpg", "abc");
        CloudOcrCacheStore.SaveEntry(files.Root, "群", 251, image, []);
        Assert.Empty(CloudOcrCacheStore.Load(files.Root, "群", 251));
    }

    [Fact]
    public void IncompleteRetryCacheIsRejectedForEveryGroup()
    {
        MethodInfo method = typeof(MainForm).GetMethod("CanReuseRetryCloudLines", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (string group in new[] { "嫣然心水", "新澳高级会员", "新澳六合彩资料", "黄大仙新澳" })
            Assert.False((bool)method.Invoke(null, new object[] { group, new OcrRule[] { new("测试", "生肖") }, new[] { "250期 测试 狗" }, 251 })!);
    }

    [Fact]
    public void IdentifiedDedicatedFolderDoesNotRequireSmallToReadTheValue()
    {
        var rule = new OcrRule("小苹果", "生肖", RequiredKeyword: "小苹果", Folder: "小苹果");
        string image = Path.Combine("C:\\", "结果", "嫣然心水", "小苹果", "当前.jpg");
        Assert.Single(RuleEngine.FindMatches(image, ["小苹果", "看不清"], [rule]));
    }

    [Fact]
    public async Task CorrectionUpdatesOnlyOwnedRowInsteadOfAppendingContradiction()
    {
        using var files = new TemporaryFiles();
        string target = files.File("251.txt", "外部原文\\n");
        await OwnedResultWriter.ApplyAsync(target, "群A", ["鸡 作者"], null, false);
        await OwnedResultWriter.ApplyAsync(target, "群A", ["狗 作者"], null, false);
        Assert.Equal(new[] { "外部原文", "狗 作者" }, await System.IO.File.ReadAllLinesAsync(target));
    }

    [Fact]
    public async Task UnownedConflictingRowIsNotRemovedOrOverwritten()
    {
        using var files = new TemporaryFiles();
        string target = files.File("251.txt", "鸡 作者\\n外部原文\\n");
        byte[] before = await System.IO.File.ReadAllBytesAsync(target);
        await Assert.ThrowsAsync<OcrException>(() => OwnedResultWriter.ApplyAsync(target, "群A", ["狗 作者"], null, false));
        Assert.Equal(before, await System.IO.File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task IdenticalReplayDoesNotClaimAnExternalRow()
    {
        using var files = new TemporaryFiles();
        string target = files.File("251.txt", "鸡 作者\\n");
        await OwnedResultWriter.ApplyAsync(target, "群A", ["鸡 作者"], null, false);
        await Assert.ThrowsAsync<OcrException>(() => OwnedResultWriter.ApplyAsync(target, "群A", ["狗 作者"], null, false));
        Assert.Equal(new[] { "鸡 作者" }, await System.IO.File.ReadAllLinesAsync(target));
    }

    [Fact]
    public async Task ExternalEditToOwnedRowIsPreserved()
    {
        using var files = new TemporaryFiles();
        string target = files.File("251.txt", "");
        await OwnedResultWriter.ApplyAsync(target, "群A", ["鸡 作者"], null, false);
        await System.IO.File.WriteAllTextAsync(target, "虎 作者\\n");
        await Assert.ThrowsAsync<OcrException>(() => OwnedResultWriter.ApplyAsync(target, "群A", ["狗 作者"], null, false));
        Assert.Equal(new[] { "虎 作者" }, await System.IO.File.ReadAllLinesAsync(target));
    }

    [Fact]
    public async Task CanceledAtomicWritePreservesPreviousFile()
    {
        using var files = new TemporaryFiles();
        string target = files.File("result.txt", "旧结果");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicFile.WriteAllTextAsync(target, "新结果", null, cancellation.Token));
        Assert.Equal("旧结果", await System.IO.File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task NonZeroExitStillPreservesCudaError()
    {
        using var files = new TemporaryFiles();
        string image = files.File("image.png", "test");
        var runner = new ErrorRunner();
        var client = new PaddleLocalOcrClient(runner, Path.Combine(files.Root, "cache.json"));
        OcrException exception = await Assert.ThrowsAsync<OcrException>(() => client.RecognizeBatchAsync([image], useCache: false));
        Assert.Equal(PaddleLocalOcrClient.CudaUnavailableCode, exception.Code);
        Assert.Contains("CUDA", exception.Message);
    }

    [Fact]
    public void OnlyConfiguredSecretSlotsAreRequired()
    {
        using var files = new TemporaryFiles();
        string path = files.File("secrets.json", "{\\"tencent\\":{\\"A\\":{\\"secretId\\":\\"test-id\\",\\"secretKey\\":\\"test-secret\\"}}}");
        OcrSecrets secrets = OcrSecretsLoader.Load(path);
        Assert.Equal("test-id", secrets.Get(OcrProvider.Tencent, "A").Id);
        Assert.Throws<OcrException>(() => secrets.Get(OcrProvider.Baidu, "D"));
    }

    [Fact]
    public void DescribingSlotsDoesNotResolveSecrets()
    {
        Assert.Equal("腾讯云 A", CredentialSchedule.DescribeSlot(0).DisplayName);
        Assert.Empty(CredentialSchedule.DescribeSlot(0).Secret);
    }

    private sealed class ErrorRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, Action<string>? output, CancellationToken cancellationToken)
        {
            string path = System.Text.RegularExpressions.Regex.Match(startInfo.Arguments, "--output \\\"(?<path>[^\\\"]+)\\\"").Groups["path"].Value;
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(new { error = "无法启用 NVIDIA CUDA 设备" }));
            return Task.FromResult(new ProcessResult(true, 3, "", ""));
        }
    }

    private sealed class TemporaryFiles : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ns-audit-" + Guid.NewGuid().ToString("N"));
        internal TemporaryFiles() => Directory.CreateDirectory(Root);
        internal string File(string name, string text)
        {
            string path = Path.Combine(Root, name);
            System.IO.File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
''')
    write('docs/audit-phase4-applied.md', '# Audit repair phase 4\n\n'
        'Distribution updates rows by tracked source-group/label ownership, not full-line deduplication alone. '
        'Unknown conflicting rows and externally changed owned rows are preserved and reported. '
        'The writer uses a per-target cooperating-process lock, rechecks content before replacement, '
        'preserves supported text encodings, and keeps a previous-file backup. '
        'Owner metadata and target files are individually atomic, not a multi-file transaction; '
        'failure is reported rather than marked distributed. Non-cooperating external writers still require a shared protocol. '
        'Candidate preference uses the requested issue without rejecting identified difficult cards.\n')

MANIFEST.write_text(json.dumps(changed, ensure_ascii=False), encoding='utf-8')
