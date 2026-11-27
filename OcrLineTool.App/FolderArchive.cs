using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OcrLineTool;

/// <summary>
/// 目录级临时封存：某个群的某个子目录连续三天都没有新图，就把它临时封存——识别时整块跳过，
/// 群结果 TXT 和缺失汇总里都不再出现它的条目；这个目录重新出现新图就自动解封。
/// 判定只看图片：OCR 失败不算“没有数据”，纯文字消息也不算更新。
/// 只有“抓图程序当天确实跑过这个群”（抓取状态里的 date 是今天）才给目录记一天，
/// 免得掉线、关机几天就把所有目录封掉。手动识别、自动识别都不会解封。
/// 抓图程序不建空目录：某天一张图都没下到时子目录根本不存在，
/// 所以“今天没新图”必须按历史记录过的目录来记账，不能只看磁盘上现存的目录。
/// </summary>
internal sealed class FolderArchive
{
    private const int CurrentVersion = 1;
    // 连续多少个“抓图跑过、但该目录没有新图”的日期就封存。
    internal const int IdleDaysBeforeSealing = 3;
    // 抓图程序全量下载用的目录；规则没写 folder 时算在这个目录名下。
    internal const string AllImagesFolderName = "全部图片";

    private static readonly Regex FileDatePattern = new(@"^(\d{8})_", RegexOptions.CultureInvariant);

    public int Version { get; set; } = CurrentVersion;
    // 键 = 规范化群名 + "/" + 子目录名。
    public Dictionary<string, Entry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal sealed class Entry
    {
        // 该目录里出现过的最新图片日期（按图片文件名里的消息日期），只增不减。
        public string? LastImageDate { get; set; }
        // 已经累计了多少个“抓图跑过但没新图”的日期；一有新图就清零。
        public int IdleDays { get; set; }
        // 最近一次给这个目录记账的日期，保证一天只记一次。
        public string? LastCountedDate { get; set; }
        public bool Sealed { get; set; }
        public string? SealedDate { get; set; }
    }

    // 状态跟规则文件一起放在“配置文件”子目录：升级只换程序目录，不会带走它。
    internal static string PathFor(string appDirectory) =>
        Path.Combine(ResultFilePaths.ConfigurationDirectory(appDirectory), "目录封存.json");

    internal static FolderArchive Load(string appDirectory)
    {
        try
        {
            string path = PathFor(appDirectory);
            if (!File.Exists(path))
                return new();
            FolderArchive? archive = JsonSerializer.Deserialize<FolderArchive>(File.ReadAllText(path));
            if (archive is null)
                return new();
            archive.Normalize();
            return archive;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or JsonException or NotSupportedException)
        {
            // 状态文件坏了按“没有封存”处理：识别不能因为一个坏文件停摆。
            return new();
        }
    }

    internal void Save(string appDirectory)
    {
        Normalize();
        string path = PathFor(appDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(
            path,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(true));
    }

    internal static string KeyFor(string groupName, string folderName) =>
        RuleCatalog.NormalizeGroupName(groupName) + "/" + folderName.Trim();

    /// <summary>该群被封存的子目录名集合。</summary>
    internal IReadOnlySet<string> SealedFolders(string groupName)
    {
        string prefix = RuleCatalog.NormalizeGroupName(groupName) + "/";
        var sealedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, Entry entry) in Entries)
        {
            if (!entry.Sealed || key.Length <= prefix.Length)
                continue;
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            sealedFolders.Add(key[prefix.Length..]);
        }
        return sealedFolders;
    }

    internal bool IsSealed(string groupName, string folderName) =>
        Entries.TryGetValue(KeyFor(groupName, folderName), out Entry? entry) && entry.Sealed;

    /// <summary>
    /// 用户直接点进被封存的那个子目录（比如 11.27-蜻蜓一套\神算子）时也算封存：
    /// 否则会拿同群别的条目的规则去认这个目录的图，读不出来就一路写缺失。
    /// 点的是群目录本身时它的名字不是任何子目录名，不受影响。
    /// </summary>
    internal bool IsSelectedFolderSealed(string groupName, string? selectedFolder)
    {
        if (string.IsNullOrWhiteSpace(selectedFolder))
            return false;
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(selectedFolder));
        return !string.IsNullOrEmpty(name) && IsSealed(groupName, name);
    }

    internal int SealedCount(string groupName) => SealedFolders(groupName).Count;

    /// <summary>该群记录过的全部子目录名（含今天没出现、因而没下到图的目录）。</summary>
    internal IReadOnlyList<string> FolderNames(string groupName)
    {
        string prefix = RuleCatalog.NormalizeGroupName(groupName) + "/";
        var names = new List<string>();
        foreach (string key in Entries.Keys)
        {
            if (key.Length <= prefix.Length || !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            names.Add(key[prefix.Length..]);
        }
        return names;
    }

    private IReadOnlyList<string> GroupNames()
    {
        var names = new List<string>();
        foreach (string key in Entries.Keys)
        {
            int split = key.IndexOf('/');
            if (split > 0)
                names.Add(key[..split]);
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>这个群目录下面现在实际存在的子目录名。</summary>
    internal static IReadOnlyList<string> KnownFolders(string groupFolder)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(groupFolder))
            return names;
        try
        {
            foreach (string folder in Directory.EnumerateDirectories(groupFolder))
                names.Add(Path.GetFileName(folder));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            // 目录暂时读不到：当作没有更细的目录信息，规则按没被封存处理。
        }
        return names;
    }

    /// <summary>
    /// 该群涉及的全部子目录名：磁盘上现存的，加上历史记录过的。
    /// 后者决定“没有 folder 的规则”要不要一起收起来——全群的目录都被封了，这些条目也没了来源。
    /// </summary>
    internal IReadOnlyList<string> KnownFolders(string groupFolder, string groupName)
    {
        var names = new List<string>(FolderNames(groupName));
        foreach (string folder in KnownFolders(groupFolder))
        {
            if (!names.Contains(folder, StringComparer.OrdinalIgnoreCase))
                names.Add(folder);
        }
        return names;
    }

    /// <summary>
    /// 规则是否因为所在目录被封存而整条作废。规则没写 folder 时按“全部图片”归属——
    /// 抓图程序把全量图片都放在这个目录里，封了它就等于封了这些没有专属目录的条目；
    /// 群里没有「全部图片」时，只有所有图片目录都被封掉才把这些条目一起收起来。
    /// </summary>
    internal static bool IsRuleArchived(OcrRule rule, IReadOnlySet<string> sealedFolders,
        IReadOnlyCollection<string> knownFolders)
    {
        if (sealedFolders.Count == 0)
            return false;
        if (!string.IsNullOrWhiteSpace(rule.Folder))
            return sealedFolders.Contains(rule.Folder);
        if (sealedFolders.Contains(AllImagesFolderName))
            return true;
        return knownFolders.Count > 0 && knownFolders.All(sealedFolders.Contains);
    }

    internal IReadOnlyList<OcrRule> FilterRules(string groupName, IReadOnlyList<OcrRule> rules,
        IReadOnlyCollection<string> knownFolders)
    {
        IReadOnlySet<string> sealedFolders = SealedFolders(groupName);
        if (sealedFolders.Count == 0)
            return rules;
        return rules
            .Where(rule => !IsRuleArchived(rule, sealedFolders, knownFolders))
            .ToArray();
    }

    internal IReadOnlyList<string> FilterImages(string groupName, string groupFolder,
        IReadOnlyList<string> imagePaths)
    {
        IReadOnlySet<string> sealedFolders = SealedFolders(groupName);
        if (sealedFolders.Count == 0)
            return imagePaths;
        return imagePaths
            .Where(path => !IsImageArchived(groupFolder, path, sealedFolders))
            .ToArray();
    }

    internal static bool IsImageArchived(string groupFolder, string imagePath,
        IReadOnlySet<string> sealedFolders)
    {
        string relative = Path.GetRelativePath(groupFolder, imagePath);
        int separator = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        // 群根目录里的图不归任何子目录，照旧识别。
        return separator >= 0 && sealedFolders.Contains(relative[..separator]);
    }

    /// <summary>
    /// 重新核对一遍封存状态：出现新图就解封；抓图跑过又连续三天没新图就封存。
    /// 返回状态是否有变化（有变化才写盘）。
    /// </summary>
    internal static bool Refresh(FolderArchive archive, string appDirectory, string imageRoot,
        DateOnly today, Action<string>? onNotice = null)
    {
        if (string.IsNullOrWhiteSpace(imageRoot) || !Directory.Exists(imageRoot))
            return false;
        string todayText = DateText(today);
        bool changed = false;

        // 今天实际出现的群目录与子目录：新目录在这里登记，新图在这里解封。
        var present = new Dictionary<string, List<(string Name, string Path)>>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> groupFolders;
        try
        {
            groupFolders = Directory.EnumerateDirectories(imageRoot).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        foreach (string groupFolder in groupFolders)
        {
            string groupName = RuleCatalog.GroupNameForFolder(appDirectory, groupFolder);
            if (string.IsNullOrWhiteSpace(groupName))
                continue;
            if (!present.TryGetValue(groupName, out var folders))
                present[groupName] = folders = new List<(string Name, string Path)>();
            foreach (string name in KnownFolders(groupFolder))
                folders.Add((name, Path.Combine(groupFolder, name)));
        }

        // 历史记录里出现过的群也要过一遍：那个群今天可能一张图都没下到，群目录根本不存在。
        var groups = new List<string>(present.Keys);
        foreach (string groupName in archive.GroupNames())
        {
            if (!groups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
                groups.Add(groupName);
        }

        foreach (string groupName in groups)
        {
            if (present.TryGetValue(groupName, out List<(string Name, string Path)>? folders))
            {
                foreach ((string name, string path) in folders)
                    changed |= Register(archive, groupName, name, path, onNotice);
            }
            // 抓图程序当天确实跑过这个群，才有资格给“今天没有新图”记一天。
            if (!DidCaptureRunOn(imageRoot, groupName, todayText))
                continue;
            foreach (string name in archive.FolderNames(groupName))
                changed |= CountIdleDay(archive, groupName, name, todayText, onNotice);
        }
        return changed;
    }

    /// <summary>登记目录、刷新它最新的图片日期；日期往前走就是来了新图，同时解封。</summary>
    private static bool Register(FolderArchive archive, string groupName, string folderName,
        string folderPath, Action<string>? onNotice)
    {
        Entry entry = archive.EnsureEntry(groupName, folderName, out bool created);
        bool changed = created;
        string? newest = NewestImageDate(folderPath);
        if (newest is null
            || (entry.LastImageDate is not null && string.CompareOrdinal(newest, entry.LastImageDate) <= 0))
            return changed;
        entry.LastImageDate = newest;
        entry.IdleDays = 0;
        changed = true;
        if (!entry.Sealed)
            return true;
        entry.Sealed = false;
        entry.SealedDate = null;
        onNotice?.Invoke($"「{groupName}」的目录「{folderName}」又来了新图，已自动解封。");
        return true;
    }

    /// <summary>给“今天也没有新图”记一天；攒满三天就地封存。</summary>
    private static bool CountIdleDay(FolderArchive archive, string groupName, string folderName,
        string todayText, Action<string>? onNotice)
    {
        Entry entry = archive.EnsureEntry(groupName, folderName, out bool created);
        bool changed = created;
        if (entry.Sealed)
            return changed;
        // 今天就有新图，或者今天已经记过一次账，都不再加。
        if (string.Equals(entry.LastImageDate, todayText, StringComparison.Ordinal)
            || string.Equals(entry.LastCountedDate, todayText, StringComparison.Ordinal))
            return changed;
        entry.LastCountedDate = todayText;
        entry.IdleDays += 1;
        changed = true;
        if (entry.IdleDays < IdleDaysBeforeSealing)
            return changed;
        entry.Sealed = true;
        entry.SealedDate = todayText;
        onNotice?.Invoke($"「{groupName}」的目录「{folderName}」连续 {IdleDaysBeforeSealing} 天没有新图，已临时封存。");
        return true;
    }

    private Entry EnsureEntry(string groupName, string folderName, out bool created)
    {
        string key = KeyFor(groupName, folderName);
        if (Entries.TryGetValue(key, out Entry? entry))
        {
            created = false;
            return entry;
        }
        entry = new Entry();
        Entries[key] = entry;
        created = true;
        return entry;
    }

    /// <summary>目录里最新的图片日期（按文件名里的消息日期），没有图片时返回 null。</summary>
    private static string? NewestImageDate(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return null;
        string? newest = null;
        foreach (string path in ImageFolderScanner.Scan(folderPath))
        {
            Match match = FileDatePattern.Match(Path.GetFileName(path));
            if (!match.Success
                || !DateOnly.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateOnly date))
                continue;
            string text = DateText(date);
            if (newest is null || string.CompareOrdinal(text, newest) > 0)
                newest = text;
        }
        return newest;
    }

    private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// 抓图程序当天确实跑过这个群：抓取状态文件的 date 就是北京时间当天。
    /// 找不到状态文件就当作“没跑过”，这一天不记账、不封存。
    /// </summary>
    internal static bool DidCaptureRunOn(string imageRoot, string groupName, string todayText)
    {
        string? statusFile = CaptureStatusFile(imageRoot, groupName);
        if (statusFile is null)
            return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(statusFile));
            return document.RootElement.TryGetProperty("date", out JsonElement date)
                && date.ValueKind == JsonValueKind.String
                && string.Equals(date.GetString(), todayText, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    // 抓图程序把状态写在 <抓图根>\..\outputs\抓取状态\<群名>抓取状态.json。
    private static string? CaptureStatusFile(string imageRoot, string groupName)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(imageRoot));
        string? parent = Path.GetDirectoryName(root);
        if (parent is null)
            return null;
        string directory = Path.Combine(parent, "outputs", "抓取状态");
        if (!Directory.Exists(directory))
            return null;
        string direct = Path.Combine(directory, groupName + "抓取状态.json");
        if (File.Exists(direct))
            return direct;
        // 文件名偶尔跟群名不完全一样：按 group_name 字段再找一遍。
        foreach (string path in Directory.EnumerateFiles(directory, "*抓取状态.json"))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("group_name", out JsonElement name)
                    && name.ValueKind == JsonValueKind.String
                    && string.Equals(RuleCatalog.NormalizeGroupName(name.GetString() ?? string.Empty),
                        RuleCatalog.NormalizeGroupName(groupName), StringComparison.OrdinalIgnoreCase))
                    return path;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or JsonException or NotSupportedException)
            {
                // 单个坏文件不影响其它群的状态读取。
            }
        }
        return null;
    }

    private void Normalize()
    {
        Entries ??= new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var merged = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, Entry entry) in Entries)
        {
            if (string.IsNullOrWhiteSpace(key) || entry is null)
                continue;
            int split = key.IndexOf('/');
            if (split <= 0 || split == key.Length - 1)
                continue;
            merged[KeyFor(key[..split], key[(split + 1)..])] = entry;
        }
        Entries = merged;
    }
}
