using System.Text.RegularExpressions;

namespace OcrLineTool;

public sealed record OcrRule(
    string Keyword,
    string Type,
    string? Label = null,
    string? Section = null,
    string? RequiredKeyword = null,
    string? Folder = null,
    bool IgnoreIssue = false,
    bool AllowNearbyValue = false,
    bool AllowValueWithoutKeyword = false,
    bool StrictIssueBlock = false,
    bool SingleValuePerIssue = false,
    IReadOnlyList<string>? PeerKeywords = null,
    IReadOnlyList<string>? RequiredKeywordsAny = null,
    bool AllowFolderIdentity = false,
    bool SkipConflictingRows = false,
    bool AllowIssueLessSummary = false,
    bool StopAtPlus = false,
    bool TenZodiacCombo = false,
    bool PrimaryOnly = false,
    bool HeaderIdentity = false,
    bool MatchByRowStructure = false,
    bool RequireRowStructure = false,
    bool DedupeNumbers = false,
    bool AllowOpeningRow = false,
    bool SectionOnRow = false,
    bool LockToFolder = false,
    string? Extractor = null,
    string? ReadModel = null)
{
    public string Id => Label ?? Keyword;
    public string OutputLabel => Label ?? Keyword;
}

public enum RuleExtractionStatus
{
    Missing,
    Success,
    Conflict
}

public sealed record RuleExtractionResult(RuleExtractionStatus Status, string? Value)
{
    public static RuleExtractionResult Missing { get; } = new(RuleExtractionStatus.Missing, null);
    public static RuleExtractionResult Conflict { get; } = new(RuleExtractionStatus.Conflict, null);
    public static RuleExtractionResult Success(string value) => new(RuleExtractionStatus.Success, value);
}

public static class RuleEngine
{
    // 正则回溯上限：云 OCR 返回超长或病态文本时，没有上限的正则会卡住 UI 线程且
    // 无法取消。5 秒对正常卡片文本远远够用，超时按“取不到值”处理。
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex IssueRegex = new(@"(?<!\d)(?<issue>\d{1,6})\s*期", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex CompactYearIssueRegex = new(@"(?<!\d)20\d{2}(?<issue>\d{3})\s*期", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex BareIssueRegex = new(
        @"^\s*[【\[（({]?\s*(?<issue>\d{3,6})(?!\d)(?!\s*\*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    // OCR may merge a one-digit row index into the period column of a
    // vertical list ("5253杀4合" = row 5, 253期). A trailing space or bracket
    // still counts as a standalone bare number and must not be split.
    // 两列卡的左列也可能是"期号+字段"（"261杀蓝单" = 261期），期号后直接跟汉字，
    // 所以只禁止后面紧跟数字，仍不允许把更长数字拆成期号。
    private static readonly Regex MergedRowIndexIssueRegex = new(
        @"^\s*[【\[（({]?\s*(?:(?<row>[1-9]|1[0-3])\s*)?(?<issue>\d{3})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex YearIssueRegex = new(@"^\s*\d{4}\s*[-—/]\s*(?<issue>\d{3,6})(?!\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    // 期号块边界：提取与失败原因共用同一套（不要在诊断里另写宽松的「截几行」解析器）。
    private static readonly Regex IssueBlockRegex = new(
        @"(?<!\d)(?:第\s*)?(?<issue>\d{1,6})\s*期|(?m:^\s*(?<issue>\d{3})(?!\d)(?=\s|$))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    internal const string Zodiac = "马蛇龙兔虎牛鼠猪狗鸡猴羊";
    private const string ConflictMarker = "OCR-CONFLICT";
    // Only reviewed layouts may place part of one physical number row before
    // its issue cell. Generic rules must never borrow heading/previous data.
    private static readonly IReadOnlySet<string> SplitIssueNumberRuleIds =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "宝典", "心水", "内幕", "强哥", "锁妖", "赛马会", "龙王", "红人馆", "老人味",
            "表弟", "祥瑞阁", "小马哥", "杀料", "金钱网", "天线宝杀", "翩翩公子杀十码",
            "狗庄", "宝典杀", "聚彩", "龙王杀", "姨妈", "藏宝十二码",
            // 六合日报两张号码卡：期号格与开奖栏都居中，号码行会被期号行和「开??」
            // 从中间切开（如 292期/开?? 插在 36 码或 8 码之间），只有按已复核的
            // 分段规则合并同一目标期行才能完整取数；数量、唯一性和 01～49 校验不变。
            "六合日报三六", "六合杀八码"
        };

    // 值行印在期号行上方的两列卡（雁塔题名）：允许把紧邻的上一行并入候选，
    // 只有经复核的这几条规则允许向前合并。
    private static readonly HashSet<string> BackwardValueLineRuleIds =
        new(StringComparer.Ordinal) { "雁塔题名杀头", "雁塔题名半波", "雁塔题名杀合" };

    // 黑色六合彩、嫣然电报：这几张卡的版面是「标题 → 数据行 → 期号格」，
    // 数据印在期号行上方（与项目里绝大多数「期号在上、数据在下」的卡相反）。
    // 只有下列已复核的 Id 允许把期号块向前扩一行取值，其余规则的数据行归属不变。
    private static readonly HashSet<string> AboveIssueValueRuleIds =
        new(StringComparer.Ordinal)
        {
            "天宫杀头", "锦囊杀尾", "锦囊半波", "黑客九肖", "黑客杀尾",
            "公式杀一肖", "公式杀尾", "公式半波", "公式杀行", "公式杀头",
            "嫣然电报十码必杀", "买什么杀一肖",
            // 这两张卡的数据行从期号上方一直跨到下方（十码的括号、半波的方括号
            // 都被期号格切开），必须把紧邻的上一行并进本期块才完整。
            "锦囊杀十码", "黑客半波"
        };

    // 经复核的多区卡：分区与带边界的行读会把同一物理行/同一字段切进互不相连
    // 的区域，需要再按几何顺序整页顺读一次。
    private static readonly HashSet<string> ReviewedWholeReadingRuleIds =
        new(StringComparer.Ordinal)
        {
            "雁塔题名杀头", "雁塔题名半波", "雁塔题名杀合", "雁塔题名杀肖肖",
            "恩平杀一尾", "简单爱", "爱晚亭"
        };

    // 黑色六合彩、嫣然电报：数据行横跨期号格、甚至被拆成单字符，按识别区域分组
    // 会把跨行字段切散。只对这几个新群的已复核 Id 在证据路径上先按整页行序取一次；
    // 取不到仍回落到原来的区域分组，既有整页卡（雁塔题名等）完全不受影响。
    private static readonly HashSet<string> NewGroupWholePageRuleIds =
        new(StringComparer.Ordinal)
        {
            "黑客杀尾", "黑客杀两肖", "黑客九肖", "锦囊杀十码", "黑客半波", "红牛四行", "黑客四行",
            "公式杀一肖", "公式杀尾", "公式半波", "公式杀行", "公式杀头",
            "嫣然电报十码必杀", "买什么杀一肖", "天宫杀头", "锦囊杀尾", "锦囊半波"
        };

    private static readonly HashSet<string> SingleValueScopedTypes =
        new(StringComparer.Ordinal) { "尾" };

    // 云 OCR 常把相邻两期并成一行（“257期杀，0尾07 258期杀，5尾”）。单值字段
    // 必须只取目标期号之后、下一个期号之前的那一段，否则会把邻期的值当成本期值。
    // 目标期在行首（at == 0）时同样要截断，否则 257 会把 258 的“5尾”一起读进来
    // 判成冲突；这里用任意期号（不限目标期）找下一个边界。
    private static string ScopeSingleValueToTargetIssue(string line, int issue, OcrRule rule)
    {
        if (!SingleValueScopedTypes.Contains(rule.Type))
            return line;
        int at = LastIssueIndex(line, issue);
        if (at < 0)
            return line;
        string scoped = line[at..];
        int next = NextIssueBoundaryIndex(scoped, issue);
        if (next > 0)
        {
            // 已经找到下一个期号边界：即使切完很短也只用切出来的这一段，
            // 绝不能回退整行（否则邻期的值又被读回来）。
            scoped = scoped[..next].Trim();
            return scoped;
        }
        scoped = scoped.Trim();
        return scoped.Length >= 2 ? scoped : line;
    }

    // 目标期片段内“下一个期号”的起点；返回 -1 表示片段里没有别的期号。
    // 跳过目标期自身（可能被 OCR 重复识别成两段），只认后面出现的其它期号。
    private static int NextIssueBoundaryIndex(string segment, int issue)
    {
        int boundary = -1;
        foreach (Regex pattern in new[] { IssueRegex, CompactYearIssueRegex, YearIssueRegex })
        {
            foreach (Match match in pattern.Matches(segment))
            {
                if (!int.TryParse(match.Groups["issue"].Value, out int actual) || actual == issue)
                    continue;
                if (boundary < 0 || match.Index < boundary)
                    boundary = match.Index;
            }
        }
        return boundary;
    }

    // 这一行里是否还印着别的期号。只要同行出现别的期号，就不允许回退整行：
    // 回退会把那个期号的值读成目标期的值（假成功）或凑出假冲突。
    // 判断覆盖期号在前和在后两种顺序（"256期杀，0尾 257期杀" 与
    // "257期杀，0尾07 258期杀，5尾" 都不许回退）。
    private static bool LineHasOtherIssue(string line, int issue)
    {
        foreach (Regex pattern in new[] { IssueRegex, CompactYearIssueRegex, YearIssueRegex })
        {
            foreach (Match match in pattern.Matches(line))
            {
                if (!int.TryParse(match.Groups["issue"].Value, out int actual) || actual != issue)
                    return true;
            }
        }
        return false;
    }

    // 期号行/行号行的判定：带“期”的期号，或以 3～6 位数字开头的行（含行号
    // 与期号粘连的 “5253杀绿单”）。向前合并值行时必须在这类行前停下。
    private static bool LooksLikeIssueRow(string line)
    {
        if (ContainsAnyIssue(line))
            return true;
        string trimmed = SimplifyOcrText(line).Trim();
        return Regex.IsMatch(trimmed, @"^[【\[（(]?\s*\d{3,6}(?!\d)")
            || MergedRowIndexIssueRegex.IsMatch(trimmed);
    }

    private static int LastIssueIndex(string line, int issue)
    {        int last = -1;
        foreach (Regex pattern in new[] { IssueRegex, CompactYearIssueRegex, YearIssueRegex })
        {
            foreach (Match match in pattern.Matches(line))
            {
                if (int.TryParse(match.Groups["issue"].Value, out int actual) && actual == issue)
                    last = Math.Max(last, match.Index);
            }
        }
        Match bare = BareIssueRegex.Match(line);
        if (bare.Success
            && int.TryParse(bare.Groups["issue"].Value, out int bareIssue)
            && bareIssue == issue)
            last = Math.Max(last, bare.Index);
        return last;
    }

    public static IReadOnlyList<OcrRule> FindMatches(IEnumerable<string> localLines, IEnumerable<OcrRule> rules)
    {
        string text = Normalize(string.Concat(localLines));
        return rules.Where(rule => MatchesText(text, rule)).ToArray();
    }

    public static IReadOnlyList<OcrRule> FindMatches(
        string imagePath,
        IEnumerable<string> localLines,
        IEnumerable<OcrRule> rules,
        IEnumerable<OcrRule>? completeRules = null)
    {
        string[] lines = localLines.ToArray();
        OcrRule[] requestedRules = rules.ToArray();
        OcrRule[] identityRules = completeRules?.ToArray() ?? [];
        bool hasCompleteIdentityCatalog = completeRules is not null;
        string text = Normalize(string.Concat(lines));
        // 嫣然心水: the subfolder alone is not identity when the image actually
        // shows another material's name and none of this rule's own names.
        bool guardForeignIdentity = RuleCatalog.PathBelongsToGroup(imagePath, "嫣然心水");
        HashSet<string> presentIdentities = guardForeignIdentity
            ? identityRules.Where(other => ContainsKeyword(text, Normalize(other.Keyword)))
                .Select(other => Normalize(other.Keyword))
                .Where(value => value.Length > 0)
                .ToHashSet(StringComparer.Ordinal)
            : [];
        HashSet<string> exactIdentityIds = hasCompleteIdentityCatalog
            ? identityRules.Where(item => HasExactIdentity(text, item))
                .Select(item => item.Id).ToHashSet(StringComparer.Ordinal)
            : [];
        return requestedRules.Where(rule =>
        {
            string expectedFolder = rule.Folder ?? rule.Keyword;
            bool folderMatches = MatchesRuleFolder(imagePath, rule);
            if (!folderMatches)
                return string.IsNullOrWhiteSpace(rule.Folder) && MatchesText(text, rule);
            if (guardForeignIdentity && YanranAuthorCards.Supports(rule))
                return !presentIdentities.Any(value => value != Normalize(rule.Keyword))
                    && YanranAuthorCards.HasIdentity(lines, rule)
                    && YanranAuthorCards.HasRows(lines, rule);
            // 复核卡（require_row_structure）：第一关子文件夹已过，第二关必须是
            // 规定行格式的图；水印/标题不再单独构成入选，杂格式的统计卡不入选。
            if (rule.RequireRowStructure)
                return !presentIdentities.Any(value => value != Normalize(rule.Keyword))
                    && MatchesRequiredRowFormat(lines, rule);
            // The shared 紫燕儿 card contains both zodiac and tail sections.
            // Small may split/drop the last character of 杀一尾. This is only
            // candidate identity; medium must still read the complete field.
            bool sharedTailIdentity = guardForeignIdentity && rule.AllowFolderIdentity
                && rule.Type == "尾" && rule.Section == "杀一尾"
                && Normalize(rule.Keyword) == "紫燕儿"
                && ContainsKeywordExact(text, Normalize(rule.Keyword))
                && Regex.IsMatch(text, @"(?<!\d)\d{1,6}期杀(?:一|1)?[0-9]尾");
            // A rule that only matches fuzzily must not steal a sibling
            // material's card when that sibling matches the image exactly.
            // 例外：靠文件夹+行形状认卡的无标题规则（爱晚亭杀肖）——同夹水印会让
            // 兄弟规则成为精确身份，本规则因此永远不在精确身份里；再用兄弟否决就
            // 会把本规则自己的卡也否决掉。放宽后身份仍由下面的
            // allow_folder_identity 分支按行形状确认，别的资料名的卡照旧排除。
            if (hasCompleteIdentityCatalog
                && !exactIdentityIds.Contains(rule.Id)
                && !(rule.AllowFolderIdentity && rule.MatchByRowStructure)
                && !sharedTailIdentity
                && HasExactSiblingIdentity(rule, expectedFolder, identityRules, exactIdentityIds))
            {
                return false;
            }
            // Title-less cards (the 斩杀系列 strips carry only data rows): the
            // folder plus the rule's own row shape is the identity. Only an
            // explicit opt-in rule may match this way, and never when a sibling
            // material's name is actually printed on the image.
            if (rule.MatchByRowStructure
                && hasCompleteIdentityCatalog
                && RowStructureMatches(lines, rule)
                && !identityRules.Any(other =>
                    !other.Id.Equals(rule.Id, StringComparison.Ordinal)
                    && (other.Folder ?? other.Keyword).Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)
                    && ContainsKeywordExact(text, Normalize(other.Keyword))))
            {
                return true;
            }
            bool requiredRepeatsFolder = !string.IsNullOrWhiteSpace(rule.RequiredKeyword)
                && RuleCatalog.NormalizeGroupName(rule.RequiredKeyword)
                    .Equals(RuleCatalog.NormalizeGroupName(expectedFolder), StringComparison.OrdinalIgnoreCase);
            string requiredKeyword = rule.RequiredKeyword ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(requiredKeyword)
                && !(guardForeignIdentity && rule.AllowFolderIdentity)
                && (guardForeignIdentity || !requiredRepeatsFolder)
                && !RequiredKeywordMatches(text, requiredKeyword, rule))
                return false;
            if (rule.RequiredKeywordsAny is { Count: > 0 }
                && !sharedTailIdentity
                && !rule.RequiredKeywordsAny.Any(value => RequiredKeywordMatches(text, value, rule)))
                return false;
            if (guardForeignIdentity && rule.AllowFolderIdentity)
            {
                // Cards in this dedicated folder print no material name; the
                // folder plus the rule type is the identity. A card showing a
                // different material's name is not this rule's image.
                string ownFolderKeyword = Normalize(rule.Keyword);
                if (presentIdentities.Any(value => !value.Equals(ownFolderKeyword, StringComparison.Ordinal)))
                    return false;
                // 无标题卡（爱晚亭杀肖）：卡面没有"杀1肖"字样时，文件夹 + 每期一行
                // 的单肖统计表形状才算命中；同夹的号码表/万能猫统计表不能顶替。
                // 老卡面（标题印了"杀1肖"）按原身份判定放行，不回退。
                return !rule.MatchByRowStructure
                    || LooksLikePerIssueZodiacTable(lines)
                    || text.Contains("杀1肖", StringComparison.Ordinal);
            }
            bool explicitIdentity = MatchesText(text, rule)
                && (string.IsNullOrWhiteSpace(rule.Section)
                    || ContainsSectionMarker(text, rule.Section));
            if (explicitIdentity)
                return true;
            return HasValueForAnyIssue(lines, rule)
                || (!string.IsNullOrWhiteSpace(rule.RequiredKeyword)
                    && !RuleCatalog.NormalizeGroupName(rule.RequiredKeyword).Equals(
                        RuleCatalog.NormalizeGroupName(expectedFolder), StringComparison.OrdinalIgnoreCase)
                    && ContainsKeyword(text, Normalize(rule.RequiredKeyword))
                    && (hasCompleteIdentityCatalog
                        && identityRules.Count(other => (other.Folder ?? other.Keyword).Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)
                            && Normalize(other.RequiredKeyword ?? other.Keyword) == Normalize(rule.RequiredKeyword)) == 1
                        || !string.IsNullOrWhiteSpace(rule.Section) && ContainsSectionMarker(text, rule.Section)))
                || (hasCompleteIdentityCatalog
                    && identityRules.Count(other => (other.Folder ?? other.Keyword).Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)) == 1
                    && expectedFolder.Equals(rule.RequiredKeyword ?? rule.Keyword, StringComparison.OrdinalIgnoreCase));
        }).ToArray();
    }

    // 目录身份判定（候选识别与缺项补读共用同一套判断）：目录段精确相等，
    // 允许规则的资料夹出现在图片的任一级祖先目录（作者\子目录\图片.jpg 仍属于该作者），
    // 但绝不按名称包含或前缀相等放宽。
    public static bool MatchesRuleFolder(string imagePath, OcrRule rule)
    {
        string expectedFolder = rule.Folder ?? rule.Keyword;
        if (string.IsNullOrWhiteSpace(expectedFolder))
            return false;
        string parent = Path.GetFileName(Path.GetDirectoryName(imagePath)) ?? string.Empty;
        return parent.Equals(expectedFolder, StringComparison.OrdinalIgnoreCase)
            || DirectoryAncestors(imagePath).Any(item =>
                item.Equals(expectedFolder, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> DirectoryAncestors(string imagePath)
    {
        string? directory = Path.GetDirectoryName(imagePath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            string? name = Path.GetFileName(directory);
            if (!string.IsNullOrWhiteSpace(name))
                yield return name;
            directory = Path.GetDirectoryName(directory);
        }
    }

    public static bool HasValueForAnyIssue(IEnumerable<string> lines, OcrRule rule)
    {
        string[] snapshot = lines.ToArray();
        if (rule.IgnoreIssue)
            return ExtractFinalValue(snapshot, 1, rule) is not null;
        return FindIssues(snapshot).Where(foundIssue => foundIssue > 0).Distinct()
            .Any(foundIssue => ExtractFinalValue(snapshot, foundIssue, rule) is not null);
    }

    public static int? DetectIssueMismatch(
        IEnumerable<IReadOnlyList<string>> cloudResults,
        int selectedIssue,
        int requiredCount = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(selectedIssue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requiredCount);
        try
        {
            return DetectIssueMismatchCore(cloudResults, selectedIssue, requiredCount);
        }
        catch (RegexMatchTimeoutException)
        {
            // 正则超时按“无法判定期号不一致”处理，绝不能中断整轮识别。
            return null;
        }
    }

    private static int? DetectIssueMismatchCore(
        IEnumerable<IReadOnlyList<string>> cloudResults,
        int selectedIssue,
        int requiredCount)
    {
        IReadOnlyList<string>[] samples = cloudResults.Take(requiredCount).ToArray();
        if (samples.Length < requiredCount)
            return null;

        var latestIssues = new List<int>(requiredCount);
        foreach (IReadOnlyList<string> sample in samples)
        {
            if (!sample.Any(line => !string.IsNullOrWhiteSpace(line)))
                return null;
            int[] issues = FindIssues(sample).Distinct().ToArray();
            if (issues.Contains(selectedIssue))
                return null;
            if (issues.Length > 0)
                latestIssues.Add(issues.Max());
        }

        IGrouping<int, int>? likelyIssue = latestIssues
            .GroupBy(issue => issue)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .FirstOrDefault();
        return likelyIssue?.Count() >= (requiredCount + 1) / 2
            ? likelyIssue.Key
            : null;
    }

    // 注意：不能写成带 yield 的惰性迭代器——yield return 不允许出现在带 catch 的 try 里，
    // 而这里必须能吞掉正则回溯超时（YanranAuthorCards/SummaryRowRecovery 会直接调用本方法，
    // 超时若逃出去会被记成“未预期错误”而不是按缺失降级）。语句数量很少，物化开销可忽略。
    internal static IEnumerable<int> FindIssues(IEnumerable<string> lines)
    {
        var found = new List<int>();
        try
        {
            foreach (string line in lines)
            {
                foreach (Match match in IssueRegex.Matches(line))
                {
                    if (int.TryParse(match.Groups["issue"].Value, out int issue))
                        found.Add(issue);
                }

                foreach (Match match in CompactYearIssueRegex.Matches(line))
                {
                    if (int.TryParse(match.Groups["issue"].Value, out int issue))
                        found.Add(issue);
                }

                Match leading = BareIssueRegex.Match(line);
                if (leading.Success
                    && int.TryParse(leading.Groups["issue"].Value, out int leadingIssue))
                    found.Add(leadingIssue);

                Match yearIssue = YearIssueRegex.Match(line);
                if (yearIssue.Success
                    && int.TryParse(yearIssue.Groups["issue"].Value, out int yearIssueNumber))
                    found.Add(yearIssueNumber);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // 病态文本：按“已扫到的部分期号”返回，绝不中断整轮识别。
        }

        return found;
    }

    private static bool MatchesText(string text, OcrRule rule)
    {
        if (rule.StrictIssueBlock)
            text = SimplifyFixedCardText(text);
        // The generic portrait title must not borrow another card's longer
        // title. Its identity cannot depend on last week's displayed zodiac.
        if (rule.StrictIssueBlock && rule.Keyword == "禁肖图"
            && Regex.IsMatch(text, "(?:佛祖|三怪|澳门)禁肖图"))
            return false;
        bool identityMatched = ContainsKeyword(text, Normalize(rule.Keyword));
        if (!identityMatched
            && IsSummaryText(text)
            && !string.IsNullOrWhiteSpace(rule.Label))
        {
            identityMatched = ContainsKeyword(text, Normalize(rule.Label));
        }

        return identityMatched
            && (string.IsNullOrWhiteSpace(rule.RequiredKeyword)
                || RequiredKeywordMatches(text, rule.RequiredKeyword, rule))
            && (rule.RequiredKeywordsAny is not { Count: > 0 }
                || rule.RequiredKeywordsAny.Any(value => RequiredKeywordMatches(text, value, rule)));
    }

    public static bool IsZodiacSummary(IEnumerable<string> lines) =>
        IsSummaryText(Normalize(string.Concat(lines)));

    private static bool IsSummaryText(string text) =>
        text.Contains("统计表", StringComparison.Ordinal)
        || text.Contains("統計表", StringComparison.Ordinal)
        || text.Contains("禁肖统计", StringComparison.Ordinal)
        || text.Contains("禁肖統計", StringComparison.Ordinal);

    private static bool ContainsKeyword(string text, string keyword)
    {
        if (text.Contains(keyword, StringComparison.Ordinal))
            return true;
        // Common OCR look-alikes may not match even at one edit either
        // (e.g. 小惠慧/小慧慧, 新奥/新澳). Identity matching only; business
        // values never pass through this normalization.
        string confusedText = ApplyIdentityOcrConfusions(text);
        string confusedKeyword = ApplyIdentityOcrConfusions(keyword);
        if ((!string.Equals(confusedText, text, StringComparison.Ordinal)
                || !string.Equals(confusedKeyword, keyword, StringComparison.Ordinal))
            && confusedText.Contains(confusedKeyword, StringComparison.Ordinal))
            return true;
        if (keyword.Length < 4)
            return false;

        int minimumLength = Math.Max(1, keyword.Length - 1);
        int maximumLength = keyword.Length + 1;
        for (int length = minimumLength; length <= maximumLength; length++)
        {
            for (int start = 0; start + length <= text.Length; start++)
            {
                if (WithinOneEdit(text.AsSpan(start, length), keyword.AsSpan()))
                    return true;
            }
        }
        return false;
    }

    private static bool ContainsSectionMarker(string text, string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
            return true;
        string normalized = Normalize(text);
        return SectionAliases(section).Any(alias => alias.Length > 0
            && normalized.Contains(alias, StringComparison.Ordinal));
    }

    private static bool ContainsAnyAlias(string text, IEnumerable<string> aliases)
    {
        string normalized = Normalize(text);
        return aliases.Any(alias => ContainsKeyword(normalized, alias));
    }

    private static bool ContainsRuleIdentity(IEnumerable<string> lines, OcrRule rule)
    {
        string[] identities = new[] { rule.Keyword, rule.Label ?? string.Empty }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return identities.Length > 0 && lines.Any(line => ContainsAnyAlias(line, identities));
    }

    private static IReadOnlyList<string> SectionAliases(string section)
    {
        string normalized = Normalize(section);
        var aliases = new List<string> { normalized };
        Match month = Regex.Match(normalized, "^(?<month>\\d{1,2})月份$");
        if (month.Success)
        {
            string number = month.Groups["month"].Value;
            aliases.Add(number + "份");
            aliases.Add(number + "月");
        }
        return aliases.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string ApplyIdentityOcrConfusions(string text) => text
        .Replace('惠', '慧')
        .Replace('奥', '澳')
        .Replace('叶', '卟')
        .Replace('啊', '阿');

    // Exact identity after the known OCR look-alike normalization, without the
    // one-edit tolerance. Required identities use this so that a sibling
    // material whose name differs by one character cannot hijack the card.
    private static bool ContainsKeywordExact(string text, string keyword)
    {
        if (text.Contains(keyword, StringComparison.Ordinal))
            return true;
        string confusedText = ApplyIdentityOcrConfusions(text);
        string confusedKeyword = ApplyIdentityOcrConfusions(keyword);
        return (!string.Equals(confusedText, text, StringComparison.Ordinal)
                || !string.Equals(confusedKeyword, keyword, StringComparison.Ordinal))
            && confusedText.Contains(confusedKeyword, StringComparison.Ordinal);
    }

    private static bool RequiredKeywordMatches(string text, string requiredKeyword, OcrRule rule) =>
        // One-edit OCR tolerance stays available to every required keyword. A
        // rule that only matches fuzzily is kept away from a sibling's card by
        // the exact-identity guard in FindMatches.
        ContainsKeyword(text, Normalize(requiredKeyword))
        || ContainsSectionMarker(text, requiredKeyword);

    private static bool HasExactIdentity(string text, OcrRule rule)
    {
        bool identityMatched = ContainsKeywordExact(text, Normalize(rule.Keyword));
        if (!identityMatched
            && IsSummaryText(text)
            && !string.IsNullOrWhiteSpace(rule.Label))
        {
            identityMatched = ContainsKeywordExact(text, Normalize(rule.Label));
        }

        return identityMatched
            && (string.IsNullOrWhiteSpace(rule.RequiredKeyword)
                || ContainsKeywordExact(text, Normalize(rule.RequiredKeyword))
                || ContainsSectionMarker(text, rule.RequiredKeyword))
            && (rule.RequiredKeywordsAny is not { Count: > 0 }
                || rule.RequiredKeywordsAny.Any(value =>
                    ContainsKeywordExact(text, Normalize(value))
                    || ContainsSectionMarker(text, value)));
    }

    private static bool HasExactSiblingIdentity(
        OcrRule rule, string expectedFolder, OcrRule[] identityRules, HashSet<string> exactIdentityIds)
    {
        foreach (OcrRule other in identityRules)
        {
            if (other.Id.Equals(rule.Id, StringComparison.Ordinal) || !exactIdentityIds.Contains(other.Id))
                continue;
            if ((other.Folder ?? other.Keyword).Equals(expectedFolder, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // Title-less strip cards are identified by the folder plus the shape of
    // their rows. Each opted-in rule owns exactly one row shape, so a stray
    // card with a different value layout can never match.
    private static bool RowStructureMatches(string[] lines, OcrRule rule)
    {
        int rowCount = 0;
        int shapeCount = 0;
        foreach (string line in lines)
        {
            string simplified = SimplifyOcrText(line).Trim();
            // Kill-list strips print a bare issue ("235刹兔开猪32√"); reviewed
            // spaced tables keep the 期 marker.
            Match issue = Regex.Match(simplified, @"^\s*[\[【(（]?\s*(?<issue>\d{2,4})\s*期?");
            if (!issue.Success)
                continue;
            rowCount++;
            string value = simplified[issue.Length..].Trim();
            value = Regex.Replace(value, @"[√×✓✗✔✘?？!！]+$", "");
            // The opening result cell ("开猪32√") is not part of the field.
            value = Regex.Split(value, @"[开開准準]")[0];
            value = Regex.Replace(value, @"^(?:[杀殺刹禁]+)", "");
            value = Regex.Replace(value, @"^[：:（(【\[《〈]+", "");
            value = Regex.Replace(value, @"[）)】\]》〉]+$", "");
            value = value.Replace(" ", string.Empty);
            if (MatchesValueShape(value, rule.Type))
                shapeCount++;
        }
        return rowCount >= 5 && shapeCount * 5 >= rowCount * 4;
    }

    // “每期一行”历史统计表（爱晚亭杀肖卡）：行形如 "NNN期：X开YY"，
    // X 是本资料的单个生肖；顶部"N1-N2期错N"区间统计行不算数据行。
    // 期号行与值行被 OCR 拆成两行（"246期：" + "鼠开牛30✓"）时，值在紧邻的下一行。
    private static bool LooksLikePerIssueZodiacTable(string[] lines)
    {
        int rows = 0;
        int shapes = 0;
        for (int index = 0; index < lines.Length; index++)
        {
            Match row = Regex.Match(
                SimplifyOcrText(lines[index]).Trim(),
                @"^\s*[\[【(（]?\s*(?<issue>\d{2,4})\s*期\s*[:：]?(?<payload>[^\r\n]*)");
            if (!row.Success)
                continue;
            rows++;
            string payload = PerIssueZodiacPayload(row.Groups["payload"].Value);
            if (payload.Length == 0 && index + 1 < lines.Length)
                payload = PerIssueZodiacPayload(SimplifyOcrText(lines[index + 1]));
            if (payload.Length == 1 && Zodiac.Contains(payload[0]))
                shapes++;
        }
        return rows >= 5 && shapes * 5 >= rows * 4;
    }

    private static string PerIssueZodiacPayload(string text)
    {
        string payload = Regex.Split(text, @"开|開|√|×|✗|✓|准|準|中|错|錯")[0];
        return Regex.Replace(payload, @"[\s：:（(【\[《〈）)】\]》〉]", string.Empty);
    }

    // 复核卡的行格式：行首 3 位期号 + 期 + 可选“：/:” + 恰好 N 个两位数字，
    // 分隔符允许 . , ， 、 空格；行尾的“开XX/√/×/中/错”等状态不参与。
    private static bool TryParseRequiredRow(
        string line,
        out int issue,
        out string[] numbers,
        out int count)
    {
        issue = 0;
        numbers = [];
        count = 0;
        Match head = Regex.Match(
            SimplifyOcrText(line),
            @"^\s*[【\[（(]?\s*(?<issue>\d{3})\s*期\s*[:：]?\s*(?<payload>.+)$");
        if (!head.Success || !int.TryParse(head.Groups["issue"].Value, out issue))
            return false;
        string payload = Regex.Split(
            head.Groups["payload"].Value,
            @"开|開|√|×|✗|✓|准|準|中|错|錯|【|】|\[|\]")[0];
        var values = new List<string>();
        foreach (string token in Regex.Split(payload, @"[.,，、;；:：\s]+"))
        {
            if (token.Length == 0)
                continue;
            if (!Regex.IsMatch(token, @"^\d{2}$")
                || !int.TryParse(token, out int value)
                || value is < 1 or > 49)
                return false;
            values.Add(token);
        }
        if (values.Count == 0)
            return false;
        numbers = values.ToArray();
        count = values.Count;
        return true;
    }

    private static int RequiredRowCount(OcrRule rule) =>
        rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(rule.Type.AsSpan("号码:".Length), out int count)
            ? count
            : 0;

    // 认卡只决定是否送 medium，残缺旧行绝不能补成最终业务值。
    private static bool MatchesRequiredRowFormat(string[] lines, OcrRule rule)
    {
        int expected = RequiredRowCount(rule);
        if (expected <= 0)
            return false;
        string text = Normalize(string.Concat(lines));
        if (IsNumberStatisticsCard(text))
            return false;
        // Folder identity plus a single complete issue row establishes this table.
        // History and watermark visibility must not gate the current clear row.
        if (lines.Any(line => TryParseRequiredRow(line, out _, out string[] numbers, out int count)
            && count == expected && numbers.Distinct(StringComparer.Ordinal).Count() == expected))
            return true;
        // An explicit author/kill-number title can identify a damaged table before
        // medium reads its values. This is selection only, never value repair.
        return ContainsKeywordExact(text, Normalize(rule.Keyword)) && text.Contains("杀码", StringComparison.Ordinal)
            && lines.Any(line => Regex.IsMatch(SimplifyOcrText(line), @"^\s*\d{3}\s*期\s*[:：]?\s*\d"));
    }

    private static bool IsNumberStatisticsCard(string text) =>
        text.Contains("计算结果", StringComparison.Ordinal) || text.Contains("统计总", StringComparison.Ordinal)
        || text.Contains("万能猫", StringComparison.Ordinal) || Regex.IsMatch(text, @"[【\[]\d+次[】\]]");

    internal static IReadOnlyList<string> RowCandidateLines(OcrEvidence evidence) =>
        evidence.HasCompleteGeometry
            ? evidence.Lines.Concat(BuildRowMajorReading(evidence.TokenItems ?? evidence.Items)).ToArray()
            : evidence.Lines;

    // 取值：只从目标期号所在的那一行取，必须恰好 N 个（01～49、不重复）。
    private static string? ExtractRequiredRowValue(string[] lines, int issue, OcrRule rule)
    {
        int expected = RequiredRowCount(rule);
        if (expected <= 0 || IsNumberStatisticsCard(Normalize(string.Concat(lines))))
            return null;
        var observed = new HashSet<string>(ResultValues.NumberSetComparer);
        foreach (string line in lines)
        {
            if (!TryParseRequiredRow(line, out int rowIssue, out string[] numbers, out int count)
                || rowIssue != issue
                || count != expected)
                continue;
            if (FormatNumbers(numbers, expected) is { } value)
                observed.Add(value);
        }
        return observed.Count > 1 ? ConflictMarker : observed.SingleOrDefault();
    }

    private static bool MatchesValueShape(string value, string type)
    {        if (type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(type.AsSpan("号码:".Length), out int expectedNumbers))
        {
            // 无标题的五码表（爱晚亭）：行形如 “258期: 34.35.12.03.01.开00”。
            string[] numbers = value
                .Split(['.', ',', '，', '、', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return numbers.Length == expectedNumbers
                && numbers.All(number => number.Length == 2
                    && int.TryParse(number, out int parsed) && parsed is >= 1 and <= 49)
                && numbers.Distinct(StringComparer.Ordinal).Count() == numbers.Length;
        }
        switch (type)
        {
            case "色单双":
                return Regex.IsMatch(value, @"^[红蓝绿兰][单双]$");
            case "单五行":
                return Regex.IsMatch(value, @"^[金木水火土]$");
            case "尾数组合":
                return Regex.IsMatch(value, @"^[0-9０-９][.．,，、][0-9０-９]尾$");
            case "头":
                return Regex.IsMatch(value, @"^[0-4０-４]头$");
            case "生肖组合":
            case "单生肖":
                string zodiacs = new(value.Where(Zodiac.Contains).ToArray());
                string stripped = Regex.Replace(value, $"[{Zodiac}]", string.Empty);
                int expected = type == "单生肖" ? 1 : 2;
                return stripped.Length == 0
                    && zodiacs.Length == expected
                    && zodiacs.Distinct().Count() == expected;
            default:
                return false;
        }
    }

    private static bool WithinOneEdit(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        if (Math.Abs(left.Length - right.Length) > 1)
            return false;
        if (left.Length == right.Length)
        {
            int differences = 0;
            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index] && ++differences > 1)
                    return false;
            }
            return true;
        }

        ReadOnlySpan<char> longer = left.Length > right.Length ? left : right;
        ReadOnlySpan<char> shorter = left.Length > right.Length ? right : left;
        int longIndex = 0;
        int shortIndex = 0;
        int skips = 0;
        while (longIndex < longer.Length && shortIndex < shorter.Length)
        {
            if (longer[longIndex] == shorter[shortIndex])
            {
                longIndex++;
                shortIndex++;
                continue;
            }
            if (++skips > 1)
                return false;
            longIndex++;
        }
        return true;
    }

    public static string? ExtractValue(IEnumerable<string> cloudLines, int issue, OcrRule rule)
    {
        try
        {
            string? raw = ExtractValueCore(cloudLines, issue, rule, requireCloudKeyword: true);
            return raw == ConflictMarker ? null : raw;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static string[] NormalizeSplitIssueDigits(IEnumerable<string> source)
    {
        string[] raw = source.Select(SimplifyOcrText).ToArray();
        var normalized = new List<string>(raw.Length);
        for (int index = 0; index < raw.Length; index++)
        {
            if (index + 2 < raw.Length
                && IsStandaloneIssueDigits(raw[index], 1)
                && IsStandaloneIssueDigits(raw[index + 1], 1)
                && Regex.IsMatch(raw[index + 2], @"^\s*\d\s*期"))
            {
                normalized.Add(raw[index] + raw[index + 1] + raw[index + 2]);
                index += 2;
                continue;
            }
            if (index + 1 < raw.Length
                && IsStandaloneIssueDigits(raw[index], 1, 2)
                && Regex.IsMatch(raw[index + 1], @"^\s*\d{1,2}\s*期"))
            {
                normalized.Add(raw[index] + raw[index + 1]);
                index++;
                continue;
            }

            string line = raw[index];
            line = Regex.Replace(
                line,
                @"(?<!\d)(?<prefix>第\s*)?(?<a>\d)\s+(?<b>\d)\s+(?<c>\d)\s*期",
                match => $"{match.Groups["prefix"].Value}{match.Groups["a"].Value}"
                    + $"{match.Groups["b"].Value}{match.Groups["c"].Value}期");
            line = Regex.Replace(
                line,
                @"(?<!\d)(?<prefix>第\s*)?(?<a>\d)\s+(?<b>\d{2})\s*期",
                match => $"{match.Groups["prefix"].Value}{match.Groups["a"].Value}"
                    + $"{match.Groups["b"].Value}期");
            normalized.Add(line);
        }
        return normalized.ToArray();
    }

    private static bool IsStandaloneIssueDigits(string text, params int[] lengths) =>
        lengths.Any(length => Regex.IsMatch(text, $@"^\s*\d{{{length}}}\s*$"));

    private static string? ExtractOuyangHalfWaveRow(
        string[] lines, int issue, OcrRule rule)
    {
        int sectionStart = LocateSectionStart(lines, rule.Section);
        if (sectionStart < 0)
            return null;

        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = sectionStart; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue)
                || !HasSectionForIssueRow(lines, index, sectionStart, issue, rule))
                continue;

            string? value = ExtractTypedForRule(lines[index], rule);
            if (value == ConflictMarker)
                return ConflictMarker;
            if (value is null && index + 1 < lines.Length
                && !ContainsAnyIssue(lines[index + 1])
                && IsHalfWaveContinuation(lines[index + 1]))
            {
                string joined = lines[index] + " " + lines[index + 1];
                value = ExtractTypedForRule(joined, rule);
            }
            if (value == ConflictMarker)
                return ConflictMarker;
            if (value is not null)
                observed.Add(value);
        }

        return observed.Count == 0
            ? null
            : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    private static bool IsHalfWaveContinuation(string line)
    {
        string compact = SimplifyOcrText(line).Trim();
        compact = Regex.Replace(compact, @"[【】\[\]（）()：:,，。.!！?？√✓×\s]", "");
        return compact.Length > 0
            && compact.All(character => "禁杀半波红蓝绿单双准對对錯错".Contains(character));
    }

    private static string? ExtractZiyanerKillZodiac(
        string[] lines, int issue, OcrRule rule)
    {
        // A vertical name may be read as 紫燕 ... 儿, with data rows between.
        // The watermark establishes ownership without becoming a section boundary.
        if (YanranAuthorCards.HasIdentity(lines, rule))
        {
            RuleExtractionResult result = YanranAuthorCards.Extract(lines, issue, rule);
            return result.Status == RuleExtractionStatus.Conflict ? ConflictMarker : result.Value;
        }
        int sectionStart = LocateSectionStart(lines, rule.Section);
        if (sectionStart < 0)
            return null;
        bool sectionTitleFollowsRows = lines
            .Take(sectionStart)
            .Any(line => ContainsIssue(line, issue));

        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue))
                continue;
            if (index < sectionStart)
            {
                if (!sectionTitleFollowsRows || !IsZiyanerKillRow(lines, index))
                    continue;
            }
            else if (!HasSectionForIssueRow(lines, index, sectionStart, issue, rule)
                || !IsZiyanerKillRow(lines, index))
                continue;

            string payload = BeforeOpeningResult(SimplifyOcrText(lines[index]));
            foreach (string marker in new[] { "附注", "备注", "说明", "杀一尾" })
            {
                int cut = payload.IndexOf(marker, StringComparison.Ordinal);
                if (cut >= 0)
                    payload = payload[..cut];
            }
            string? value = ExtractSingleZodiac(RemoveIssue(payload));
            if (value is null
                && index + 1 < lines.Length
                && !ContainsAnyIssue(lines[index + 1])
                && ContainsKeyword(lines[index + 1], "新澳杀"))
            {
                value = ExtractSingleZodiac(RemoveIssue(
                    BeforeOpeningResult(lines[index] + " " + lines[index + 1])));
            }
            if (value is null && index + 1 < lines.Length
                && Regex.IsMatch(RemoveIssue(payload), @"^\s*[:：,，]*\s*新[澳奥]杀\s*[:：,，]*\s*$")
                && !ContainsAnyIssue(lines[index + 1])
                && IsWrappedSingleZodiacCell(lines[index + 1]))
            {
                value = ExtractSingleZodiac(lines[index + 1]);
            }
            if (value is not null)
                observed.Add(value);
        }

        return observed.Count == 0
            ? null
            : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    private static bool IsZiyanerKillRow(string[] lines, int index) =>
        ContainsKeyword(lines[index], "新澳杀")
        || index + 1 < lines.Length
            && !ContainsAnyIssue(lines[index + 1])
            && ContainsKeyword(lines[index + 1], "新澳杀");

    private static int LocateSectionStart(string[] lines, string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
            return 0;
        int direct = Array.FindIndex(lines, line => ContainsSectionMarker(line, section));
        if (direct >= 0)
            return direct;

        string joined = Normalize(string.Concat(lines));
        int joinedIndex = SectionAliases(section)
            .Select(alias => joined.IndexOf(alias, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (joinedIndex < 0)
            return -1;
        int consumed = 0;
        for (int index = 0; index < lines.Length; index++)
        {
            consumed += Normalize(lines[index]).Length;
            if (consumed > joinedIndex)
                return index;
        }
        return -1;
    }

    private static bool IsWrappedSingleZodiacCell(string line)
    {
        string compact = SimplifyOcrText(line).Trim();
        compact = Regex.Replace(compact, @"[【】\[\]（）()：:,，。.!！?？√✓×\s]", "");
        compact = compact.TrimStart('杀', '禁');
        return compact.Length == 1 && Zodiac.Contains(compact[0]);
    }

    // 提取分四段，顺序本身就是语义，不要调换：
    // ① 身份识别 —— 没有可核对期号的卡先验资料名，再把必须按原始行序读的卡读掉；
    // ② 专属解析 —— 每张卡只在自己那一段里找值，公共的只有「只读本期那一行、遇期号/开奖格即停」；
    // ③ 类型化   —— 复核行结构、汇总表、严格期块、号码:N 这些按形状取值；
    // ④ 兜底     —— 通用候选拼装，只有前三段都没命中才会走到。
    private static string? ExtractValueCore(IEnumerable<string> cloudLines, int issue, OcrRule rule, bool requireCloudKeyword)
    {
        if (!rule.IgnoreIssue)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(issue);
        string[] lines = cloudLines.Select(line => rule.StrictIssueBlock ? SimplifyFixedCardText(line) : line).ToArray();

        if (TryReadIdentity(lines, issue, rule, out string? identityValue))
            return identityValue;

        if (!rule.StrictIssueBlock && !(rule.Type == "生肖" && IsZodiacSummary(lines)))
            lines = SplitInlineIssueRows(lines);

        if (TryExtractOwnCard(lines, issue, rule, requireCloudKeyword, out string[] scoped, out string? cardValue))
            return cardValue;

        var state = new ExtractionState(scoped, BuildAliases(rule));
        if (TryExtractTyped(state, issue, rule, requireCloudKeyword, out string? typedValue))
            return typedValue;

        return ExtractFallbackValue(state, issue, rule);
    }

    // 跨段共享的提取状态：当前行、别名、区段起点、本期候选行。
    private sealed class ExtractionState(string[] lines, string[] aliases)
    {
        public string[] Lines { get; set; } = lines;
        public string[] Aliases { get; } = aliases;
        public int ScopeStart { get; set; }
        public IReadOnlyList<string> Candidates { get; set; } = [];
        public bool KeywordInTarget { get; set; }
    }

    private static string[] BuildAliases(OcrRule rule) =>
        new[] { rule.Keyword, rule.Label ?? string.Empty }
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    // ① 身份识别：卡上没有可核对期号时，资料名本身就是身份；跨期号格的数据行必须
    // 在 SplitInlineIssueRows 重排之前按原始行序读，否则字段会被拆散。
    private static bool TryReadIdentity(string[] lines, int issue, OcrRule rule, out string? value)
    {
        value = null;
        // IgnoreIssue (时点半): the card carries no verifiable issue; identity
        // comes from the matched candidate/template and only the body numbers
        // are validated, so no issue marker is required. The rule's own title
        // must still be present so a mismatched candidate cannot be extracted.
        // A requested issue is a query, never evidence for repairing OCR.
        if (rule.IgnoreIssue
            && !ContainsKeyword(Normalize(string.Concat(lines)), Normalize(rule.Keyword)))
            return true;
        // 黑色六合彩「黑客九肖」：卡面把九个生肖排成两行（第一行七个、第二行两个，
        // 第二行排在「开??」之后），且数据行印在期号格上方。
        if (rule.Extractor == "nine_zodiacs_across_wrapped_rows")
        {
            value = ExtractNineZodiacsAcrossWrappedRows(lines, issue, rule);
            return true;
        }
        // 嫣然电报「十码必杀」：号码行被期号格和「直接不要」标签切断，必须按原始行序读。
        if (rule.Extractor == "ten_numbers_across_issue_cell")
        {
            value = ExtractSplitTenNumbers(lines, issue, rule);
            return true;
        }
        return false;
    }

    // ② 专属解析：每张卡自己的版式，由配置里的 extractor 指定走哪一支，
    // 只认自己那一段里的字段。
    // 配置里 extractor 的合法取值。代码侧认识的解析器只在这个集合里，
    // RuleCatalog 读配置时先挡一道，写错名字在启动时就报错，而不是静默变成缺失。
    internal static readonly IReadOnlySet<string> KnownExtractors = new HashSet<string>(StringComparer.Ordinal)
    {
        "nine_zodiacs_across_wrapped_rows",
        "ten_numbers_across_issue_cell",
        "ten_numbers_straddling_issue",
        "perfect_two_zodiac",
        "ouyang_half_wave",
        "ziyaner_kill_zodiac",
        "yanran_author_card",
        "heike_nine_tails",
        "jinnang_nine_tails",
        "kill_two_zodiacs",
        "labelled_repeated",
        "labelled_single_head",
        "labelled_single_half_wave",
        "labelled_half_waves",
        "four_elements_bullseye",
        "four_elements_bracket",
        "author_forbidden_zodiac",
        "jieshao_nine_zodiacs",
        "rui_er_team_card",
    };

    private static bool TryExtractOwnCard(
        string[] lines, int issue, OcrRule rule, bool requireCloudKeyword,
        out string[] scoped, out string? value)
    {
        scoped = lines;
        value = null;
        switch (rule.Extractor)
        {
            case "perfect_two_zodiac":
                // 先过身份：同夹还有「绿格子双杀」那张绝杀2肖【…】卡，没看到本资料名就不能取值。
                if (requireCloudKeyword
                    && !ContainsKeyword(Normalize(string.Concat(lines)), Normalize(rule.Keyword)))
                    return true;
                // 「完美杀肖心水料」每期只有一行数据：值必须落在「绝杀2肖」后面的括号里，
                // 且这一行自己带目标期号；读不到就判缺失，不靠通用期行凑两肖。
                value = ExtractPerfectTwoZodiacRow(lines, issue);
                return true;

            case "yanran_author_card":
                if (requireCloudKeyword && !YanranAuthorCards.HasIdentity(lines, rule))
                    return true;
                RuleExtractionResult result = YanranAuthorCards.Extract(lines, issue, rule);
                value = result.Status == RuleExtractionStatus.Conflict ? ConflictMarker : result.Value;
                return true;

            case "ouyang_half_wave":
            {
                string[] normalized = NormalizeSplitIssueDigits(lines);
                string? halfWave = ExtractOuyangHalfWaveRow(normalized, issue, rule);
                if (halfWave is not null || normalized.Any(line => ContainsIssue(line, issue)))
                {
                    value = halfWave;
                    return true;
                }
                scoped = normalized;
                return false;
            }

            case "ziyaner_kill_zodiac":
            {
                string[] normalized = NormalizeSplitIssueDigits(lines);
                string? zodiac = ExtractZiyanerKillZodiac(normalized, issue, rule);
                if (zodiac is not null || normalized.Any(line => ContainsIssue(line, issue)))
                {
                    value = zodiac;
                    return true;
                }
                scoped = normalized;
                return false;
            }

            // 黑色六合彩「黑客杀尾」：卡面「数据分析 0 1 2 3 4 5 6 7 9 尾」，九个尾缺一个。
            case "heike_nine_tails":
                value = ExtractHeiKeNineTails(lines, issue);
                return true;

            // 黑色六合彩「锦囊杀尾」：卡面「锦囊九尾：(012456789)」，括号里九个尾缺一个。
            case "jinnang_nine_tails":
                value = ExtractJinNangNineTails(lines, issue);
                return true;

            // 黑色六合彩「黑客杀两肖」：数据行「数据杀特肖：鼠 兔」印在期号格下方，
            // 只取该标签后的两个不同生肖。
            case "kill_two_zodiacs":
                value = ExtractKillTwoZodiacs(lines, issue, rule);
                return true;

            // 嫣然电报「公式杀一肖 / 公式杀尾 / 公式杀行」：卡面都是「期号 + 固定标签 + 值 + 开奖格」，
            // 和黑色六合彩同一套骨架，只认本期行标签后的字段，三遍重复必须一致，形状不对判缺失。
            case "labelled_repeated":
                value = ExtractLabelledRepeatedValue(lines, issue, rule);
                return true;

            case "labelled_single_head":
                value = ExtractLabelledSingleHead(lines, issue);
                return true;

            case "labelled_single_half_wave":
                value = ExtractLabelledSingleHalfWave(lines, issue);
                return true;

            // 黑色六合彩「锦囊杀十码」：号码从期号上方一直跨到下方，中间被期号格和
            // 「开??」切开；开奖截断会把下半段全丢掉，所以单独合并本期块两侧的号码。
            case "ten_numbers_straddling_issue":
                value = ExtractSplitTenNumbers(lines, issue, rule);
                return true;

            // 黑色六合彩「黑客半波」：卡面固定「数据半波【红波单 … 绿波单】」，
            // 只认本期行方括号里那五个半波（六种缺一），括号外不参与。
            case "labelled_half_waves":
                value = ExtractLabelledHalfWaves(lines, issue);
                return true;

            // 黑色六合彩「红牛四行」：卡面固定「四行中特 🎯金 水 火 土🎯」，
            // 只认两个靶心之间那四个不同五行，反推缺的那个。
            case "four_elements_bullseye":
                value = ExtractLabelledFourElements(lines, issue, @"四行中特");
                return true;

            // 黑色六合彩「黑客四行」：卡面固定「五行必中【金 木 水 土】」，
            // 只认本期行方括号里那四个不同五行，反推缺的那个。
            case "four_elements_bracket":
                value = ExtractLabelledFourElements(lines, issue, @"五行必中");
                return true;

            // 嫣然心水「陈思思」：乖乖团队的禁肖统计表，每行作者名 + 正字 + 「禁」+ 生肖。
            // 通用路径会取页面上第一个「禁」后面的生肖（别人的），必须只认本人那一行。
            case "author_forbidden_zodiac":
                value = ExtractAuthorForbiddenZodiac(lines, issue, rule);
                return true;

            // 黄大仙新澳「蕊儿团队」：作者名只在图头，值在方括号里，括号右边是开奖结果。
            case "rui_er_team_card":
                value = ExtractRuiErTeamCard(lines, issue, rule);
                return true;

            case null:
                // 没有声明专属解析器的卡走下一段类型化取值，这是绝大多数规则。
                break;

            case "jieshao_nine_zodiacs":
                // 这张卡要按候选行匹配「九肖」后面的九个生肖，候选行在类型化段才拼好，
                // 所以解析放在那里，这里只是放行。
                break;

            default:
                // 配置写了代码不认识的解析器名。宁可当场报错，也不能静默落到类型化段
                // 用别的形状去凑一个值——那种错误只会以「值不对」的形式出现在结果里。
                throw new OcrException($"未知的解析器「{rule.Extractor}」：{rule.Id}，请检查配置。");
        }
        scoped = lines;
        return false;
    }

    // ③ 类型化：按卡面形状取值，不看具体是哪张卡。
    private static bool TryExtractTyped(
        ExtractionState state, int issue, OcrRule rule, bool requireCloudKeyword, out string? value)
    {
        value = null;
        string[] lines = state.Lines;
        string[] aliases = state.Aliases;
        // 复核卡（require_row_structure）：取值只认目标期号那一行，不拼行、不借期。
        if (rule.RequireRowStructure)
        {
            value = ExtractRequiredRowValue(lines, issue, rule);
            return true;
        }
        if (rule.Type == "生肖" && IsZodiacSummary(lines))
        {
            // 只认目标期本人行；找不到就判缺失。曾经回落到「图上印着的最大期号」，
            // 那等于允许拿上一期表当本期结果，已按口径取消。
            value = ExtractZodiacSummaryValue(lines, issue, rule);
            return true;
        }
        // 半波卡片只能由目标期行本身的完整资料名确认身份。
        // 这样即使上一期标题仍留在图里，也绝不能把本期“杀半波”通用字段归给旧资料。
        bool explicitHalfWaveIssueIdentity = rule.StrictIssueBlock
            && rule.Type == "半波"
            && lines.Any(line => ContainsIssue(line, issue)
                && HasExplicitHalfWaveIdentity(line, rule));
        if (rule.StrictIssueBlock && rule.Type == "半波" && !explicitHalfWaveIssueIdentity)
            return true;

        int scopeStart = 0;
        if (!string.IsNullOrWhiteSpace(rule.Section) && !explicitHalfWaveIssueIdentity)
        {
            scopeStart = LocateSectionStart(lines, rule.Section);
            if (scopeStart < 0)
                return true;
        }
        state.ScopeStart = scopeStart;

        IReadOnlyList<string> candidates = BuildIssueCandidates(lines, issue, rule, scopeStart, aliases);
        state.Candidates = candidates;
        // Section ownership is enforced while each issue candidate is built; do
        // not reopen the scope later based on a sibling section.

        bool keywordInTarget = candidates.Any(line => ContainsAnyAlias(line, aliases));
        state.KeywordInTarget = keywordInTarget;
        bool keywordInHeading = aliases.Any(alias => HasKeywordInHeading(lines, alias));
        if (requireCloudKeyword && !rule.AllowValueWithoutKeyword && !keywordInTarget && !keywordInHeading)
            return true;

        if (rule.SingleValuePerIssue && rule.Type == "头")
        {
            value = ExtractSingleHeadPerIssue(lines, issue);
            return true;
        }
        if (rule.Type == "统计生肖")
        {
            value = ExtractMostFrequentZodiac(lines, issue, rule);
            return true;
        }
        if (rule.StrictIssueBlock || rule.Folder == "公式杀料")
        {
            value = ExtractStrictIssueBlock(lines, issue, rule);
            return true;
        }
        // This sheet prints the opening result before a separate nine-zodiac row.
        // Match that row within the requested issue, never combine the smaller tiers.
        if (rule.Extractor == "jieshao_nine_zodiacs")
        {
            var nineValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (string candidate in candidates)
            {
                string normalized = Normalize(candidate);
                MatchCollection matches = Regex.Matches(normalized, $"九肖(?<value>[{Zodiac}]{{9}})(?![{Zodiac}])");
                foreach (Match nine in matches)
                {
                    string nineValue = nine.Groups["value"].Value;
                    if (nineValue.Distinct().Count() == 9)
                        nineValues.Add(nineValue);
                }
            }
            value = nineValues.Count == 0 ? null : nineValues.Count == 1 ? nineValues.Single() : ConflictMarker;
            return true;
        }
        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(rule.Type.AsSpan("号码:".Length), out int expectedCount))
        {
            // Number extraction is provenance-aware. Never fall through to the
            // generic candidate concatenation and accidentally rebuild a failed row.
            value = rule.IgnoreIssue
                ? ExtractNumbers(string.Join(' ', lines), expectedCount)
                : ExtractNumbersAroundIssue(lines, scopeStart, issue, expectedCount, rule);
            return true;
        }
        return false;
    }

    // ④ 兜底：前三段都没命中才走这里，按通用候选拼装取值。
    private static string? ExtractFallbackValue(ExtractionState state, int issue, OcrRule rule)
    {
        string[] lines = state.Lines;
        string[] aliases = state.Aliases;
        IReadOnlyList<string> candidates = state.Candidates;
        string keyword = Normalize(rule.Keyword);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        bool zodiacTypes = rule.Type is "生肖" or "单生肖" or "生肖组合" or "九肖";
        void CollectCandidates(IEnumerable<string> source, bool maximalOnly)
        {
            IEnumerable<string> relevant = maximalOnly && zodiacTypes
                ? MaximalCandidates(source)
                : source;
            foreach (string line in relevant)
            {
                string scoped = ScopeSingleValueToTargetIssue(line, issue, rule);
                string? typed = ExtractTypedForRule(scoped, rule);
                // 只有“这一行里没有别的期号”时才允许回退整行：同行一旦出现别的期号
                // （无论在前还是在后），回退都会把邻期的值读成本期值。
                if (typed is null && !ReferenceEquals(scoped, line)
                    && !LineHasOtherIssue(line, issue))
                    typed = ExtractTypedForRule(line, rule);
                if (typed is not null)
                    observed.Add(typed);
            }
        }
        IEnumerable<string> keywordCandidates = candidates.Where(
            line => ContainsAnyAlias(line, aliases)).ToArray();
        IEnumerable<string>[] passes = state.KeywordInTarget
            ? [keywordCandidates, candidates]
            : [candidates];
        foreach (IEnumerable<string> pass in passes)
        {
            CollectCandidates(pass, maximalOnly: true);
            if (observed.Count == 0)
                CollectCandidates(pass, maximalOnly: false);
            if (observed.Count > 0)
                break;
        }
        // Never let an earlier copy of the selected issue silently win.
        if (observed.Count > 0)
            return observed.Count == 1 ? observed.Single() : ConflictMarker;

        if (rule.AllowNearbyValue
            && rule.Type is ("生肖" or "单生肖"))
        {
            int heading = rule.Type == "九肖"
                ? Array.FindIndex(lines, line => Normalize(line).Contains(keyword, StringComparison.Ordinal))
                : Array.FindIndex(lines, line => ContainsAnyAlias(line, aliases));
            if (heading >= 0)
            {
                string nearby = rule.Type == "九肖"
                    ? string.Join(' ', lines.Skip(heading).Take(2))
                    : string.Join(' ', lines.Skip(Math.Max(0, heading - 1)).Take(6));
                string? value = ExtractTyped(nearby, rule.Type, rule);
                if (value is not null)
                    return value;
            }

            // Fixed-template crops can omit the title while retaining the value
            // immediately before or after the current issue marker.
            foreach (int issueIndex in Enumerable.Range(0, lines.Length)
                         .Where(index => ContainsIssue(lines[index], issue)))
            {
                string nearby = string.Join(' ', lines.Skip(Math.Max(0, issueIndex - 2)).Take(5));
                string? value = ExtractTyped(nearby, rule.Type, rule);
                if (value is not null)
                    return value;
            }
        }

        return null;
    }

    // 提取流程与失败诊断共用同一套「本人作者/栏目归属 + 目标期」目标行定位：先按作者与
    // 栏目筛掉同图旁的别人区段，再按期号边界合并相邻行。诊断统计到的字必须来自提取器
    // 真正检查的目标字段，不能拿整张图里第一个碰巧同期的期块冒充。
    private static List<string> BuildIssueCandidates(
        string[] lines, int issue, OcrRule rule, int scopeStart, IReadOnlyList<string> aliases)
    {
        var candidates = new List<string>();

        for (int index = scopeStart; index < lines.Length; index++)
        {
            string line = lines[index];
            if (!ContainsIssue(line, issue))
                continue;
            // Shared multi-author sheets must identify the author on the selected
            // issue row itself. A heading attached to an older issue is not proof.
            if (rule.Folder == "天机阁杀料"
                && !ContainsAnyAlias(line, aliases))
                continue;
            // Section ownership is resolved from the nearest active section.
            if (!string.IsNullOrWhiteSpace(rule.Section)
                && !HasExplicitHalfWaveIdentity(line, rule)
                && !HasSectionForIssueRow(lines, index, scopeStart, issue, rule))
                continue;

            string scopedLine = ScopeCandidateToPeerBoundary(line, rule, aliases, out bool peerClosed);
            candidates.Add(scopedLine);
            // 值行印在期号行上方的两列卡（雁塔题名）：期号行本身只有色单双，
            // 头/合的值在紧邻的上一行，允许向前合并有限行。
            if (BackwardValueLineRuleIds.Contains(rule.Id))
            {
                string backwards = scopedLine;
                for (int previous = index - 1; previous >= scopeStart && previous >= index - 2; previous--)
                {
                    if (OcrLayoutMarkers.IsBoundary(lines[previous])
                        || LooksLikeIssueRow(lines[previous])
                        || ContainsIssueBoundary(lines[previous], issue)
                        || ContainsPeerIdentity(lines[previous], rule))
                        break;
                    backwards = lines[previous] + " " + backwards;
                    candidates.Add(backwards);
                }
            }
            if (peerClosed)
                continue;
            // 雁塔题名两列卡的取值是同行的另一列；OCR 常把同一 Excel 行的两列
            // 读成两行，也常把值行读在期号行之后。只允许并入紧邻的下一行，
            // 遇到边界/期号行立刻停止：不取下一行之外的任何行，也就不会借下一期。
            if (BackwardValueLineRuleIds.Contains(rule.Id))
            {
                string forwards = scopedLine;
                for (int next = index + 1; next == index + 1 && next < lines.Length; next++)
                {
                    if (OcrLayoutMarkers.IsBoundary(lines[next])
                        || LooksLikeIssueRow(lines[next])
                        || ContainsIssueBoundary(lines[next], issue))
                        break;
                    forwards += " " + lines[next];
                    candidates.Add(forwards);
                }
                continue;
            }
            string combined = scopedLine;
            for (int next = index + 1; next < lines.Length && next <= index + 4; next++)
            {
                if (OcrLayoutMarkers.IsBoundary(lines[next])
                    || ContainsIssueBoundary(lines[next], issue)
                    || ContainsPeerIdentity(lines[next], rule)
                    || combined.Length + lines[next].Length >= 180)
                    break;
                combined += " " + lines[next];
                candidates.Add(combined);
            }
        }

        return candidates;
    }

    private static IEnumerable<string> MaximalCandidates(IEnumerable<string> source)
    {
        string[] values = source.Distinct(StringComparer.Ordinal).ToArray();
        return values.Where(candidate => !values.Any(other =>
            other.Length > candidate.Length
            && other.StartsWith(candidate + " ", StringComparison.Ordinal)));
    }

    private static string ScopeCandidateToPeerBoundary(
        string line, OcrRule rule, IReadOnlyList<string> aliases, out bool closed)
    {
        closed = false;
        if (rule.PeerKeywords is not { Count: > 0 })
            return line;
        (string normalized, int[] map) = NormalizeWithMap(line);
        (int Index, int Length)[] own = aliases
            .Select(alias => (Index: normalized.IndexOf(alias, StringComparison.Ordinal), Length: alias.Length))
            .Where(item => item.Index >= 0)
            .ToArray();
        if (own.Length == 0)
            return line;

        // Keep only this author's own cell. Text before the target author may
        // belong to a sibling author just as text after it may do so. The
        // slice is taken from the ORIGINAL characters so brackets, spaces and
        // instruction labels survive for both success and conflict validation.
        int ownIndex = own.Min(item => item.Index);
        int ownEnd = own.Where(item => item.Index == ownIndex).Max(item => item.Index + item.Length);
        int peerIndex = rule.PeerKeywords
            .Where(peer => !string.IsNullOrWhiteSpace(peer))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .Select(peer => normalized.IndexOf(peer, ownEnd, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (peerIndex >= 0)
            closed = true;
        int end = peerIndex >= 0 ? peerIndex : normalized.Length;
        int rawStart = ownIndex < map.Length ? map[ownIndex] : line.Length;
        int rawEnd = end < map.Length ? map[end] : line.Length;
        return line[rawStart..Math.Max(rawStart, rawEnd)];
    }

    private static bool ContainsPeerIdentity(string line, OcrRule rule)
    {
        if (rule.PeerKeywords is not { Count: > 0 })
            return false;
        string normalized = Normalize(line);
        return rule.PeerKeywords
            .Where(peer => !string.IsNullOrWhiteSpace(peer))
            .Select(Normalize)
            .Any(peer => normalized.Contains(peer, StringComparison.Ordinal));
    }

    private static string TrimAtPeerBoundary(string normalizedTail, OcrRule rule)
    {
        if (rule.PeerKeywords is not { Count: > 0 })
            return normalizedTail;
        int peer = rule.PeerKeywords
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .Select(value => normalizedTail.IndexOf(value, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        return peer >= 0 ? normalizedTail[..peer] : normalizedTail;
    }

    // 黑色六合彩「黑客杀尾」：卡面是「数据分析 0 1 2 3 4 5 6 7 8 尾」九个尾数缺一个。
    // OCR 有时整行读出，有时拆成单字符逐个输出（0 / 2 / 3 / 5 / 6 / 7 / 8 / 9尾），
    // 因此按出现顺序合并本期块前后的数字，要求恰好九个不同尾数再反推缺的那个。
    private static string? ExtractNineTailsAcrossSplitRows(string[] lines, int issue, OcrRule rule)
    {
        string[] normalized = lines.Select(SimplifyOcrText).ToArray();
        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < normalized.Length; index++)
        {
            if (!ContainsIssue(normalized[index], issue))
                continue;
            var tails = new List<int>();
            void Take(string line)
            {
                // 尾数只有 0～9，可能是整行「0 1 2 3 5 6 7 8 9 尾」，也可能是 OCR
                // 拆成单字符逐个输出（0 / 2 / 3 … / 9尾）。行读后同一物理行还会带上
                // 期号和「开??」，所以先剥掉期号、标签与开奖段，再要求剩下的数字
                // 全部是单个 0～9，避免把开奖号码等两位数误当尾数。
                string payload = BeforeOpeningResult(line);
                payload = Regex.Replace(payload, @"(?<!\d)\d{1,6}\s*期", string.Empty);
                payload = Regex.Replace(payload, @"数据分析|必中九尾|数据最准之|黑客数据|尾", string.Empty);
                MatchCollection runs = Regex.Matches(payload, @"\d+");
                if (runs.Count == 0 || runs.Cast<Match>().Any(run => run.Value.Length > 1))
                    return;
                foreach (Match run in runs)
                {
                    if (!int.TryParse(run.Value, out int tail) || tails.Contains(tail))
                        continue;
                    tails.Add(tail);
                }
            }
            // 行读后整行常是「292期 数据分析 0 1 2 3 5 6 7 8 9 尾 开??」，
            // 本期数据就在期号那一行本身，必须先收当前行再向两侧扩。
            Take(normalized[index]);
            for (int back = index - 1; back >= 0 && index - back <= 12; back--)
            {
                if (ContainsOtherIssue(normalized[back], issue))
                    break;
                if (normalized[back].TrimStart().StartsWith('开'))
                    break;
                Take(normalized[back]);
            }
            for (int forward = index + 1; forward < normalized.Length && forward - index <= 12; forward++)
            {
                string line = normalized[forward];
                if (ContainsOtherIssue(line, issue) || line.TrimStart().StartsWith('开'))
                    break;
                Take(line);
            }
            if (tails.Count == 9)
                observed.Add($"{Enumerable.Range(0, 10).Single(n => !tails.Contains(n))}尾");
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 黑色六合彩专属版式：五张卡都是「期号 + 固定标签 + 括号/靶心包住的取值 + 开奖格」，
    // 值行有时被 OCR 折成两行，所以按「本期行及其下一行」取；标签外、别的期号、
    // 开奖格一律不参与。取不到或形状不对就判缺失，绝不回落到通用扫描。
    private static IEnumerable<string> LabelledIssuePayload(
        string[] lines, int issue, string label, bool labelOptional = false)
    {
        for (int index = 0; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue))
                continue;
            var text = new System.Text.StringBuilder(SimplifyOcrText(lines[index]));
            // 值行可能被 OCR 折成多行（黑客九尾常被拆成 0 / 1 / 2 … / 9尾 逐个输出），
            // 只向后合并到下一个期号或开奖格之前，绝不跨行到别的期。
            for (int next = index + 1; next < lines.Length && next - index <= 12; next++)
            {
                string following = lines[next].Trim();
                if (following.Length == 0
                    || ContainsAnyIssue(following)
                    || following.StartsWith('开')
                    || following.Contains('期')
                    // 开奖格（猴23中 / 馬25错）不是资料值，遇到就停，别把开奖号码带进来。
                    || Regex.IsMatch(following, $@"^[{Zodiac}]\s*\d{{1,2}}\s*[中错錯准準赢贏√×✗]"))
                    break;
                text.Append(' ').Append(SimplifyOcrText(following));
            }
            string payload = BeforeOpeningResult(text.ToString());
            int at = payload.IndexOf(label, StringComparison.Ordinal);
            if (at >= 0)
                yield return payload[(at + label.Length)..];
            else if (labelOptional)
                // 允许没有关键词的卡（翩翩公子尾）：标签没读出来时，整段就是字段。
                yield return payload;
        }
    }

    // 锦囊九尾「(012456789)」/ 黑客九尾「0 1 2 3 4 5 6 7 9 尾」：
    // 标签后必须恰好九个不同单数字，反推缺的那个尾。
    private static string? ExtractLabelledNineTails(string[] lines, int issue)
    {
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string label in new[] { "锦囊九尾", "数据分析" })
        {
            foreach (string payload in LabelledIssuePayload(lines, issue, label))
            {
                // 锦囊把九个尾印成连写的「(012456789)」，黑客印成「0 1 2 3 4 5 6 7 9 尾」。
                // 两种都按单个数字收；出现两位数（2 位）说明混进了开奖号或期号，直接丢弃。
                string[] runs = Regex.Replace(payload, @"[^0-9]", " ")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (runs.Length == 0 || runs.Any(run => run.Length == 2))
                    continue;
                int[] tails = runs.SelectMany(run => run.Select(character => character - '0'))
                    .Distinct().ToArray();
                if (tails.Length != 9)
                    continue;
                observed.Add($"{Enumerable.Range(0, 10).Single(number => !tails.Contains(number))}尾");
            }
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 黑色六合彩「锦囊杀尾」专属版式：
    //   297期 | 锦囊九尾：(0 1 2 4 5 6 7 8 9) | 开??
    // 括号里是连写的九个数字（可能带空格），缺一个就是答案。只认本期行、只认括号内；
    // 括号缺失、个数不是九个、出现两位数（说明读到开奖号/期号）一律判缺失。
    private static string? ExtractJinNangNineTails(string[] lines, int issue)
    {
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string payload in LabelledIssuePayload(lines, issue, "锦囊九尾"))
        {
            Match bracket = Regex.Match(payload, @"[（(]\s*(?<digits>[0-9\s]{9,30})\s*[）)]");
            if (!bracket.Success)
                continue;
            string[] runs = Regex.Replace(bracket.Groups["digits"].Value, @"\s+", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (runs.Any(run => run.Length == 2))
                continue;
            int[] tails = runs.SelectMany(run => run.Select(character => character - '0'))
                .Distinct().ToArray();
            if (tails.Length != 9)
                continue;
            observed.Add($"{Enumerable.Range(0, 10).Single(number => !tails.Contains(number))}尾");
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 黑色六合彩「黑客杀尾」专属版式：
    //   297期 | 数据分析 0 1 2 3 4 5 6 7 9 尾 | 开??
    // 「数据分析」到「尾」之间是空格分隔的九个单数字，缺一个就是答案。
    private static string? ExtractHeiKeNineTails(string[] lines, int issue)
    {
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string payload in LabelledIssuePayload(lines, issue, "数据分析"))
        {
            Match field = Regex.Match(payload, @"^(?<digits>[0-9\s]{9,40}?)\s*尾");
            if (!field.Success)
                continue;
            string[] runs = field.Groups["digits"].Value.Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (runs.Length == 0 || runs.Any(run => run.Length != 1))
                continue;
            int[] tails = runs.Select(run => run[0] - '0').Distinct().ToArray();
            if (tails.Length != 9)
                continue;
            observed.Add($"{Enumerable.Range(0, 10).Single(number => !tails.Contains(number))}尾");
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 黑色六合彩「红牛四行」/「黑客四行」：两张卡各自版式不同——
    //   红牛：四行中特 🎯木 水 火 土🎯
    //   黑客：五行必中【金 木 水 土】
    // 只在标签后取四个不同五行，缺的那个就是答案。
    private static string? ExtractLabelledFourElements(string[] lines, int issue, string label)
    {
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string payload in LabelledIssuePayload(lines, issue, label))
        {
            // 靶心、括号都是分隔符；只认这五个五行字，个数或重复不符就判缺失。
            string cleaned = Regex.Replace(payload, @"[^\p{L}]", string.Empty);
            string value = string.Concat(Regex.Matches(cleaned, "[金木水火土]").Select(match => match.Value));
            if (value.Length != 4 || value.Distinct().Count() != 4)
                continue;
            observed.Add(Enumerable.Range(0, 5).Select(n => "金木水火土"[n]).Single(e => !value.Contains(e)) + "行");
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 黑客半波「【红波单 红波双 蓝波单 蓝波双 绿波单】」：
    // 标签后必须恰好五个不同半波（六种缺一），反推缺的那一种。
    private static string? ExtractLabelledHalfWaves(string[] lines, int issue) =>
        ExtractMissingHalfWave(LabelledHalfWavesPayloads(lines, issue));

    private static string? ExtractMissingHalfWave(IEnumerable<string> payloads)
    {
        string[] all = ["红波单", "红波双", "蓝波单", "蓝波双", "绿波单", "绿波双"];
        var observed = new HashSet<string>(StringComparer.Ordinal);
        const string token = @"(?<color>[红蓝绿])\s*波?\s*(?<parity>[单双])";
        foreach (string rawPayload in payloads)
        {
            string payload = rawPayload;
            const string placeholder = @"开\s*[:：]?\s*[?？]{1,2}";
            if (payload.Contains('开'))
            {
                // 仅容忍完整半波之间的一次开奖占位，不得修补半个词或拼接多个块。
                if (!Regex.IsMatch(payload, $@"^\s*(?:{token}[\s、,，]*)+{placeholder}(?:[\s、,，]*{token})*[\s、,，]*$"))
                    continue;
                payload = Regex.Replace(payload, placeholder, string.Empty);
            }
            string[] found = Regex.Matches(payload, token)
                .Select(match => match.Groups["color"].Value + "波" + match.Groups["parity"].Value)
                .ToArray();
            if (found.Length != 5 || found.Distinct(StringComparer.Ordinal).Count() != 5
                || !Regex.IsMatch(Regex.Replace(payload, token, string.Empty), @"^[\s、,，]*$"))
                continue;
            string[] missing = all.Except(found, StringComparer.Ordinal).ToArray();
            if (missing.Length != 1)
                continue;
            observed.Add(missing[0].Replace("波", string.Empty));
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    private const string HalfWaveIssuePrefix = @"^\s*[【\[（(]?\s*(?<issue>\d{1,6})\s*期\s*[】\]）)]?";

    private static string? ClosedHalfWavePayload(string field)
    {
        Match container = Regex.Match(field,
            @"^\s*数据半波\s*[【\[](?<inner>[^【\[】\]]*)[】\]]\s*(?:开\s*[:：]?\s*[?？]{1,2})?\s*$");
        return container.Success ? container.Groups["inner"].Value : null;
    }

    // 纯文字不能证明期号上方字段的归属；只接受本行或紧随期号、开奖之前起始的字段。
    private static IEnumerable<string> LabelledHalfWavesPayloads(string[] lines, int issue)
    {
        for (int index = 0; index < lines.Length; index++)
        {
            string line = SimplifyOcrText(lines[index]);
            Match anchor = Regex.Match(line, HalfWaveIssuePrefix);
            if (!anchor.Success || !int.TryParse(anchor.Groups["issue"].Value, out int actual) || actual != issue)
                continue;
            string field = line[anchor.Length..].Trim();
            int next = index + 1;
            if (!field.StartsWith("数据半波", StringComparison.Ordinal))
            {
                if (!Regex.IsMatch(field, @"^(?:【未开】|\[未开\])?$") || next >= lines.Length)
                    continue;
                // 重复期号夹着无期号字段时，文字顺序不能确定字段属于哪个格。
                string? boundary = lines.Skip(next).FirstOrDefault(ContainsAnyIssue);
                if (boundary is not null && ContainsIssue(boundary, issue))
                    continue;
                field = SimplifyOcrText(lines[next++]).Trim();
            }
            if (!field.StartsWith("数据半波", StringComparison.Ordinal))
                continue;
            for (; !field.Contains('】') && !field.Contains(']') && next < lines.Length && next - index <= 6; next++)
            {
                string following = SimplifyOcrText(lines[next]).Trim();
                if (following.Length == 0 || ContainsAnyIssue(following)
                    || following.Contains(OcrLayoutMarkers.RegionBoundary, StringComparison.Ordinal)
                    || following.Contains("数据半波", StringComparison.Ordinal))
                    break;
                field += " " + following;
            }
            // 行读时仅未开奖占位可以夹在半波两行之间；实际开奖结果不是半波字段。
            if (ClosedHalfWavePayload(field) is string payload)
                yield return payload;
        }
    }

    private static RuleExtractionResult ExtractPositionedHalfWaves(OcrEvidence evidence, int issue)
    {
        var payloads = new List<string>();
        bool unresolvedInlineField = false;
        foreach (var region in (evidence.TokenItems ?? evidence.Items).GroupBy(item => (item.ViewId, item.RegionId)))
        {
            OcrLineEvidence[] items = region.Where(item => !string.IsNullOrWhiteSpace(item.Text)).ToArray();
            if (items.All(item => item.Box is null))
            {
                payloads.AddRange(LabelledHalfWavesPayloads(items.Select(item => item.Text).ToArray(), issue));
                continue;
            }
            // 部分坐标缺失也不准回落到拼文本，避免把上一期残留重新接回。
            if (items.Any(item => item.Box is not { Width: > 0, Height: > 0 }))
                continue;
            // 同一个 OCR 框内自带期号和完整字段时，归属不依赖相邻框。
            foreach (OcrLineEvidence item in items)
            {
                string text = SimplifyOcrText(item.Text);
                Match prefix = Regex.Match(text, HalfWaveIssuePrefix);
                // 原始框自身已合并期号/残缺字段时，不能丢掉它而只接受另一条同期值。
                if (prefix.Success && int.TryParse(prefix.Groups["issue"].Value, out int actual) && actual == issue
                    && Regex.IsMatch(text[prefix.Length..], @"^\s*数据半波\s*[【\[][^】\]]*$"))
                    unresolvedInlineField = true;
                payloads.AddRange(LabelledHalfWavesPayloads([text], issue));
            }
            OcrLineEvidence[] anchors = items.Where(item => Regex.IsMatch(SimplifyOcrText(item.Text),
                HalfWaveIssuePrefix + @"\s*$")).ToArray();
            // 邻期带冒号或附带开奖文字仍是边界，不能因其不够纯净而跨行取值。
            OcrLineEvidence[] boundaries = items.Where(item => Regex.IsMatch(SimplifyOcrText(item.Text),
                HalfWaveIssuePrefix)).ToArray();
            foreach (OcrLineEvidence anchor in anchors.Where(item => ContainsIssue(item.Text, issue)))
            {
                OcrBox box = anchor.Box!;
                OcrLineEvidence[] openings = items.Where(item => item.Box!.X > box.Right
                    && Math.Abs(item.Box.CenterY - box.CenterY) <= Math.Min(box.Height, item.Box.Height) * 0.6
                    && Regex.IsMatch(SimplifyOcrText(item.Text).Trim(),
                        $@"^(?:开\s*[:：]?\s*[?？]{{1,2}}|[{Zodiac}]\s*\d{{1,2}}\s*[中错准赢√×✗])$"))
                    .ToArray();
                if (openings.Length != 1)
                    continue;
                double top = box.CenterY - box.Height * 1.5;
                double bottom = box.CenterY + box.Height * 1.5;
                foreach (OcrLineEvidence neighbor in boundaries.Where(item => item != anchor
                    && item.Box!.X < box.Right && item.Box.Right > box.X))
                {
                    double y = neighbor.Box!.CenterY;
                    if (y < box.CenterY) top = Math.Max(top, (y + box.CenterY) / 2);
                    else if (y > box.CenterY) bottom = Math.Min(bottom, (y + box.CenterY) / 2);
                    else { top = bottom; break; }
                }
                OcrLineEvidence[] cell = items.Where(item => item.Box!.X >= box.Right
                    && item.Box.Right <= openings[0].Box!.X
                    && item.Box.Y >= top && item.Box.Bottom <= bottom).ToArray();
                string field = string.Join(" ", GroupVisualRows(cell)
                    .Select(row => string.Concat(row.OrderBy(item => item.Box!.X).Select(item => SimplifyOcrText(item.Text)))));
                if (ClosedHalfWavePayload(field) is string payload)
                    payloads.Add(payload);
            }
        }
        RuleExtractionResult result = ToExtractionResult(ExtractMissingHalfWave(payloads));
        return unresolvedInlineField && result.Status == RuleExtractionStatus.Success ? RuleExtractionResult.Missing : result;
    }

    // 黑色六合彩「黑客杀两肖」：数据行「数据杀特肖：鼠 兔」印在期号格下方，
    // 只取该标签后的两个不同生肖；多肖、重复或没有生肖都判缺失。
    private static string? ExtractKillTwoZodiacs(string[] lines, int issue, OcrRule rule)
    {
        string[] normalized = lines.Select(SimplifyOcrText).ToArray();
        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < normalized.Length; index++)
        {
            if (!ContainsIssue(normalized[index], issue))
                continue;
            var collected = new List<string>();
            for (int forward = index + 1; forward < normalized.Length && forward - index <= 3; forward++)
            {
                string line = normalized[forward];
                if (ContainsOtherIssue(line, issue) || line.TrimStart().StartsWith('开'))
                    break;
                collected.Add(line);
            }
            // 数据行也可能与期号格同一行（期号在左、数据在右）。
            collected.Insert(0, normalized[index]);
            foreach (string line in collected)
            {
                Match field = Regex.Match(line, @"(?:杀特肖|杀两肖)\s*[:：]?\s*(?<value>.+)$");
                if (!field.Success)
                    continue;
                char[] distinct = field.Groups["value"].Value
                    .Where(Zodiac.Contains).Distinct().ToArray();
                if (distinct.Length == 2)
                    observed.Add(string.Concat(distinct));
            }
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 黑色六合彩「黑客九肖」：数据行印在期号格上方，九个生肖分两行
    // （「数据分析兔雞鼠蛇馬虎龍」+「猴狗」）。只并目标期号块前后紧邻的行，
    // 遇到别的期号或开奖行立即停止，不跨期借值；必须恰好九个不同生肖。
    private static string? ExtractNineZodiacsAcrossWrappedRows(string[] lines, int issue, OcrRule rule)
    {
        string[] normalized = lines.Select(SimplifyOcrText).ToArray();
        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < normalized.Length; index++)
        {
            if (!ContainsIssue(normalized[index], issue))
                continue;
            var collected = new List<string>();
            for (int back = index - 1; back >= 0 && index - back <= 3; back--)
            {
                // 只挡「别的期号」：本期期号可能被 OCR 重复识别成相邻行，
                // 它本身不是越界证据。
                if (ContainsOtherIssue(normalized[back], issue))
                    break;
                collected.Add(normalized[back]);
            }
            for (int forward = index + 1; forward < normalized.Length && forward - index <= 3; forward++)
            {
                string line = normalized[forward];
                if (ContainsOtherIssue(line, issue))
                    break;
                // 「开??」是开奖占位，不结束收集：这张卡的第二行生肖排在它后面。
                if (line.TrimStart().StartsWith('开'))
                    continue;
                collected.Add(line);
            }
            // 卡面标题里的「九肖」「数据最准之」等词不是数据行；生肖字可能被标题词
            // 隔开印在两行（第一行七个、第二行两个）。按出现顺序只收集生肖字，
            // 非生肖字一律当分隔符丢掉，再要求恰好九个不同生肖。
            string joined = SimplifyOcrText(string.Concat(collected));
            char[] zodiacs = joined.Where(Zodiac.Contains).ToArray();
            if (zodiacs.Length == 9 && zodiacs.Distinct().Count() == 9)
                observed.Add(string.Concat(zodiacs));
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 嫣然电报「公式杀一肖 / 公式杀尾 / 公式杀行」：卡面把同一个值重复印三遍
    //（雞雞雞 / 1 1 1 / 土 土 土）。只认本期行标签后的字段，三遍必须一致；
    // 出现两个不同值、个数不是三、或标签缺失，一律判缺失。
    private static string? ExtractLabelledRepeatedValue(string[] lines, int issue, OcrRule rule)
    {
        string[] labels = rule.Type switch
        {
            "生肖" => ["一肖", "杀肖"],
            "尾" => ["一尾", "杀尾"],
            "单五行" => ["绝杀一行", "杀一行"],
            _ => ["一肖"]
        };
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string label in labels)
        {
            foreach (string payload in LabelledIssuePayload(lines, issue, label))
            {
                string field = Regex.Replace(payload, @"[^\p{L}\p{N}]", " ");
                IEnumerable<string> tokens = rule.Type switch
                {
                    "生肖" => Regex.Matches(field, $"[{Zodiac}]").Select(match => match.Value),
                    // 尾数可能连写（「777」）也可能分开（「7 7 7」）：先取出全部数字，
                    // 去重后仍要求只有一个值，两个不同值才判缺失。
                    "尾" => Regex.Matches(field, @"[0-9]").Select(match => match.Value + "尾"),
                    _ => Regex.Matches(field, "[金木水火土]").Select(match => match.Value + "行")
                };
                string[] values = tokens.Distinct(StringComparer.Ordinal).ToArray();
                // 卡面把同一个值重复印几遍是排版习惯（现在常见三遍），不是校验条件：
                // 去重后只允许一个值，出现两个不同值才判缺失。
                if (values.Length != 1)
                    continue;
                observed.Add(values[0]);
            }
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 嫣然电报「公式杀头」：本期行只有「0头」这样一个值，别的字段不参与。
    private static string? ExtractLabelledSingleHead(string[] lines, int issue)
    {
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string payload in LabelledIssuePayload(lines, issue, string.Empty))
        {
            MatchCollection heads = Regex.Matches(payload, @"(?<!\d)(?<head>[0-4零一二三四])\s*头");
            string[] values = heads.Select(match => $"{ToArabicDigit(match.Groups["head"].Value[0])}头")
                .Distinct(StringComparer.Ordinal).ToArray();
            if (heads.Count == 1 && values.Length == 1)
                observed.Add(values[0]);
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 嫣然电报「公式半波」：本期行「1肖 蓝波单」，标签后恰好一个红/蓝/绿单双。
    private static string? ExtractLabelledSingleHalfWave(string[] lines, int issue)
    {
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string payload in LabelledIssuePayload(lines, issue, "1肖"))
        {
            MatchCollection waves = Regex.Matches(payload, "(?<color>[红蓝绿])\\s*波?\\s*(?<parity>[单双])");
            string[] values = waves.Select(match => match.Groups["color"].Value + match.Groups["parity"].Value)
                .Distinct(StringComparer.Ordinal).ToArray();
            if (waves.Count == 1 && values.Length == 1)
                observed.Add(values[0]);
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 嫣然电报「公式杀行」：卡面数据行是「绝杀一行：水水水」。同群的
    // 「公式绝杀肖」卡标题也含「绝杀」，通用单五行分支会从别处凑出一个五行字，
    // 让两张卡给出不同值被判冲突。这里只认带「绝杀一行」标签的那一行，
    // 取标签之后恰好一个五行字；缺标签、多字或不同字一律缺失/冲突。
    private static string? ExtractLabelledSingleElement(string[] lines, int issue, OcrRule rule)
    {
        string[] normalized = lines.Select(SimplifyOcrText).ToArray();
        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < normalized.Length; index++)
        {
            if (!ContainsIssue(normalized[index], issue))
                continue;
            var collected = new List<string> { normalized[index] };
            for (int back = index - 1; back >= 0 && index - back <= 2; back--)
            {
                if (ContainsOtherIssue(normalized[back], issue))
                    break;
                if (normalized[back].TrimStart().StartsWith('开'))
                    break;
                collected.Add(normalized[back]);
            }
            for (int forward = index + 1; forward < normalized.Length && forward - index <= 2; forward++)
            {
                string line = normalized[forward];
                if (ContainsOtherIssue(line, issue) || line.TrimStart().StartsWith('开'))
                    break;
                collected.Add(line);
            }
            foreach (string line in collected)
            {
                string scoped = Regex.Replace(line, @"(?<!\d)\d{1,6}\s*期", string.Empty);
                scoped = BeforeOpeningResult(scoped);
                Match field = Regex.Match(scoped, @"绝杀一行\s*[:：]?\s*(?<value>.+)$");
                if (!field.Success)
                    continue;
                char[] elements = field.Groups["value"].Value
                    .Where("金木水火土".Contains).Distinct().ToArray();
                if (elements.Length == 1)
                    observed.Add(elements[0].ToString());
            }
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 嫣然电报「公式杀一肖」：卡面把同一个生肖重复印三遍（豬豬豬）。
    // 取「一肖」标签之后唯一的那个生肖；两个不同生肖或没有生肖都判缺失。
    private static string? ExtractRepeatedSingleZodiac(string[] lines, int issue, OcrRule rule)
    {
        string[] normalized = lines.Select(SimplifyOcrText).ToArray();
        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < normalized.Length; index++)
        {
            if (!ContainsIssue(normalized[index], issue))
                continue;
            // 行读后整行常是「292期 金牌绝杀一肖：牛牛牛 开??」，本期数据就在
            // 期号那一行本身，必须先收当前行再向两侧扩。
            var collected = new List<string> { normalized[index] };
            for (int back = index - 1; back >= 0 && index - back <= 2; back--)
            {
                if (ContainsOtherIssue(normalized[back], issue))
                    break;
                // 「开??」是上一期的开奖占位：越过它就进入上一期的数据区，停止收集。
                if (normalized[back].TrimStart().StartsWith('开'))
                    break;
                collected.Add(normalized[back]);
            }
            for (int forward = index + 1; forward < normalized.Length && forward - index <= 2; forward++)
            {
                string line = normalized[forward];
                if (ContainsOtherIssue(line, issue) || line.TrimStart().StartsWith('开'))
                    break;
                collected.Add(line);
            }
            foreach (string line in collected)
            {
                // 只认真正的字段标签「一肖 / 杀肖」；行读后同一物理行会带上前面的
                // 期号和后面的「开??」，所以先去掉期号标记、按开奖标记截断，
                // 再在剩下的文字里找字段。
                string scoped = Regex.Replace(line, @"(?<!\d)\d{1,6}\s*期", string.Empty);
                scoped = BeforeOpeningResult(scoped);
                Match label = Regex.Match(scoped, @"(?:一肖|杀肖)\s*[:：]?\s*(?<value>.+)$");
                if (!label.Success)
                    continue;
                char[] distinct = label.Groups["value"].Value
                    .Where(Zodiac.Contains).Distinct().ToArray();
                if (distinct.Length == 1)
                    observed.Add(distinct[0].ToString());
            }
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 嫣然电报「十码必杀」：号码行被期号格和「直接不要」标签切断
    //（「今期10码：0216」/「18 19 20 21」/ 期号 /「开??」/「30 31 47」/「直接不要」）。
    // 只合并目标期号紧邻的号码段，仍要求恰好 10 个唯一 01～49，绝不借邻期。
    private static string? ExtractSplitTenNumbers(string[] lines, int issue, OcrRule rule)
    {
        const int expected = 10;
        string[] normalized = lines.Select(SimplifyOcrText).ToArray();
        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < normalized.Length; index++)
        {
            if (!ContainsIssue(normalized[index], issue))
                continue;
            var numbers = new List<int>();
            // 开奖格是否已经开出结果。这张卡的期号格、号码段、开奖格在同一物理行上，
            // OCR 可能把开奖号一起读进来，而且常常丢掉「开」字（只留「36中」「36」）。
            // 所以不能只靠「开」字判边界：一旦本期号码已经收满十个就立刻停止，
            // 后面出现的数字（含开奖号）一律不再接收。
            bool drawn = false;
            bool full = false;
            void Take(string line)
            {
                if (full)
                    return;
                // 字段标签「今期10码」「锦囊杀10码」里的 10 是数量说明，不是号码：
                // 先剥掉标签，再处理开奖边界。
                string labelled = Regex.Replace(
                    line, @"^[^\d:：]*\d+\s*码\s*[:：]?", string.Empty);
                // 这两张卡的号码印在括号里，开奖格在括号外。括号还在时只认括号内的
                // 数字，开奖号天然被隔离——这是最可靠的一层。
                Match bracketed = Regex.Match(labelled, @"[（(【\[]+(?<inner>[^）)】\]]*)[）)】\]]");
                if (bracketed.Success)
                {
                    labelled = bracketed.Groups["inner"].Value;
                    drawn = true;   // 括号内就是本期号码，括号外不再收集
                }
                else
                {
                    labelled = Regex.Replace(labelled, @"^[（(【\[]+", string.Empty);
                }
                // 未开奖的「开??」占位允许越过（本期号码会印在它后面）；
                // 一旦看到已开奖的标记，本行此后的数字都不属于本期号码。
                if (Regex.IsMatch(labelled, @"开\s*[?？]"))
                    labelled = Regex.Replace(labelled, @"开\s*[?？]+", " ");
                else if (Regex.IsMatch(labelled, @"开|[中错錯赢贏]|[\u4e00-\u9fa5]\s*\d{1,2}\s*$"))
                    drawn = true;
                string payload = drawn
                    ? Regex.Split(labelled, @"开|[中错錯赢贏]")[0]
                    : labelled;
                // 无括号的卡（嫣然电报十码必杀）：开奖格印在号码行末尾，OCR 常把
                // 「开」和「中/错」都丢掉，只剩一个孤立的两位数。本行号码不足十个、
                // 末尾却再挂一个数字时，那一个是开奖结果，不是本期的杀号。
                Match trailing = Regex.Match(payload, @"^(?<body>.*\S)\s+(?<tail>\d{2})$");
                if (trailing.Success)
                {
                    string body = trailing.Groups["body"].Value;
                    int bodyCount = Regex.Matches(body, @"\d{2}").Count;
                    if (bodyCount == expected - 1)
                        payload = body;
                }
                foreach (Match run in Regex.Matches(payload, @"\d+"))
                {
                    // 号码可能连写成一段（「今期10码：0216」是 02 和 16 两个），
                    // 只有按每两位完整拆开后全部合法才接受，奇数位或越界一律不算。
                    if (run.Value.Length % 2 != 0)
                        continue;
                    for (int offset = 0; offset < run.Value.Length; offset += 2)
                    {
                        if (!int.TryParse(run.Value.Substring(offset, 2), out int number)
                            || number is < 1 or > 49)
                            break;
                        if (!numbers.Contains(number))
                            numbers.Add(number);
                        // 收满十个立即停：多出来的数字只能是开奖结果或下一期的。
                        if (numbers.Count == expected)
                        {
                            full = true;
                            return;
                        }
                    }
                }
            }
            for (int back = index - 1; back >= 0 && index - back <= 4 && !full; back--)
            {
                if (ContainsOtherIssue(normalized[back], issue))
                    break;
                Take(normalized[back]);
            }
            for (int forward = index + 1; forward < normalized.Length && forward - index <= 4 && !full; forward++)
            {
                string line = normalized[forward];
                if (ContainsOtherIssue(line, issue))
                    break;
                // 「开??」是开奖占位，不结束收集：这张卡的号码段排在它后面。
                if (line.TrimStart().StartsWith('开') && Regex.IsMatch(line, @"开\s*[?？]"))
                    continue;
                Take(line);
                // 「直接不要」是本期的结束标签：后面若还有号码，属于下一期的字段，
                // 收齐本期就该停，不能把下一期的号码并进来。
                if (line.Contains("直接不要", StringComparison.Ordinal))
                    break;
            }
            if (numbers.Count == expected)
                observed.Add(string.Join(' ', numbers.Order().Select(n => n.ToString("00"))));
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 黑色六合彩「黑客半波」：六种半波里印出五种，反推缺的那一种。
    // 卡面的方括号被期号格切成两半（「数据半波【红波单红波双蓝波双」/ 期号 /
    // 「绿波单绿波双】」），所以合并本期块两侧的文字再统一识别。
    private static string? ExtractSplitHalfWaves(string[] lines, int issue, OcrRule rule)
    {
        string[] normalized = lines.Select(SimplifyOcrText).ToArray();
        var observed = new HashSet<string>(StringComparer.Ordinal);
        string[] all = ["红单", "红双", "蓝单", "蓝双", "绿单", "绿双"];
        for (int index = 0; index < normalized.Length; index++)
        {
            if (!ContainsIssue(normalized[index], issue))
                continue;
            var parts = new List<string> { normalized[index] };
            for (int back = index - 1; back >= 0 && index - back <= 3; back--)
            {
                if (ContainsOtherIssue(normalized[back], issue))
                    break;
                parts.Add(normalized[back]);
            }
            for (int forward = index + 1; forward < normalized.Length && forward - index <= 3; forward++)
            {
                string line = normalized[forward];
                if (ContainsOtherIssue(line, issue))
                    break;
                if (line.TrimStart().StartsWith('开'))
                    continue;
                parts.Add(line);
            }
            string joined = string.Concat(parts);
            // 只取方括号里的半波字段，标题里的「GPT半波预测」不算数据。
            Match field = Regex.Match(joined, @"[【\[](?<value>[^】\]]*)[】\]]");
            if (!field.Success)
                continue;
            var printed = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match wave in Regex.Matches(
                field.Groups["value"].Value, @"(?<color>[红蓝绿])波?(?<parity>[单双])"))
                printed.Add(wave.Groups["color"].Value + wave.Groups["parity"].Value);
            string[] missing = all.Where(item => !printed.Contains(item)).ToArray();
            if (printed.Count == 5 && missing.Length == 1)
                observed.Add(missing[0]);
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    private static string? ExtractSingleHeadPerIssue(string[] lines, int issue)
    {
        var heads = new HashSet<char>();
        bool foundIssue = false;

        for (int index = 0; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue))
                continue;

            foundIssue = true;
            int end = index + 1;
            while (end < lines.Length
                && !ContainsIssue(lines[end], issue)
                && !ContainsIssueBoundary(lines[end], issue))
                end++;

            for (int row = index; row < end; row++)
            {
                string text = row == index
                    ? TextAfterIssue(lines[row], issue)
                    : lines[row];
                string beforeOpening = SimplifyOcrText(text).Split('开', 2)[0];
                foreach (Match match in Regex.Matches(
                    beforeOpening,
                    @"(?<!\d)(?<head>[0-4零一二三四])\s*头"))
                {
                    heads.Add(ToArabicDigit(match.Groups["head"].Value[0]));
                }
            }
        }

        return !foundIssue || heads.Count == 0 ? null : heads.Count == 1 ? $"{heads.Single()}头" : ConflictMarker;
    }

    private static string TextAfterIssue(string line, int issue)
    {
        foreach (Match match in IssueRegex.Matches(line))
        {
            if (int.TryParse(match.Groups["issue"].Value, out int actual) && actual == issue)
                return line[(match.Index + match.Length)..];
        }

        foreach (Match match in CompactYearIssueRegex.Matches(line))
        {
            if (int.TryParse(match.Groups["issue"].Value, out int actual) && actual == issue)
                return line[(match.Index + match.Length)..];
        }

        Match leading = BareIssueRegex.Match(line);
        if (leading.Success && int.TryParse(leading.Groups["issue"].Value, out int leadingIssue) && leadingIssue == issue)
            return line[(leading.Index + leading.Length)..];

        Match year = YearIssueRegex.Match(line);
        return year.Success && int.TryParse(year.Groups["issue"].Value, out int yearIssue) && yearIssue == issue
            ? line[(year.Index + year.Length)..]
            : RemoveIssue(line);
    }

    private static string[] SplitInlineIssueRows(IEnumerable<string> source)
    {
        var output = new List<string>();
        foreach (string line in source)
        {
            MatchCollection periods = IssueRegex.Matches(line);
            int start = 0;
            for (int index = 1; index < periods.Count; index++)
            {
                output.Add(line[start..periods[index].Index]);
                start = periods[index].Index;
            }
            output.Add(line[start..]);
        }
        return output.ToArray();
    }

    private static bool IsSectionHeadingLike(string normalized) =>
        normalized.IndexOfAny(
            ['杀', '殺', '禁', '肖', '尾', '合', '波', '头', '段', '码', '計', '计',
             '一', '二', '三', '四', '五', '六', '七', '八', '九', '十']) >= 0;

    private static bool HasExplicitHalfWaveIdentity(string text, OcrRule rule)
    {
        if (!rule.StrictIssueBlock || rule.Type != "半波")
            return false;

        string normalized = Normalize(text);
        string keyword = Normalize(rule.Keyword);
        if (keyword.Length == 0 || !normalized.Contains(keyword, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrWhiteSpace(rule.RequiredKeyword))
            return true;
        string requiredKeyword = Normalize(rule.RequiredKeyword);
        return requiredKeyword.Length > 0
            && normalized.Contains(requiredKeyword, StringComparison.Ordinal);
    }

    private static bool HasSectionForIssueRow(
        string[] lines, int issueIndex, int scopeStart, int issue, OcrRule rule)
    {
        string section = Normalize(rule.Section ?? string.Empty);
        if (section.Length == 0)
            return true;
        if (ContainsSectionMarker(lines[issueIndex], rule.Section))
            return true;
        // section_on_row：区段词必须出现在本期这一行（或其下一行）里，不许拿卡面
        // 主标题里的同一串字当区段——女人味标题「澳门女人味杀2肖」就含「女人味杀2肖」，
        // 否则六肖段会被当成杀2肖段。
        if (rule.SectionOnRow)
            return issueIndex + 1 < lines.Length
                && !ContainsAnyIssue(lines[issueIndex + 1])
                && ContainsSectionMarker(lines[issueIndex] + lines[issueIndex + 1], rule.Section);
        if (issueIndex + 1 < lines.Length
            && !ContainsAnyIssue(lines[issueIndex + 1])
            && ContainsSectionMarker(lines[issueIndex] + lines[issueIndex + 1], rule.Section))
            return true;

        if (issueIndex + 1 < lines.Length
            && !ContainsAnyIssue(lines[issueIndex + 1])
            && ContainsSectionMarker(lines[issueIndex] + lines[issueIndex + 1], rule.Section))
            return true;

        string[] identity = new[] { rule.Keyword, rule.RequiredKeyword ?? string.Empty, rule.Folder ?? string.Empty }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        for (int index = issueIndex - 1; index >= scopeStart; index--)
        {
            if (index + 1 < issueIndex
                && !ContainsAnyIssue(lines[index])
                && !ContainsAnyIssue(lines[index + 1])
                && ContainsSectionMarker(lines[index] + lines[index + 1], rule.Section))
                return true;
            if (ContainsIssue(lines[index], issue))
                continue;
            // Other-period rows of the same section are data, not a section
            // boundary. Only an ambiguous standalone number (or a real heading
            // without this section) closes the scope; a historical year label
            // inside the section is skipped.
            if (ContainsAnyIssue(lines[index]))
                continue;
            if (IsBareIssueBoundary(lines[index], issue))
            {
                if (IsYearMarker(lines[index]))
                    continue;
                break;
            }
            string current = Normalize(lines[index]);
            if (current.Length == 0)
                continue;
            if (ContainsSectionMarker(lines[index], rule.Section))
                return true;
            // A sibling section heading of the same folder is a boundary even
            // when it also repeats the shared author name.
            if (ContainsPeerIdentity(lines[index], rule))
                return false;
            if (identity.Any(item => current.Contains(item, StringComparison.Ordinal)))
                continue;
            // Short neutral labels ("记录：") between a heading and its rows are
            // part of the block; only a heading-like sibling title closes it.
            if (!IsSectionHeadingLike(current))
                continue;
            // The nearest non-identity heading is a sibling section boundary.
            return false;
        }
        return false;
    }

    private static bool IsYearMarker(string line)
    {
        string trimmed = SimplifyOcrText(line).Trim();
        if (trimmed.Length is < 4 or > 5)
            return false;
        if (!trimmed.StartsWith("20", StringComparison.Ordinal))
            return false;
        for (int index = 0; index < 4; index++)
        {
            if (!char.IsDigit(trimmed[index]))
                return false;
        }
        // Cards like 独傲洒脱 interleave their vertical watermark with the
        // year labels, so medium/cloud OCR returns 20241/20244/2026傲. The
        // extra character is watermark noise, not a new issue marker.
        return trimmed.Length == 4 || trimmed[4] != '期';
    }

    private static string[] SummaryRowsForIssue(IEnumerable<string> source, int issue)
    {
        string[] lines = source.Select(Normalize).ToArray();
        var rows = new List<string>();
        bool inTarget = false;
        bool titleBound = false;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (IsSummaryText(line))
            {
                string title = line;
                // OCR may split the heading into “GG团队N期新澳 / 禁 / 肖统计表”.
                for (int previous = index - 1; previous >= 0 && previous >= index - 2
                    && !FindIssues([title]).Any(); previous--)
                {
                    string prefix = lines[previous];
                    if (prefix is "禁" or "肖" or "禁肖")
                        title = prefix + title;
                    else if (Regex.IsMatch(prefix, @"^(?:[A-Za-z]*团队|乖乖团队)?第?\d{1,6}期(?:新[澳奥])?$"))
                    {
                        title = prefix + title;
                        break;
                    }
                    else
                        break;
                }
                int[] titleIssues = FindIssues([title]).Distinct().ToArray();
                titleBound = titleIssues.Length > 0;
                inTarget = titleIssues.SequenceEqual([issue]);
                if (inTarget)
                    rows.Add(line);
                continue;
            }
            foreach (string row in SplitInlineIssueRows([line]))
            {
                // Only period rows open a block. “战绩：从N期开始记录” is history.
                if (BareIssueRegex.IsMatch(row) || Regex.IsMatch(row, @"^第?\d+期"))
                {
                    int[] rowIssues = FindIssues([row]).Distinct().ToArray();
                    if (!titleBound)
                        inTarget = rowIssues.SequenceEqual([issue]);
                    else if (rowIssues.Any(found => found != issue))
                        inTarget = false;
                }
                if (inTarget)
                    rows.Add(row);
            }
        }
        return rows.ToArray();
    }

    internal static bool HasCurrentSummaryAuthor(IReadOnlyList<string> lines, int issue, OcrRule rule) =>
        IsZodiacSummary(lines) && SummaryRowsForIssue(lines, issue)
            .Any(line => BuildAliases(rule).Any(alias => ContainsKeyword(line, alias)));

    public static bool LineContainsIssue(string line, int issue)
    {
        try
        {
            return ContainsIssue(line, issue);
        }
        catch (RegexMatchTimeoutException)
        {
            // 超时按“这一行不含目标期号”降级：调用方只会少用一个补读候选，
            // 不会因为一个病态字符串中断整轮识别。
            return false;
        }
    }

    // Issue-row strip recovery: the strip was cropped around exactly one
    // period row, so it may hold a single zodiac only, and only when the
    // target issue appears on the strip.
    // 右侧小块条（简单爱）：目标期号所在的那一行必须带“禁/月禁”，且其“禁”
    // 之后恰好一个生肖；该行不能出现第二个期号。带内其它行不参与取值。
    public static string? ExtractRightBlockZodiacFromStrip(IEnumerable<string> stripLines, int issue)
    {
        string[] lines = stripLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(Normalize)
            .ToArray();
        string[] rows = lines.Where(line => ContainsIssue(line, issue)).ToArray();
        if (rows.Length != 1)
            return null;
        string row = rows[0];
        int forbidden = row.IndexOf('禁');
        if (forbidden < 0)
            return null;
        string tail = row[(forbidden + 1)..];
        if (ContainsAnyIssue(tail) || IsBareIssueBoundary(tail, issue))
            return null;
        return ExtractSingleZodiac(tail);
    }

    public static string? ExtractIssueRowZodiacFromStrip(IEnumerable<string> stripLines, int issue)
    {        string[] lines = stripLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(Normalize)
            .ToArray();
        if (!lines.Any(line => ContainsIssue(line, issue)))
            return null;
        if (lines.Count(line => line.Any(Zodiac.Contains)) != 1)
            return null;
        string row = lines.Single(line => line.Any(Zodiac.Contains));
        int forbidden = row.IndexOf('禁');
        return ExtractSingleZodiac(forbidden >= 0 ? row[(forbidden + 1)..] : row);
    }

    // Row-strip recovery for shared summary sheets: the strip was cropped from
    // the original image around exactly one author row, so it must contain a
    // single material identity (this rule's own) and a single zodiac behind
    // this row's own 禁 mark. Anything else stays missing.
    public static string? ExtractSummaryRowZodiacFromStrip(
        IEnumerable<string> stripLines, OcrRule rule, IEnumerable<OcrRule> catalog)
    {
        string[] lines = stripLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(Normalize)
            .ToArray();
        string own = Normalize(rule.Keyword);
        string label = string.IsNullOrWhiteSpace(rule.Label) ? string.Empty : Normalize(rule.Label);
        OcrRule[] others = catalog
            .Where(item => !item.Id.Equals(rule.Id, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(item.Keyword))
            .ToArray();

        string[] aliases = new[] { own, label }
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string joined = string.Join('\n', lines);
        int ownOccurrences = aliases.Length == 0
            ? 0
            : aliases.Max(alias => CountOccurrences(joined, alias));
        if (ownOccurrences != 1)
            return null;
        foreach (string line in lines)
        {
            bool isOwn = aliases.Any(alias => line.Contains(alias, StringComparison.Ordinal));
            if (isOwn)
                continue;
            if (others.Any(other => line.Contains(Normalize(other.Keyword), StringComparison.Ordinal)))
                return null;
        }
        // Exactly one line may carry a zodiac at all: the strip is one author
        // row, so a second zodiac line means a neighbour leaked into the crop.
        if (lines.Count(line => line.Any(Zodiac.Contains)) != 1)
            return null;

        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            int aliasIndex = own.Length > 0 ? line.IndexOf(own, StringComparison.Ordinal) : -1;
            if (aliasIndex < 0 && label.Length > 0)
                aliasIndex = line.IndexOf(label, StringComparison.Ordinal);
            if (aliasIndex >= 0)
            {
                string alias = aliases.First(item => line.IndexOf(item, StringComparison.Ordinal) == aliasIndex);
                string tail = line[(aliasIndex + alias.Length)..];
                int forbidden = tail.IndexOf('禁');
                if (forbidden >= 0)
                {
                    if (Regex.IsMatch(tail[..forbidden], @"[^正准準中错錯对對囸企一二三四五0-9]"))
                        continue;
                    string payload = tail[(forbidden + 1)..];
                    string? value = payload.Contains('禁') ? null : ExtractSingleZodiac(payload);
                    if (value is not null)
                        observed.Add(value);
                }

                if (index + 2 < lines.Length
                    && !Regex.IsMatch(tail, @"[^正准準中错錯对對囸企一二三四五0-9]")
                    && Regex.IsMatch(lines[index + 1], "^禁+$"))
                {
                    string? value = ExtractSingleZodiac(lines[index + 2]);
                    if (value is not null)
                        observed.Add(value);
                }
            }
        }

        return observed.Count == 1 ? observed.Single() : null;
    }

    // 「GG团队…禁肖统计」表：每行 = 作者名 + 正字 + 红底「禁」+ 一个生肖。
    // 整图 OCR 常把各行的生肖并成一列或整行丢掉，通用路径又会拿页面上第一个
    // 「禁」后面的生肖充数，所以这里只认本人那一行：作者名后面紧跟「禁」，
    // 且「禁」之后恰好一个生肖才算数。本人行读不到生肖就判缺失，交给按行裁剪补读。
    private static string? ExtractAuthorForbiddenZodiac(string[] lines, int issue, OcrRule rule)
    {
        return ExtractZodiacSummaryValue(lines, issue, rule);
    }

    // 黄大仙新澳「蕊儿团队」：十张同版式卡，作者名只印在图头，数据行统一是
    // 「新菜304期：杀①肖【虎】T00肖」。答案只认方括号里那一截，
    // 括号右边是开奖结果（T17虎 之类），算进去就会拿到别人的生肖。
    private static string? ExtractRuiErTeamCard(string[] lines, int issue, OcrRule rule)
    {
        // 作者名只出现在图头，用它挡掉同版式的别人卡。
        if (!HasKeywordInHeading(lines, Normalize(rule.Keyword)))
            return null;

        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < lines.Length; index++)
        {
            if (!RowBelongsToIssue(lines[index], issue))
                continue;

            // 方括号可能被 OCR 切到下一行（碧水蓝天 302 期、蕊儿 302 期就是这样）。
            string row = lines[index];
            if (!row.Contains('【') && index + 1 < lines.Length && lines[index + 1].Contains('【'))
                row += lines[index + 1];

            Match payload = Regex.Match(row, "【(?<value>[^】]*)】");
            if (!payload.Success)
                continue;

            if (ParseRuiErTeamPayload(rule.Type, payload.Groups["value"].Value) is { } value)
                observed.Add(value);
        }

        return observed.Count == 1 ? observed.Single() : null;
    }

    // 这一族卡的期号有两种写法：「新菜304期」和「新菜期:300」。
    private static bool RowBelongsToIssue(string line, int issue)
    {
        if (LineContainsIssue(line, issue))
            return true;
        return Regex.IsMatch(line, $@"期\s*[:：]?\s*{issue}(?![0-9])");
    }

    // 括号里是「虎」/「猴+7尾」/「01」/「4头双」这类短串，按规则类型各取各的。
    private static string? ParseRuiErTeamPayload(string type, string payload)
    {
        string value = payload.Trim();
        if (value.Length == 0)
            return null;

        switch (type)
        {
            case "生肖":
                return ExtractSingleZodiac(value);
            case "尾":
            {
                Match tail = Regex.Match(value, "(?<digit>[0-9])尾");
                return tail.Success ? tail.Groups["digit"].Value + "尾" : null;
            }
            case "合":
                return Regex.IsMatch(value, "^[0-9]{2}$") ? value + "合" : null;
            case "半头":
                return Regex.IsMatch(value, "^[0-4]头[单双]$") ? value : null;
            default:
                return null;
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while (value.Length > 0 && (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string? ExtractZodiacSummaryValue(string[] lines, int issue, OcrRule rule, bool allowSplitFields = true)
    {
        if (rule.Type != "生肖" || !IsZodiacSummary(lines))
        {
            return null;
        }

        lines = SummaryRowsForIssue(lines, issue);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        string[] aliases = [rule.Keyword, rule.Label ?? string.Empty];
        foreach (string aliasText in aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal))
        {
            for (int index = 0; index < lines.Length; index++)
            {
                string line = Normalize(lines[index]);
                int aliasIndex = line.IndexOf(aliasText, StringComparison.Ordinal);
                if (aliasIndex < 0)
                    continue;

                string tail = TrimAtPeerBoundary(line[(aliasIndex + aliasText.Length)..], rule);
                int forbiddenIndex = tail.IndexOf('禁');
                if (forbiddenIndex >= 0)
                {
                    string statusPrefix = Normalize(tail[..forbiddenIndex]);
                    // Between this author and its 禁 field only status marks may
                    // appear; another author or “待更新” closes this cell.
                    if (statusPrefix.Length > 0
                        && Regex.IsMatch(statusPrefix, @"[^正准準中错錯对對囸企一二三四五0-9]"))
                        continue;
                    string payload = tail[(forbiddenIndex + 1)..];
                    string? value = payload.Contains('禁') ? null : ExtractSingleZodiac(payload);
                    if (value is not null)
                        observed.Add(value);
                }

                if (allowSplitFields && index + 2 < lines.Length
                    && !Regex.IsMatch(tail, @"[^正准準中错錯对對囸企一二三四五0-9]")
                    && Regex.IsMatch(Normalize(lines[index + 1]), "^禁+$"))
                {
                    string? value = ExtractSingleZodiac(Normalize(lines[index + 2]));
                    if (value is not null)
                        observed.Add(value);
                }
            }
        }

        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    private static string? ExtractMostFrequentZodiac(string[] lines, int issue, OcrRule rule)
    {
        int start = 0;
        if (!string.IsNullOrWhiteSpace(rule.Section))
        {
            start = Array.FindIndex(lines, line => ContainsSectionMarker(line, rule.Section));
            if (start < 0)
                return null;
        }

        bool inTargetIssue = false;
        int blockStart = -1;
        int blockEnd = lines.Length;
        var rows = new List<(int Count, string Value)>();
        for (int index = start; index < lines.Length; index++)
        {
            string line = lines[index];
            if (ContainsIssue(line, issue))
            {
                if (!inTargetIssue)
                    blockStart = index;
                inTargetIssue = true;
            }
            else if (ContainsIssueBoundary(line, issue))
            {
                if (inTargetIssue)
                {
                    blockEnd = index;
                    break;
                }
                continue;
            }

            if (!inTargetIssue || !TryExtractFrequencyRow(line, out int count, out string rowValue))
                continue;
            if (rowValue.Length == 0)
            {
                for (int next = index + 1; next < lines.Length; next++)
                {
                    if (ContainsIssue(lines[next], issue)
                        || ContainsIssueBoundary(lines[next], issue)
                        || Regex.IsMatch(lines[next], @"(?<!\d)\d{1,2}\s*次"))
                        break;
                    string nextValue = string.Concat(Regex.Matches(SimplifyOcrText(lines[next]), $"[{Zodiac}]")
                        .Select(item => item.Value).Distinct());
                    if (nextValue.Length > 0)
                    {
                        rowValue = nextValue;
                        break;
                    }
                }
            }
            rows.Add((count, rowValue));
        }

        if (rows.Count == 0)
            return null;

        // Empty frequency buckets are normal on these sheets (no zodiac reached
        // that count). Only buckets that actually list zodiacs compete for the
        // maximum; however an empty bucket that sits above the valued maximum
        // and before the table's last valued row means the top row was not read
        // and the whole table stays missing.
        (int Count, string Value)[] valuedRows = rows.Where(row => row.Value.Length > 0).ToArray();
        if (valuedRows.Length == 0)
            return null;
        // 这些统计表都会印「总次数:44次」「统计(14人)」或「共26人」：Σ(次数×该行生肖数)
        // 必须正好等于本期块自己的那个总量。次数被读错（例如 11次 → 1次）时总数对不上，
        // 直接判缺失；块内出现两个不同总量说明表读串了，同样判缺失，绝不拿上一期的总量。
        if (blockStart >= 0 && blockEnd > blockStart
            && TryReadPrintedTotal(lines, blockStart..blockEnd, out int printedTotal))
        {
            int total = valuedRows.Sum(row => row.Count * row.Value.Length);
            if (total != printedTotal)
                return null;
        }
        int maximum = valuedRows.Max(row => row.Count);
        int lastValuedIndex = rows.FindLastIndex(row => row.Value.Length > 0);
        for (int index = 0; index < lastValuedIndex; index++)
        {
            if (rows[index].Value.Length == 0 && rows[index].Count > maximum)
                return null;
        }
        (int Count, string Value)[] maximumRows = valuedRows.Where(row => row.Count == maximum).ToArray();
        string result = string.Concat(maximumRows
            .Select(row => row.Value)
            .SelectMany(item => item)
            .Distinct());
        return result.Length > 0 ? result : null;
    }

    // 统计表上印的总量：品鉴是「【生肖统计】(总次数:44次)」，小慧慧是「统计结果（共26人）」，
    // 灰灰团是「第296期统计(14人)」。读不到就跳过一致性校验，绝不用猜的总数否决结果。
    private static bool TryReadPrintedTotal(string[] lines, Range range, out int total)
    {
        total = 0;
        int start = range.Start.GetOffset(lines.Length);
        int end = range.End.GetOffset(lines.Length);
        bool found = false;
        for (int index = start; index < Math.Min(end, lines.Length); index++)
        {
            // 用简化后的原文匹配：Normalize 会把括号和空格去掉，
            // 那样「第253期统计(14人)」就永远对不上「人前必须有括号」的条件。
            string line = SimplifyOcrText(lines[index]);
            Match match = Regex.Match(line,
                @"(?:总次数|總次數)\s*[:：]?\s*(?<n>\d{1,3})\s*次|共\s*(?<n2>\d{1,3})\s*人|(?<!\d)(?<n3>\d{1,3})\s*人");
            if (!match.Success)
                continue;
            string value = match.Groups["n"].Success ? match.Groups["n"].Value
                : match.Groups["n2"].Success ? match.Groups["n2"].Value
                : match.Groups["n3"].Value;
            if (!int.TryParse(value, out int parsed))
                continue;
            if (found && parsed != total)
                return false;   // 同一期块出现两个不同总量：表读串了，不用任何一个
            total = parsed;
            found = true;
        }
        return found;
    }

    private static bool TryExtractFrequencyRow(string line, out int count, out string value)
    {
        string normalized = Normalize(line);
        if (normalized.Contains("总次数", StringComparison.Ordinal)
            || normalized.Contains("總次數", StringComparison.Ordinal))
        {
            count = 0;
            value = string.Empty;
            return false;
        }
        Match match = Regex.Match(line, @"(?<!\d)(?<count>\d{1,2})\s*次");
        if (!match.Success || !int.TryParse(match.Groups["count"].Value, out count))
        {
            count = 0;
            value = string.Empty;
            return false;
        }

        string payload = line[(match.Index + match.Length)..];
        payload = payload.Trim().TrimStart(':', '：', ']', '】', ')', '）');
        value = string.Concat(Regex.Matches(SimplifyOcrText(payload), $"[{Zodiac}]")
            .Select(item => item.Value)
            .Distinct());
        return true;
    }

    private static string? ExtractSingleZodiac(string text)
    {
        MatchCollection matches = Regex.Matches(SimplifyOcrText(text), $"[{Zodiac}]");
        return matches.Count == 1 ? matches[0].Value : null;
    }

    private static bool HasKeywordInHeading(string[] lines, string keyword)
    {
        for (int index = 0; index < lines.Length; index++)
        {
            if (ContainsAnyIssue(lines[index]))
                continue;

            string combined = lines[index];
            if (ContainsKeyword(Normalize(combined), keyword))
                return true;
            for (int next = index + 1; next < lines.Length && next <= index + 2 && !ContainsAnyIssue(lines[next]); next++)
            {
                combined += " " + lines[next];
                if (ContainsKeyword(Normalize(combined), keyword))
                    return true;
            }
        }
        return false;
    }

    public static RuleExtractionResult ExtractFinalResult(
        IEnumerable<string> cloudLines, int issue, OcrRule rule)
    {
        string[] lines = cloudLines as string[] ?? cloudLines.ToArray();
        try
        {
            string? raw = ExtractValueCore(
                RejectCrossIssueNearbyValue(lines, issue, rule), issue, rule, requireCloudKeyword: false);
            return ToExtractionResult(raw);
        }
        catch (RegexMatchTimeoutException)
        {
            // 病态文本触发了回溯上限：按取不到值（缺失）处理，绝不让异常中断整轮识别。
            return RuleExtractionResult.Missing;
        }
    }

    public static string? ExtractFinalValue(IEnumerable<string> cloudLines, int issue, OcrRule rule) =>
        ExtractFinalResult(cloudLines, issue, rule).Value;

    public static RuleExtractionResult ExtractFinalResult(OcrEvidence evidence, int issue, OcrRule rule)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!rule.IgnoreIssue)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(issue);
        if (rule.Id == "黑客半波" && rule.Type == "缺半波")
            return ExtractPositionedHalfWaves(evidence, issue);
        if (rule.Type == "生肖" && IsZodiacSummary(evidence.Lines))
        {
            // Read each physical view independently; a vertical merged column is not a row cell.
            var summaryValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (var view in (evidence.TokenItems ?? evidence.Items).GroupBy(item => item.ViewId))
            {
                OcrLineEvidence[] items = view.Where(item => !string.IsNullOrWhiteSpace(item.Text)).ToArray();
                IEnumerable<string> lines = items.All(item => item.Box is not null)
                    ? BuildRowMajorReading(items.Where(item => item.Box is { Height: > 0 } box
                        && box.Width >= box.Height).ToArray())
                    : OcrEvidence.SafeLines(items);
                RuleExtractionResult result = ToExtractionResult(ExtractZodiacSummaryValue(
                    lines.ToArray(), issue, rule, allowSplitFields: false));
                if (result.Status == RuleExtractionStatus.Conflict)
                    return result;
                if (result.Value is not null)
                    summaryValues.Add(result.Value);
            }
            return summaryValues.Count > 1 ? RuleExtractionResult.Conflict
                : summaryValues.Count == 1 ? RuleExtractionResult.Success(summaryValues.Single())
                : RuleExtractionResult.Missing;
        }
        if (YanranAuthorCards.Supports(rule)
            && RuleCatalog.PathBelongsToGroup(evidence.SourcePath, "嫣然心水")
            && evidence.HasCompleteGeometry)
        {
            if (!MatchesRuleFolder(evidence.SourcePath, rule))
                return RuleExtractionResult.Missing;
            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (var view in (evidence.TokenItems ?? evidence.Items).GroupBy(item => item.ViewId))
            {
                OcrLineEvidence[] items = view.Where(item => !string.IsNullOrWhiteSpace(item.Text)).ToArray();
                if (items.Length == 0 || items.Any(item => item.Box is null)
                    || !YanranAuthorCards.HasIdentity(items.Select(item => item.Text).ToArray(), rule))
                    continue;
                RuleExtractionResult result = YanranAuthorCards.Extract(
                    BuildRowMajorReading(items).ToArray(), issue, rule, allowWrappedField: false);
                if (result.Status == RuleExtractionStatus.Conflict)
                    return result;
                if (result.Value is not null)
                    values.Add(result.Value);
            }
            return values.Count > 1 ? RuleExtractionResult.Conflict
                : values.Count == 1 ? RuleExtractionResult.Success(values.Single()) : RuleExtractionResult.Missing;
        }
        // 唐朝杀头属于嫣然六合彩大全，不在这里；它的期号上方取值走 ExtractFinalResult
        // 里的 FindTangChaoHeadValueLine，不要再往这个新澳六合彩资料的分支里塞。
        if (rule.StrictIssueBlock && rule.Id is ("买什么杀十码" or "老码头杀七码")
            && RuleCatalog.PathBelongsToGroup(evidence.SourcePath, "新澳六合彩资料")
            && evidence.HasCompleteGeometry
            && ExtractFixedWrappedTableRows(evidence, issue, rule) is { } wrappedRows)
            return wrappedRows;
        if (rule.Id == "藏宝杀一肖" && rule.Type == "生肖")
            return ToExtractionResult(ZodiacIconMatcher.TryExtractSingle(evidence, issue));
        if (rule.Id == "龙王杀两肖" && rule.Type == "生肖组合" &&
            ZodiacIconMatcher.TryExtractPair(evidence, issue) is string iconValue)
            return RuleExtractionResult.Success(iconValue);

        // 黑色六合彩、嫣然电报的这几张卡，数据行横跨期号格、甚至被拆成单字符；
        // 证据路径默认按识别区域分组，OCR 还会把它们判成竖排按列切开，
        // evidence.Lines 因此不是阅读顺序。这里先按几何坐标重排成行读再取值，
        // 取不到仍回落到原来的区域分组，既有整页卡（雁塔题名等）完全不受影响。
        if (NewGroupWholePageRuleIds.Contains(rule.Id))
        {
            // 行读会在列间隔处插入区域边界标记（\u001e）；这些卡同一期号行的
            // 「期号 / 数据 / 开奖」本来就被宽间隔分开，标记不是真边界，过滤掉。
            string[] rowMajor = BuildRowMajorReading(evidence.TokenItems ?? evidence.Items)
                .Where(line => !line.Contains(OcrLayoutMarkers.RegionBoundary, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(line))
                .ToArray();
            RuleExtractionResult wholePage = ExtractFinalResult(rowMajor, issue, rule);
            if (wholePage.Status != RuleExtractionStatus.Missing)
                return wholePage;
        }

        IEqualityComparer<string> comparer = rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            ? ResultValues.NumberSetComparer : StringComparer.Ordinal;
        var observed = new HashSet<string>(comparer);
        bool conflict = false;
        var anchoredObserved = new HashSet<string>(comparer);
        bool anchoredConflict = false;
        bool hasAnchoredIdentity = false;
        foreach (IGrouping<string, OcrLineEvidence> region in evidence.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .GroupBy(item => item.ViewId + "\u001f" + item.RegionId, StringComparer.Ordinal))
        {
            OcrLineEvidence[] items = region.ToArray();
            bool anchored = ContainsRuleIdentity(items.Select(item => item.Text), rule);
            hasAnchoredIdentity |= anchored;
            RuleExtractionResult regional;
            if (items.All(item => item.Box is not null))
            {
                regional = ExtractFinalResult(items.Select(item => item.Text), issue, rule);
            }
            else
            {
                RuleExtractionResult holistic = ExtractFinalResult(items.Select(item => item.Text), issue, rule);
                if (holistic.Status == RuleExtractionStatus.Conflict)
                {
                    if (anchored)
                        anchoredConflict = true;
                    else
                        conflict = true;
                    continue;
                }
                var atomic = new HashSet<string>(comparer);
                foreach (OcrLineEvidence item in items)
                {
                    RuleExtractionResult itemResult = ExtractFinalResult(new[] { item.Text }, issue, rule);
                    if (itemResult.Status == RuleExtractionStatus.Conflict)
                    {
                        if (anchored)
                            anchoredConflict = true;
                        else
                            conflict = true;
                    }
                    else if (itemResult.Status == RuleExtractionStatus.Success)
                        atomic.Add(itemResult.Value!);
                }
                if ((anchored && anchoredConflict) || (!anchored && conflict) || atomic.Count > 1)
                {
                    if (anchored)
                        anchoredConflict = true;
                    else
                        conflict = true;
                    continue;
                }
                bool unpositionedPurpleSection = rule.Id == "紫蝴蝶"
                    && items.All(item => item.Box is null);
                regional = holistic.Status == RuleExtractionStatus.Success
                    && (unpositionedPurpleSection
                        || atomic.Count == 1 && atomic.Contains(holistic.Value!))
                    ? holistic
                    : RuleExtractionResult.Missing;
            }
            if (regional.Status == RuleExtractionStatus.Conflict)
            {
                if (anchored)
                    anchoredConflict = true;
                else
                    conflict = true;
            }
            else if (regional.Status == RuleExtractionStatus.Success)
            {
                if (anchored)
                    anchoredObserved.Add(regional.Value!);
                else
                    observed.Add(regional.Value!);
            }
        }
        // Column partitioning is required to stop cross-column joins, but a
        // dense card table can split one physical field into many cell
        // columns. Fully positioned evidence therefore also gets one row-major
        // reading with explicit boundaries across wide horizontal gaps, which
        // restores the issue row and its wrapped data as a single block
        // without ever joining across an empty span.
        OcrLineEvidence[] positioned = (evidence.TokenItems ?? evidence.Items)
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .ToArray();
        if (positioned.Length > 0 && positioned.All(item => item.Box is not null))
        {
            RuleExtractionResult rowMajor = ExtractFinalResult(
                BuildRowMajorReading(positioned), issue, rule);
            bool anchored = ContainsRuleIdentity(positioned.Select(item => item.Text), rule);
            hasAnchoredIdentity |= anchored;
            if (rowMajor.Status == RuleExtractionStatus.Conflict)
            {
                if (anchored)
                    anchoredConflict = true;
                else
                    conflict = true;
            }
            else if (rowMajor.Status == RuleExtractionStatus.Success)
            {
                if (anchored)
                    anchoredObserved.Add(rowMajor.Value!);
                else
                    observed.Add(rowMajor.Value!);
            }
        }
        // 经复核的多区卡（雁塔题名/恩平杀一尾/简单爱/爱晚亭）：分区与带边界的
        // 行读会把同一物理行切进互不相连的区域，按几何顺序整页顺读再试一次。
        if (ReviewedWholeReadingRuleIds.Contains(rule.Id)
            && positioned.Length > 0
            && positioned.All(item => item.Box is not null))
        {
            RuleExtractionResult wholeReading = ExtractFinalResult(
                positioned
                    .OrderBy(item => item.Box!.Y)
                    .ThenBy(item => item.Box!.X)
                    .Select(item => item.Text),
                issue,
                rule);
            bool anchored = ContainsRuleIdentity(positioned.Select(item => item.Text), rule);
            hasAnchoredIdentity |= anchored;
            if (wholeReading.Status == RuleExtractionStatus.Conflict)
            {
                if (anchored)
                    anchoredConflict = true;
                else
                    conflict = true;
            }
            else if (wholeReading.Status == RuleExtractionStatus.Success)
            {
                if (anchored)
                    anchoredObserved.Add(wholeReading.Value!);
                else
                    observed.Add(wholeReading.Value!);
            }
        }
        // A card can contain a neighbouring author's row in another OCR region.
        // Once this rule's own identity is visible, only identity-anchored
        // regions are evidence; a sibling row must not create a false conflict.
        if (hasAnchoredIdentity && (anchoredConflict || anchoredObserved.Count > 0))
        {
            if (anchoredConflict || anchoredObserved.Count > 1)
                return RuleExtractionResult.Conflict;
            return anchoredObserved.Count == 1
                ? RuleExtractionResult.Success(anchoredObserved.Single())
                : RuleExtractionResult.Missing;
        }
        if (conflict || observed.Count > 1)
            return RuleExtractionResult.Conflict;
        if (observed.Count == 1)
            return RuleExtractionResult.Success(observed.Single());
        // Last resort for reviewed split number tables (their physical rows may
        // be cut by the issue/opening cells, so no single partition holds the
        // whole field): the concatenated reading is the same input the cloud
        // path would have used. Other materials keep the strict region rules.
        bool reviewedNumberRule = rule.StrictIssueBlock
            && SplitIssueNumberRuleIds.Contains(rule.Id)
            && rule.Type.StartsWith("号码:", StringComparison.Ordinal);
        if (!reviewedNumberRule)
            return RuleExtractionResult.Missing;
        RuleExtractionResult whole = ExtractFinalResult(
            evidence.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .Select(item => item.Text),
            issue,
            rule);
        return whole.Status == RuleExtractionStatus.Conflict
            ? RuleExtractionResult.Conflict
            : whole;
    }

    // “宝典”（新澳六合彩资料·特围36码固定海报）专用：每个期区块固定 5 行，
    // 行内号码数固定 7/9/9/9/2；整表只缺一格时，用另一识别视图的同一行序列
    // 补回该格。只有“宝典”在白名单里，其他号码资料一律不走此路径。
    internal static readonly int[] FixedNumberBlockRowShape = [7, 9, 9, 9, 2];
    internal const int FixedNumberBlockTotal = 36;
    private static readonly HashSet<string> SingleMissingNumberCellRuleIds =
        new(StringComparer.Ordinal) { "宝典" };

    internal static (string Value, OcrEvidence Evidence)? TryFillSingleMissingNumberCell(
        IReadOnlyList<OcrEvidence> views,
        int issue,
        OcrRule rule) =>
        TryFillSingleMissingNumberCell(views, issue, rule, out _);

    internal static (string Value, OcrEvidence Evidence)? TryFillSingleMissingNumberCell(
        IReadOnlyList<OcrEvidence> views,
        int issue,
        OcrRule rule,
        out string? reason)
    {
        reason = null;
        if (!SingleMissingNumberCellRuleIds.Contains(rule.Id)
            || !rule.StrictIssueBlock
            || rule.Type is not { } ruleType
            || !ruleType.StartsWith("号码:", StringComparison.Ordinal)
            || !int.TryParse(ruleType.AsSpan("号码:".Length), out int expectedCount)
            || expectedCount != FixedNumberBlockTotal)
            return null;

        OcrEvidence[] distinctViews = views
            .Where(view => view.Items.Any(item => !string.IsNullOrWhiteSpace(item.Text)))
            .GroupBy(view => view.ViewId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (distinctViews.Length < 2)
        {
            reason = $"两路不同视图不足（{distinctViews.Length} 路）";
            return null;
        }
        reason = "固定形状或缺格定位不成立";
        foreach (OcrEvidence primary in distinctViews)
        {
            if (!TryReadFixedNumberBlock(primary, issue, out List<List<(string Value, double CenterX)>> rows))
                continue;
            if (!TryFindSingleMissingCell(rows, out int gapRow, out int gapIndex, out string left, out string right))
                continue;
            var current = rows
                .SelectMany(row => row.Select(cell => cell.Value))
                .ToHashSet(StringComparer.Ordinal);
            string[] rowValues = rows[gapRow].Select(cell => cell.Value).ToArray();
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            OcrEvidence? witness = null;
            foreach (OcrEvidence other in distinctViews)
            {
                if (other.ViewId.Equals(primary.ViewId, StringComparison.Ordinal))
                    continue;
                foreach (string sequence in CorroborationSequences(other).Distinct(StringComparer.Ordinal))
                {
                    if (!TryMatchGapWitness(sequence, rowValues, gapIndex, left, right, current, out string? value)
                        || value is null)
                        continue;
                    candidates.Add(value);
                    witness ??= other;
                }
            }
            if (candidates.Count == 0)
            {
                reason = $"缺格 {left}(第{issue}期行) → {right} 之间无另一视图佐证";
                continue;
            }
            if (candidates.Count > 1)
            {
                reason = $"缺格佐证候选不唯一（{candidates.Count} 个）";
                continue;
            }

            string fill = candidates.Single();
            var numbers = new List<string>();
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                for (int cellIndex = 0; cellIndex < rows[rowIndex].Count; cellIndex++)
                {
                    numbers.Add(rows[rowIndex][cellIndex].Value);
                    if (rowIndex == gapRow && cellIndex == gapIndex)
                        numbers.Add(fill);
                }
            }
            string? formatted = FormatNumbers(numbers.ToArray(), expectedCount);
            if (formatted is null)
            {
                reason = "补后仍不满足 36 个唯一 01～49";
                continue;
            }
            reason = null;
            return (formatted, witness!);
        }
        return null;
    }

    private static bool TryReadFixedNumberBlock(
        OcrEvidence evidence,
        int issue,
        out List<List<(string Value, double CenterX)>> rows)
    {
        rows = [];
        // Partition 会把同一行的单元格合并成行列段，固定海报必须按原始
        // token（TokenItems）逐格解析，否则行形状无法还原。
        IReadOnlyList<OcrLineEvidence> source =
            evidence.TokenItems is { Count: > 0 } tokens ? tokens : evidence.Items;
        OcrLineEvidence[] positioned = source
            .Where(item => !string.IsNullOrWhiteSpace(item.Text) && item.Box is not null)
            .ToArray();
        if (positioned.Length < FixedNumberBlockRowShape.Length)
            return false;

        List<List<OcrLineEvidence>> grouped = GroupVisualRows(positioned);
        int targetRow = -1;
        for (int index = 0; index < grouped.Count; index++)
        {
            if (!grouped[index].Any(item => LineContainsIssue(item.Text, issue)))
                continue;
            if (targetRow >= 0)
                return false;
            targetRow = index;
        }
        int halfRows = FixedNumberBlockRowShape.Length / 2;
        if (targetRow < halfRows || targetRow + halfRows >= grouped.Count)
            return false;

        var parsed = new List<List<(string Value, double CenterX)>>(FixedNumberBlockRowShape.Length);
        for (int offset = 0; offset < FixedNumberBlockRowShape.Length; offset++)
        {
            var cells = new List<(string Value, double CenterX)>();
            foreach (OcrLineEvidence item in grouped[targetRow - halfRows + offset]
                .OrderBy(entry => entry.Box!.X))
            {
                string cleaned = Regex.Replace(
                    RemoveIssue(item.Text), $@"[{Zodiac}]\s*\d{{1,2}}\s*[中错錯赢贏]", string.Empty);
                string[]? values = ParseNumbers(cleaned);
                if (values is null)
                    return false;
                for (int index = 0; index < values.Length; index++)
                    cells.Add((
                        values[index],
                        item.Box!.X + item.Box.Width * (index + 0.5) / values.Length));
            }
            parsed.Add(cells);
        }

        int shortRows = 0;
        int total = 0;
        for (int offset = 0; offset < parsed.Count; offset++)
        {
            total += parsed[offset].Count;
            if (parsed[offset].Count == FixedNumberBlockRowShape[offset] - 1)
                shortRows++;
            else if (parsed[offset].Count != FixedNumberBlockRowShape[offset])
                return false;
        }
        if (shortRows != 1 || total != FixedNumberBlockTotal - 1)
            return false;
        string[] all = parsed.SelectMany(row => row.Select(cell => cell.Value)).ToArray();
        if (all.Distinct(StringComparer.Ordinal).Count() != all.Length)
            return false;
        rows = parsed;
        return true;
    }

    private static bool TryFindSingleMissingCell(
        List<List<(string Value, double CenterX)>> rows,
        out int gapRow,
        out int gapIndex,
        out string left,
        out string right)
    {
        gapRow = -1;
        gapIndex = -1;
        left = string.Empty;
        right = string.Empty;
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (rows[rowIndex].Count >= FixedNumberBlockRowShape[rowIndex])
                continue;
            if (rows[rowIndex].Count < 5)
                return false;
            double[] deltas = new double[rows[rowIndex].Count - 1];
            for (int index = 0; index < deltas.Length; index++)
                deltas[index] = rows[rowIndex][index + 1].CenterX - rows[rowIndex][index].CenterX;
            double median = deltas.Order().ElementAt(deltas.Length / 2);
            if (median <= 0)
                return false;
            double widest = deltas.Max();
            int widestIndex = Array.IndexOf(deltas, widest);
            if (widest < median * 1.5 || widestIndex < 2 || widestIndex + 3 >= rows[rowIndex].Count)
                return false;
            gapRow = rowIndex;
            gapIndex = widestIndex;
            left = rows[rowIndex][widestIndex].Value;
            right = rows[rowIndex][widestIndex + 1].Value;
            return true;
        }
        return false;
    }

    private static IEnumerable<string> CorroborationSequences(OcrEvidence view)
    {
        IReadOnlyList<OcrLineEvidence> source = view.TokenItems is { Count: > 0 } tokens ? tokens : view.Items;
        foreach (OcrLineEvidence item in source.Where(entry => !string.IsNullOrWhiteSpace(entry.Text)))
            yield return item.Text;
        if (source.Any(item => !string.IsNullOrWhiteSpace(item.Text) && item.Box is not null))
        {
            foreach (string line in BuildRowMajorReading(source))
            {
                if (!OcrLayoutMarkers.IsBoundary(line))
                    yield return line;
            }
        }
    }

    private static bool TryMatchGapWitness(
        string sequence,
        string[] rowValues,
        int gapIndex,
        string left,
        string right,
        IReadOnlySet<string> current,
        out string? value)
    {
        value = null;
        string cleaned = Regex.Replace(
            RemoveIssue(sequence), $@"[{Zodiac}]\s*\d{{1,2}}\s*[中错錯赢贏]", string.Empty);
        string[]? tokens = ParseNumbers(cleaned);
        if (tokens is null)
            return false;
        for (int index = 0; index + 2 < tokens.Length; index++)
        {
            if (index < 2 || index + 4 >= tokens.Length)
                continue;
            if (!tokens[index].Equals(left, StringComparison.Ordinal)
                || !tokens[index + 2].Equals(right, StringComparison.Ordinal))
                continue;
            string candidate = tokens[index + 1];
            if (current.Contains(candidate))
                continue;
            if (!tokens[index - 2].Equals(rowValues[gapIndex - 2], StringComparison.Ordinal)
                || !tokens[index - 1].Equals(rowValues[gapIndex - 1], StringComparison.Ordinal)
                || !tokens[index + 3].Equals(rowValues[gapIndex + 2], StringComparison.Ordinal)
                || !tokens[index + 4].Equals(rowValues[gapIndex + 3], StringComparison.Ordinal))
                continue;
            value = candidate;
            return true;
        }
        return false;
    }

    private static List<List<OcrLineEvidence>> GroupVisualRows(IEnumerable<OcrLineEvidence> items)
    {
        var rows = new List<List<OcrLineEvidence>>();
        foreach (OcrLineEvidence item in items
            .OrderBy(entry => entry.Box!.CenterY)
            .ThenBy(entry => entry.Box!.X))
        {
            List<OcrLineEvidence>? row = rows.FirstOrDefault(candidate =>
            {
                double center = candidate.Average(entry => entry.Box!.CenterY);
                double height = candidate.Average(entry => Math.Max(1, entry.Box!.Height));
                return Math.Abs(center - item.Box!.CenterY)
                    <= Math.Max(3, Math.Min(height, Math.Max(1, item.Box.Height)) * 0.45);
            });
            if (row is null)
                rows.Add([item]);
            else
                row.Add(item);
        }
        return rows;
    }

    private static RuleExtractionResult? ExtractFixedWrappedTableRows(OcrEvidence evidence, int issue, OcrRule rule)
    {
        var observed = new HashSet<string>(rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            ? ResultValues.NumberSetComparer : StringComparer.Ordinal);
        bool applies = false;
        foreach (var view in (evidence.TokenItems ?? evidence.Items).GroupBy(item => (item.ViewId, item.RegionId)))
        {
            var items = view.Where(item => !string.IsNullOrWhiteSpace(item.Text)).ToArray();
            if (items.Any(item => item.Box is null))
                continue;
            if (!ContainsRuleIdentity(items.Select(item => SimplifyFixedCardText(item.Text)), rule))
                continue;
            var anchors = items.Where(item => Regex.IsMatch(item.Text.Trim(), @"^\d{1,6}\s*期$"))
                .OrderBy(item => item.Box!.CenterY).ToArray();
            var selected = anchors.Where(item => ContainsIssue(item.Text, issue)).ToArray();
            if (selected.Length == 0)
                continue;
            applies = true;
            foreach (var inline in items.Where(item => ContainsIssue(item.Text, issue) && !anchors.Contains(item)))
            {
                RuleExtractionResult result = ExtractFinalResult([inline.Text], issue, rule);
                if (result.Status == RuleExtractionStatus.Conflict)
                    return result;
                if (result.Value is not null)
                    observed.Add(result.Value);
            }
            foreach (var anchor in selected)
            {
                int index = Array.IndexOf(anchors, anchor);
                if (anchors.Length < 2)
                    continue;
                double center = anchor.Box!.CenterY;
                double top = index > 0 ? (anchors[index - 1].Box!.CenterY + center) / 2
                    : center - (anchors[index + 1].Box!.CenterY - center) / 2;
                double bottom = index + 1 < anchors.Length ? (anchors[index + 1].Box!.CenterY + center) / 2
                    : center + (center - anchors[index - 1].Box!.CenterY) / 2;
                // The issue and opening columns bound this physical cell. Nothing
                // outside it, including another view or adjacent row, can fill it.
                var opening = items.Where(item => item.Box!.X > anchor.Box.Right
                    && Math.Abs(item.Box.CenterY - center) < anchor.Box.Height / 2d
                    && Regex.IsMatch(SimplifyFixedCardText(item.Text).Trim(),
                        $@"^(?:开\s*[?？]+|[{Zodiac}]\s*\d{{1,2}}\s*[中错])$"))
                    .OrderBy(item => item.Box!.X).FirstOrDefault();
                if (opening is null)
                    continue;
                var cell = items.Where(item => item.Box!.X >= anchor.Box.Right
                    && item.Box.Right <= opening.Box!.X
                    && item.Box.CenterY > top && item.Box.CenterY < bottom).ToArray();
                string field = string.Join("\n", BuildRowMajorReading(cell));
                if (field.Contains(OcrLayoutMarkers.RegionBoundary, StringComparison.Ordinal))
                    continue;
                string? value = ExtractStrictTableValue(SimplifyFixedCardText(field), rule);
                if (value == ConflictMarker)
                    return RuleExtractionResult.Conflict;
                if (value is not null)
                    observed.Add(value);
            }
        }
        if (!applies)
            return null;
        return observed.Count > 1 ? RuleExtractionResult.Conflict
            : observed.Count == 1 ? RuleExtractionResult.Success(observed.Single()) : RuleExtractionResult.Missing;
    }

    // Row-major reading order with the same row tolerance the layout builder
    // uses. Cells of one visual row are joined into one physical line and only
    // split where the horizontal gap is wide or the rendered view changes, so
    // a wrapped field and the next card's leading row stay whole lines. A
    // region boundary is inserted between different views, across a wide empty
    // span, and when a row starts beyond the previous row's right edge. A wrap
    // back to the left edge gets no boundary.
    private static IEnumerable<string> BuildRowMajorReading(IReadOnlyList<OcrLineEvidence> items)
    {
        OcrLineEvidence[] ordered = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Text) && item.Box is not null)
            .OrderBy(item => item.Box!.CenterY)
            .ThenBy(item => item.Box!.X)
            .ToArray();
        var rows = new List<List<OcrLineEvidence>>();
        foreach (OcrLineEvidence item in ordered)
        {
            List<OcrLineEvidence>? row = rows.FirstOrDefault(candidate =>
            {
                double center = candidate.Average(value => value.Box!.CenterY);
                double height = candidate.Average(value => Math.Max(1, value.Box!.Height));
                return Math.Abs(center - item.Box!.CenterY)
                    <= Math.Max(3, Math.Min(height, Math.Max(1, item.Box.Height)) * 0.45);
            });
            if (row is null)
                rows.Add([item]);
            else
                row.Add(item);
        }

        var lines = new List<string>(ordered.Length);
        int previousRowRight = int.MinValue;
        string? previousView = null;
        foreach (List<OcrLineEvidence> row in rows)
        {
            List<(string Text, int Left, int Right, string View)> segments = RowSegments(row).ToList();
            for (int index = 0; index < segments.Count; index++)
            {
                (string text, int left, int right, string view) = segments[index];
                int gapThreshold = Math.Max(48, (int)Math.Round(
                    row.Where(cell => cell.Box is not null).Average(cell => Math.Max(1, cell.Box!.Height)) * 4));
                bool viewChanged = previousView is not null
                    && !previousView.Equals(view, StringComparison.Ordinal);
                bool crossesRow = index == 0 && previousRowRight != int.MinValue
                    && left - previousRowRight > gapThreshold;
                if (viewChanged || crossesRow || index > 0)
                    lines.Add(OcrLayoutMarkers.RegionBoundary);
                lines.Add(text);
                previousRowRight = right;
                previousView = view;
            }
        }
        return lines;
    }

    // Splits one visual row into segments at wide gaps or view changes, and
    // joins each segment's cells with a space so digit runs keep their pairs.
    // The split gap is capped so a huge decorative glyph cannot swallow
    // unrelated watermark strips on the same baseline.
    private static IEnumerable<(string Text, int Left, int Right, string View)> RowSegments(
        List<OcrLineEvidence> row)
    {
        List<OcrLineEvidence> cells = row.OrderBy(item => item.Box!.X).ToList();
        var current = new List<OcrLineEvidence>();
        int currentRight = int.MinValue;
        foreach (OcrLineEvidence cell in cells)
        {
            int gapThreshold = Math.Max(48, Math.Min(Math.Max(1, cell.Box!.Height), 50) * 4);
            bool split = current.Count > 0
                && (cell.Box!.X - currentRight > gapThreshold
                    || !current[0].ViewId.Equals(cell.ViewId, StringComparison.Ordinal));
            if (split)
            {
                yield return Materialize(current);
                current.Clear();
            }
            current.Add(cell);
            currentRight = Math.Max(currentRight, cell.Box!.Right);
        }
        if (current.Count > 0)
            yield return Materialize(current);

        static (string, int, int, string) Materialize(List<OcrLineEvidence> cells) => (
            string.Join(' ', cells.Select(cell => cell.Text)),
            cells.Min(cell => cell.Box!.X),
            cells.Max(cell => cell.Box!.Right),
            cells[0].ViewId);
    }

    public static string? ExtractFinalValue(OcrEvidence evidence, int issue, OcrRule rule) =>
        ExtractFinalResult(evidence, issue, rule).Value;

    private static RuleExtractionResult ToExtractionResult(string? raw) =>
        raw == ConflictMarker
            ? RuleExtractionResult.Conflict
            : raw is null
                ? RuleExtractionResult.Missing
                : RuleExtractionResult.Success(raw);

    private static IEnumerable<string> RejectCrossIssueNearbyValue(IEnumerable<string> source, int issue, OcrRule rule)
    {
        string[] lines = source.ToArray();
        if (!rule.StrictIssueBlock || !rule.AllowNearbyValue)
            return lines;
        int target = Array.FindIndex(lines, line => ContainsIssue(line, issue));
        if (target < 0)
            return lines;
        bool hasEarlierIssue = lines.Take(target).Any(line =>
            ContainsIssue(line, issue) || ContainsIssueBoundary(line, issue));
        bool hasKeyword = Normalize(lines[target]).Contains(
            Normalize(rule.RequiredKeyword ?? rule.Keyword), StringComparison.Ordinal);
        if (hasEarlierIssue && !hasKeyword)
            return lines.Take(target + 1).ToArray();
        return lines;
    }

    // 提取流程的期号块切片（期号之后到下一期号之前）。诊断也走这里，保证失败原因统计的
    // 就是取值流程看的那一块文字，而不是整张图。
    private static string SliceIssueBlock(string text, MatchCollection periods, int index)
    {
        Match period = periods[index];
        int start = period.Index + period.Length;
        int end = index + 1 < periods.Count ? periods[index + 1].Index : text.Length;
        return text[start..end].Trim();
    }

    // 诊断用的当期目标字段：与提取流程走同一套定位，失败原因里统计到的字必须就是提取器
    // 真正检查过的那段文字。严格期号卡（StrictIssueBlock / 公式杀料）在提取时按
    // IssueBlockRegex 切块，这里复用同一套切块与块过滤；其余规则按作者/栏目归属构建
    // 候选行，只统计最长的那批候选（提取也走 MaximalCandidates）。
    // 定位不到、或存在多个互相独立的目标字段时返回 null，由调用方报告「无法确定当期目标
    // 字段」，绝不拿同图旁栏、历史期或开奖段的字凑数。无期号卡（IgnoreIssue）整张卡即
    // 目标字段，身份已由候选/模板确定。
    // 失败原因统计的答案字段边界：与提取流程共用同一套「开奖结果之后不是答案」的规则。
    // AllowOpeningRow 卡行首的「开:」是这一行自己的结构标记（ExtractStrictTableValue
    // 也是先剥掉再截断），普通规则的开奖行在切块阶段已被跳过，这里只负责行内边界。
    private static string AnswerFieldBoundary(string field, OcrRule rule)
    {
        if (rule.AllowOpeningRow)
            field = Regex.Replace(field.TrimStart(), @"^开\s*[:：]?\s*", string.Empty);
        return BeforeOpeningResult(field);
    }

    private static string? LocateIssueCandidateField(string[] lines, int issue, OcrRule rule)
    {
        if (rule.IgnoreIssue)
            return Regex.Split(SimplifyOcrText(string.Join('\n', lines)), @"上期\s*开奖\s*结果")[0];
        if (rule.StrictIssueBlock || rule.Folder == "公式杀料")
            return LocateStrictIssueBlock(lines, issue, rule);
        string[] prepared = SplitInlineIssueRows(lines);
        string[] aliases = new[] { rule.Keyword, rule.Label ?? string.Empty }
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        int scopeStart = 0;
        if (!string.IsNullOrWhiteSpace(rule.Section))
        {
            scopeStart = LocateSectionStart(prepared, rule.Section);
            if (scopeStart < 0)
                return null;
        }
        IEnumerable<string> maximal = MaximalCandidates(
            BuildIssueCandidates(prepared, issue, rule, scopeStart, aliases));
        // 栏目标题下的相邻行都可能被判定为「本栏目」，但只有自己带栏目标记的那一行才是
        // 规则自己的字段。先按此筛一遍，避免把标题旁的另一栏（如「杀一肖」）当成目标字段；
        // 数据行本身不重复栏目标记的老卡仍保留唯一候选的原行为。
        if (!string.IsNullOrWhiteSpace(rule.Section))
        {
            string[] own = maximal
                .Where(candidate => ContainsSectionMarker(candidate, rule.Section))
                .ToArray();
            if (own.Length > 0)
                maximal = own;
        }
        string[] fields = maximal
            .Select(candidate => Normalize(AnswerFieldBoundary(candidate, rule)))
            .Distinct(StringComparer.Ordinal).ToArray();
        return fields.Length == 1 ? fields[0] : null;
    }

    // 严格期号卡提取时真正检查的期号块：与 ExtractStrictIssueBlock 共用 SliceIssueBlock、
    // 开奖横幅、栏目标记与公式适用范围这几道过滤，不再另写一套「截几行」的宽松解析器。
    private static string? LocateStrictIssueBlock(string[] lines, int issue, OcrRule rule)
    {
        string text = Regex.Split(SimplifyOcrText(string.Join('\n', lines)), @"上期\s*开奖\s*结果")[0];
        MatchCollection periods = IssueBlockRegex.Matches(text);
        var blocks = new List<string>();
        for (int index = 0; index < periods.Count; index++)
        {
            if (!int.TryParse(periods[index].Groups["issue"].Value, out int actualIssue)
                || actualIssue != issue)
                continue;
            string block = SliceIssueBlock(text, periods, index);
            // 数据行印在期号上方的已复核卡（黑色六合彩/嫣然电报）：把紧邻的上一行并入本期块。
            // 上一行若本身带别的期号或属于下一张卡，不并入，避免跨期借值。
            if (AboveIssueValueRuleIds.Contains(rule.Id)
                && index > 0
                && int.TryParse(periods[index - 1].Groups["issue"].Value, out int previousIssue)
                && previousIssue != issue)
            {
                string previousBlock = text[periods[index - 1].Index..periods[index].Index];
                string[] previousLines = previousBlock.Split('\n')
                    .Skip(1)   // 丢掉上一期的期号行本身
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray();
                if (previousLines.Length == 1)
                    block = previousLines[0] + "\n" + block;
            }
            // 开奖横幅不是数据行，栏目/公式归属决定这个块是否属于本规则。
            if (block.StartsWith('开') && !rule.AllowOpeningRow)
                continue;
            if (!string.IsNullOrWhiteSpace(rule.Section)
                && !ContainsSectionMarker(block, rule.Section)
                && !HasExplicitHalfWaveIdentity(block, rule))
                continue;
            if (rule.Folder == "公式杀料" && !FormulaBlockAppliesToRule(block, rule))
                continue;
            blocks.Add(AnswerFieldBoundary(block, rule));
        }
        return blocks.Count == 1 ? blocks[0] : null;
    }

    // 藏宝九肖的当期行候选（正文压缩 + 期号到下一位「N期」）：提取与失败原因共用同一段定位。
    private static IEnumerable<string> TreasureDirectionRows(string[] lines, int issue)
    {
        string compact = Regex.Replace(
            SimplifyOcrText(string.Concat(lines)), @"[\s/／|｜、,，]+", string.Empty);
        foreach (Match candidate in Regex.Matches(compact, $@"(?<!\d){issue}期"))
        {
            int start = candidate.Index + candidate.Length;
            Match nextPeriod = Regex.Match(compact[start..], @"(?<!\d)\d+期");
            yield return compact[start..(nextPeriod.Success ? start + nextPeriod.Index : compact.Length)];
        }
    }

    public static string DescribeExtractionFailure(IEnumerable<string> cloudLines, int issue, OcrRule rule)
    {
        try
        {
            return DescribeExtractionFailureCore(cloudLines, issue, rule);
        }
        catch (RegexMatchTimeoutException)
        {
            // 诊断入口同样要有上限：超时就报一个真实但不猜测的原因，不能把异常抛给 UI 层。
            return RuleEngine.DescribeMissing(foundImage: true, recognizedText: true);
        }
    }

    private static string DescribeExtractionFailureCore(IEnumerable<string> cloudLines, int issue, OcrRule rule)
    {
        string[] lines = cloudLines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (lines.Length == 0)
            return "云 OCR 未返回有效文字";
        if (!FindIssues(lines).Contains(issue) && !rule.IgnoreIssue)
            return $"已找到图片和文字，但未识别到第{issue}期";
        if (rule.Folder == "杰少" && !string.IsNullOrWhiteSpace(rule.Section))
        {
            string[] rows = SplitInlineIssueRows(lines.Select(Normalize).ToArray());
            if (!Enumerable.Range(0, rows.Length).Any(index => ContainsIssue(rows[index], issue)
                && HasSectionForIssueRow(rows, index, 0, issue, rule)))
                return $"已识别到第{issue}期，但未在“{rule.Section}”区段找到该期数据";
        }
        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(rule.Type.AsSpan("号码:".Length), out int count))
            return $"已找到候选图片和{issue}期文字，但未通过{count}个两位数号码校验（要求01-49且不重复）";
        if (rule.Type is "九肖" or "四季" or "方位" or "琴棋书画")
        {
            // 输入契约才是印在卡上的字数：九肖=9 个不同生肖；方位=3 个不同方位字；
            // 四季=3 个不同四季字；琴棋书画=3 个不同琴棋书画字。它们落库前都被属性表还原成
            // 九个生肖，那是输出契约。
            (string printedCharacters, int required, string unit) = rule.Type switch
            {
                "方位" => ("东西南北", 3, "个不同方位字"),
                "四季" => (ZodiacAttributes.FamilyCharacters("四季"), 3, "个不同四季字"),
                "琴棋书画" => ("琴棋书画", 3, "个不同琴棋书画字"),
                _ => (Zodiac, 9, "个不同生肖"),
            };
            string? field = rule.Type switch
            {
                "方位" => TreasureDirectionRows(lines, issue).FirstOrDefault(),
                "琴棋书画" => MemberArtsPayloads(lines, issue, rule).FirstOrDefault(),
                _ => LocateIssueCandidateField(lines, issue, rule),
            };
            // 目标字段定位不到时只报定不了位，不拿整张图的历期文字凑总数/去重数。
            if (field is null)
                return $"已找到{issue}期文字，但无法确定当期目标字段，未通过{required}{unit}校验";
            string printed = string.Concat(Regex.Matches(field, $"[{printedCharacters}]")
                .Select(match => match.Value));
            return printed.Length == 0
                ? $"已找到{issue}期文字，但当期目标字段未识别到{required}{unit}"
                : $"已找到{issue}期文字，但未通过{required}{unit}校验"
                    + $"（识别到{printed.Length}个，去重后{printed.Distinct().Count()}个）";
        }
        if (rule.Type == "缺两肖")
        {
            string[] zodiacs = Regex.Matches(string.Join(' ', lines), $"[{Zodiac}]")
                .Select(match => match.Value).ToArray();
            return zodiacs.Length == 0
                ? $"已找到{issue}期文字，但未识别到十个生肖"
                : $"已找到{issue}期文字，但未通过10个不同生肖校验"
                    + $"（识别到{zodiacs.Length}个，去重后{zodiacs.Distinct().Count()}个）";
        }
        if (rule.Type is "生肖" or "单生肖")
            return $"已找到{issue}期文字，但未识别到唯一有效生肖";
        if (rule.Type == "统计生肖")
            return $"已找到{issue}期文字，但未提取到最高次数生肖";
        return $"已找到{issue}期文字，但未提取到符合规则的{rule.OutputLabel}值";
    }

    // Opt-in for hardened multi-row cards. Never fall through to the generic
    // nearby/prefix extraction when a complete issue block fails validation.
    private static string? ExtractStrictIssueBlock(string[] lines, int issue, OcrRule rule)
    {
        IEqualityComparer<string> comparer = rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            ? ResultValues.NumberSetComparer : StringComparer.Ordinal;
        var values = new HashSet<string>(comparer);
        int reviewedExpectedCount = 0;
        bool reviewedSplitRule = SplitIssueNumberRuleIds.Contains(rule.Id)
            && rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(rule.Type.AsSpan("号码:".Length), out reviewedExpectedCount);
        if (reviewedSplitRule)
        {
            var reviewed = new HashSet<string>(comparer);
            bool reviewedLeftCellProven = false;
            for (int index = 0; index < lines.Length; index++)
            {
                if (!ContainsIssue(lines[index], issue))
                    continue;

                // The special adapter is only for a row that genuinely straddles
                // the issue cell: a numeric payload must already exist immediately
                // before this issue without crossing another issue. Ordinary
                // complete target blocks keep using the strict block validator.
                int previous = index - 1;
                // A real centred row has its left data cell immediately before
                // the target issue cell. Crossing an opening separator or any
                // earlier row would reintroduce the previous-period borrowing bug.
                if (previous < 0 || ContainsAnyIssue(lines[previous])
                    || IsOpeningOnlySeparator(lines[previous])
                    || !IsNumberContinuation(lines[previous], reviewedExpectedCount, rule, issue))
                {
                    // Some reviewed cards print the whole field after the issue
                    // cell. This window never borrows anything above the issue
                    // row, so it cannot reintroduce the previous-period bug; a
                    // failed attempt also does not suppress the ordinary parser.
                    string? trailing = ExtractStrictTrailingNumberWindow(
                        lines, index, issue, reviewedExpectedCount, rule);
                    if (trailing is not null)
                        reviewed.Add(trailing);
                    continue;
                }
                // The adapter applies only when the field genuinely starts after
                // any earlier issue. A result/opening banner from the previous
                // row must not mark the rule as physically reviewed, otherwise
                // the ordinary strict parser would be suppressed for a field
                // that never straddled the issue cell.
                if (!HasCurrentFieldStartAfterEarlierIssue(
                        lines, 0, index, issue, rule, reviewedExpectedCount))
                    continue;

                reviewedLeftCellProven = true;
                string? split = ExtractStrictCenteredNumberWindow(
                    lines, index, issue, reviewedExpectedCount, rule);
                if (split is not null)
                    reviewed.Add(split);
            }

            // The reviewed adapter is physical-row evidence. Merge it with the
            // ordinary strict block parser below: an incomplete sibling block
            // must not veto it, but a complete different same-issue answer must
            // still form a conflict instead of being masked. When the adapter
            // proved a straddling left cell but could not resolve the whole
            // physical field, the generic right-only parser must not revive a
            // subset as success. A distant bare number elsewhere on the page
            // must not veto an independent complete ordinary target.
            if (reviewed.Count == 0 && reviewedLeftCellProven)
                return null;
            values.UnionWith(reviewed);
        }

        string text = SimplifyOcrText(string.Join('\n', lines));
        text = Regex.Split(text, @"上期\s*开奖\s*结果")[0];
        if (rule.IgnoreIssue)
        {
            // Identity was already established by candidate/template selection;
            // a noisy title must not be required again for this unnumbered card.
            Match marker = Regex.Match(text, @"36\s*码");
            if (marker.Success)
                return ExtractStrictTableValue(text[(marker.Index + marker.Length)..], rule);
            string heading = string.Join(@"\s*", rule.Keyword.Select(c => Regex.Escape(c.ToString())));
            return ExtractStrictTableValue(Regex.Replace(text.Trim(), "^" + heading, ""), rule);
        }
        MatchCollection periods = IssueBlockRegex.Matches(text);
        if (rule.Type == "琴棋书画")
            return ExtractMemberArts(lines, issue, rule);
        string? treasureDirection = ExtractTreasureDirections(lines, issue, rule);
        if (treasureDirection is not null)
            return treasureDirection;
        // 新澳高级会员这几张卡各有固定写法，各走自己的专属解析（只读本期行 + 自己的字段）：
        //   翩翩公子尾   公子送尾数：0 1 2 4 5 6 7 8 9 准！   （九个尾缺一，常折成两行）
        //   翩翩公子五行 公子4行：金 木 水 土 准！            （四个五行缺一）
        //   翩翩公子半波 公子秒杀半波：绿波单 准！            （恰一个半波）
        //   翩翩公子头   公子禁止1头：一头 准！               （恰一个头，标签里的「1头」不算值）
        //   会员暴打     会员九肖：雞馬狗虎猴猪鼠龍蛇         （九个不同生肖）
        if (rule.Id is "翩翩公子尾" or "翩翩公子五行" or "翩翩公子半波" or "翩翩公子头" or "会员暴打")
            return ExtractMemberLabelledCard(lines, issue, rule);
        // 新澳六合彩资料这几张缺项卡各有固定写法，同样各走自己的取数（校验仍交给类型层）：
        //   大赢家杀尾 狂赢九尾:012346789                 （连写九个尾，缺一）
        //   铁甲小宝   【四头出特→→→0 1 2 3】            （方括号里四个头，缺一）
        //   特头必中   必中四头(( 0 1 2 4 ))头            （双括号里四个头，缺一）
        //   帅铁两肖   精选:狗 馬 龍 牛 虎 蛇 兔 鼠 猴 猪 （十个生肖，缺两）
        //   心水两两肖 馬 雞 兔 鼠 龍 猴 羊 虎 狗 牛       （十个生肖，缺两）
        //   白小姐四季 / 慈善四季  四季:夏肖 秋肖 冬肖     （三个季节还原九肖）
        if (rule.Id is "大赢家杀尾" or "铁甲小宝" or "特头必中"
            or "帅铁两肖" or "心水两两肖" or "白小姐四季" or "慈善四季")
            return ExtractMacauLabelledCard(lines, issue, rule);
        // Do not infer a centred/split row from page-level leading numbers.
        // Reviewed wrapped cards are handled below by TryExtractWrappedCardRow,
        // which consumes explicit neighbouring physical rows instead of TakeLast
        // data from an earlier issue.
        if (rule.AllowNearbyValue && rule.Type == "生肖")
        {
            // This reviewed poster family owns its nearby-value parser. If that
            // parser cannot prove a value inside the selected issue block, stay
            // missing instead of falling through to the generic strict-table
            // parser, which has a different layout contract.
            return ExtractNearbySingleZodiac(lines, issue, rule);
        }
        if (rule.AllowNearbyValue && rule.Type == "生肖组合")
            return ExtractNearbyZodiacPair(lines, issue);
        for (int index = 0; index < periods.Count; index++)
        {
            Match period = periods[index];
            if (!int.TryParse(period.Groups["issue"].Value, out int actualIssue) || actualIssue != issue)
                continue;
            string block = SliceIssueBlock(text, periods, index);
            // 唐朝杀一头的值行印在本期期号格上方（整卡第一期也可能如此），窄栏又把收尾的
            // 「稳杀」折到下一行。只认紧邻本期期号、且带「唐朝绝杀」标记的那一行；
            // 拿不到就直接判缺失——不能回落到通用期块，那样会借到下一张卡印在本块里的值。
            if (rule.Id == "唐朝杀头")
            {
                string? headLine = FindTangChaoHeadValueLine(text, periods, index);
                if (headLine is null)
                    continue;
                block = headLine;
            }
            // 数据行印在期号上方的已复核卡（黑色六合彩/嫣然电报）：把紧邻的上一行并入本期块。
            // 上一行必须属于另一个期号块且只有一行数据，绝不跨两张卡借值。
            else if (AboveIssueValueRuleIds.Contains(rule.Id)
                && index > 0
                && int.TryParse(periods[index - 1].Groups["issue"].Value, out int previousIssue)
                && previousIssue != issue)
            {
                string[] previousLines = text[periods[index - 1].Index..periods[index].Index]
                    .Split('\n')
                    .Skip(1)   // 丢掉上一期的期号行本身
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray();
                if (previousLines.Length == 1)
                    block = previousLines[0] + "\n" + block;
            }
            if (TryExtractWrappedCardRow(text, period, rule, out string? wrappedValue))
            {
                if (wrappedValue is null)
                    return null;
                values.Add(wrappedValue);
                continue;
            }
            // The banner's previous opening is not a data row, even when its
            // period equals the selected one.
            if (block.StartsWith('开') && !rule.AllowOpeningRow)
                continue;
            if (!string.IsNullOrWhiteSpace(rule.Section)
                && !ContainsSectionMarker(block, rule.Section)
                && !HasExplicitHalfWaveIdentity(block, rule))
                continue;
            if (rule.Folder == "公式杀料" && !FormulaBlockAppliesToRule(block, rule))
                continue;
            string? value = ExtractStrictTableValue(block, rule);
            if (value is null)
            {
                // A complete reviewed candidate survives an incomplete sibling
                // block of the same issue; the generic loop must not fabricate.
                if (values.Count > 0)
                    continue;
                return null;
            }
            values.Add(value);
        }
        return values.Count == 0 ? null : values.Count == 1 ? values.Single() : ConflictMarker;
    }

    // 唐朝杀一头的值行固定印在本期期号格上方（整卡第一期也一样），窄栏把收尾的
    // 「稳杀」折到下一行。这里只返回紧邻本期期号、且带「唐朝绝杀」标记的那一行；
    // 本期值行缺失时返回 null，由调用方保持缺失，绝不翻到更早期号块借值。
    private static string? FindTangChaoHeadValueLine(string text, MatchCollection periods, int index)
    {
        // 证据路径会把「期号格」和「值行」合并成同一个 token
        //（「298期唐朝绝杀二头二头二头稳」），这时值就在期号本行里。
        // 先剥掉本行的期号前缀，若剩余部分仍是完整的「唐朝绝杀…稳杀」就直接用它。
        int issueStart = periods[index].Index;
        int issueEnd = issueStart + periods[index].Length;
        int thisBlockEnd = index + 1 < periods.Count ? periods[index + 1].Index : text.Length;
        string inlineLine = text[issueStart..thisBlockEnd]
            .Split('\n')[0]
            .Trim();
        if (inlineLine.Contains("唐朝绝杀", StringComparison.Ordinal))
        {
            // 合并 token 形如「298期唐朝绝杀二头二头二头稳」：字段解析要求以
            // 「唐朝绝杀」开头，所以先剥掉期号前缀，再按需补上折下来的「杀」。
            int at = inlineLine.IndexOf("唐朝绝杀", StringComparison.Ordinal);
            string field = inlineLine[at..].Trim();
            string candidate = field.EndsWith('杀') ? field : field + "杀";
            if (Regex.IsMatch(candidate, @"^唐朝绝杀\s*(?<value>.+?)\s*稳\s*杀$", RegexOptions.Singleline))
                return candidate;
        }
        int start = index > 0 ? periods[index - 1].Index : 0;
        int end = periods[index].Index;
        if (end <= start)
            return null;
        string[] previousLines = text[start..end]
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Skip(index > 0 ? 1 : 0)   // 丢掉上一期的期号行本身
            .ToArray();
        if (previousLines.Length == 0)
            return null;
        string valueLine = previousLines[^1];
        if (!valueLine.Contains("唐朝绝杀", StringComparison.Ordinal))
            return null;
        if (valueLine.EndsWith('杀'))
            return valueLine;
        // 折下来的「杀」只能出现在期号格后面紧跟的「开??」这类结构占位之后：
        // 允许跳过的只有占位行，遇到别的文字（例如下一张卡的值行）就停止，
        // 不能在这一块里随便找一个「杀」拼上来。
        int blockEnd = index + 1 < periods.Count ? periods[index + 1].Index : text.Length;
        int afterIssue = periods[index].Index + periods[index].Length;
        foreach (string line in text[afterIssue..blockEnd]
                     .Split('\n')
                     .Select(item => item.Trim())
                     .Where(item => item.Length > 0)
                     .Take(2))
        {
            if (line == "杀")
                return valueLine + "杀";
            // 期号格后面紧跟的是本期的开奖格（「开??」或「馬25中」），
            // 只有这种结构行才允许跳过；遇到别的文字说明已经越到下一张卡。
            if (!line.StartsWith('开')
                && !Regex.IsMatch(line, $@"^[{Zodiac}]\s*\d{{1,2}}\s*[中錯错准準赢贏√×✗]"))
                break;
        }
        return valueLine;
    }

    // 完美两肖：卡面每期一行「296期：绝杀2肖(鸡龙】开？00准」，括号常被 OCR 读成
    // 半角「(」配全角「】」，繁体也常见。先按 SimplifyOcrText 归一，再用既有期号解析
    // 取期号；只认目标期这一行 + 括号内恰好两个不同生肖，别的行、别的期号都不参与。
    private static string? ExtractPerfectTwoZodiacRow(string[] lines, int issue)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (string rawLine in lines)
        {
            string line = SimplifyOcrText(rawLine);
            Match row = Regex.Match(line,
                @"绝杀\s*2\s*肖\s*[（(【\[]?\s*(?<value>[^）)】\]开准]{2,8}?)\s*[）)】\]]");
            if (!row.Success)
                continue;
            int[] printed = FindIssues(new[] { line[..row.Index] }).ToArray();
            if (printed.Length == 0 || printed.Any(actual => actual != issue))
                continue;
            string zodiacs = string.Concat(Regex.Matches(row.Groups["value"].Value, $"[{Zodiac}]")
                .Select(match => match.Value).Distinct());
            if (zodiacs.Length != 2)
                continue;
            values.Add(zodiacs);
        }
        return values.Count == 0 ? null : values.Count == 1 ? values.Single() : ConflictMarker;
    }

    private static bool TryExtractWrappedCardRow(string text, Match period, OcrRule rule, out string? value)
    {
        value = null;
        if ((rule.Id, rule.Type) is not (("狗庄", "号码:12") or ("天线宝杀", "号码:12") or ("内幕", "号码:36")))
            return false;

        int lineStart = text.LastIndexOf('\n', period.Index);
        int lineEnd = text.IndexOf('\n', period.Index + period.Length);
        if (lineStart < 0 || lineEnd < 0 || text[(lineStart + 1)..period.Index].Trim().Length != 0)
            return false;
        int previousStart = lineStart == 0 ? -1 : text.LastIndexOf('\n', lineStart - 1);
        int nextEnd = text.IndexOf('\n', lineEnd + 1);
        string before = text[(previousStart + 1)..lineStart].Trim();
        string after = text[(lineEnd + 1)..(nextEnd < 0 ? text.Length : nextEnd)].Trim();
        string middle = text[(period.Index + period.Length)..lineEnd].Trim();
        Match opening = Regex.Match(middle, $@"(?:开\s*[?？]+|[{Zodiac}]\s*\d{{2}}\s*[中错])\s*$");
        if (!int.TryParse(period.Groups["issue"].Value, out int selectedIssue)
            || !opening.Success
            || ContainsIssueBoundary(before, selectedIssue)
            || ContainsIssueBoundary(after, selectedIssue))
            return false;
        middle = middle[..opening.Index].Trim();
        if (rule.Id == "天线宝杀")
        {
            before = Regex.Replace(before, @"^(?:12码\s*→?\s*)+", "");
            after = Regex.Replace(after, @"\s*←精选杀$", "");
        }
        if (!Regex.IsMatch(before, @"^[0-9 ]+$") || !Regex.IsMatch(after, @"^[0-9 ]+$")
            || !Regex.IsMatch(middle, @"^[0-9 ]*$"))
            return false;

        // These cards wrap a single physical row around its centred issue/result
        // cells. Validate whole lines, never take a numeric prefix from another row.
        string[]? first = ParseNumbers(before);
        string[]? last = ParseNumbers(after);
        string[]? centre = ParseNumbers(middle);
        int expected = int.Parse(rule.Type.AsSpan("号码:".Length));
        if (first is null || last is null || centre is null)
            return true;
        bool validLayout = rule.Id == "内幕"
            ? first.Length == centre.Length && first.Length > last.Length && last.Length > 0
            : first.Length == expected - 1 && centre.Length == 0 && last.Length == 1;
        if (validLayout)
            value = FormatNumbers([.. first, .. centre, .. last], expected);
        return true;
    }

    private static string? ExtractNearbySingleZodiac(string[] lines, int issue, OcrRule rule)
    {
        lines = lines.TakeWhile(line => !Regex.IsMatch(
            SimplifyFixedCardText(line), @"上期\s*开奖\s*结果")).ToArray();

        var standaloneValues = new HashSet<string>(StringComparer.Ordinal);
        for (int issueIndex = 0; issueIndex < lines.Length; issueIndex++)
        {
            if (!ContainsIssue(lines[issueIndex], issue))
                continue;
            foreach (string line in NearbyIssueWindow(lines, issueIndex, issue, before: 4, after: 4))
            {
                string normalized = Normalize(line);
                if (Regex.IsMatch(normalized, $"^[{Zodiac}]$"))
                    standaloneValues.Add(SimplifyOcrText(normalized));
            }
        }
        if (standaloneValues.Count > 0)
            return standaloneValues.Count == 1 ? standaloneValues.Single() : ConflictMarker;

        var values = new HashSet<string>(StringComparer.Ordinal);
        for (int issueIndex = 0; issueIndex < lines.Length; issueIndex++)
        {
            if (!ContainsIssue(lines[issueIndex], issue))
                continue;
            foreach (string line in NearbyIssueWindow(lines, issueIndex, issue, before: 1, after: 1))
            {
                string? value = ExtractSingleZodiac(line);
                if (value is not null)
                    values.Add(value);
            }
        }
        return values.Count == 0 ? null : values.Count == 1 ? values.Single() : ConflictMarker;
    }

    private static string? ExtractNearbyZodiacPair(string[] lines, int issue)
    {
        var zodiacs = new List<string>();
        for (int issueIndex = 0; issueIndex < lines.Length; issueIndex++)
        {
            if (!ContainsIssue(lines[issueIndex], issue))
                continue;
            foreach (string line in NearbyIssueWindow(lines, issueIndex, issue, before: 4, after: 1))
            {
                if (Regex.IsMatch(SimplifyFixedCardText(line), @"(?:上期)?\s*开奖\s*结果"))
                    continue;
                // 开奖格（「蛇38中」「牛05错」）是上一期的结果，不是本资料的剪影值：
                // 窗口里出现它时整行跳过，绝不从中凑第二个生肖。
                if (Regex.IsMatch(SimplifyOcrText(line),
                        $@"[{Zodiac}]\s*\d{{1,2}}\s*[中錯错准準赢贏√✗×]"))
                    continue;
                foreach (char character in SimplifyOcrText(line))
                {
                    if (Zodiac.Contains(character))
                        zodiacs.Add(character.ToString());
                }
            }
        }
        string distinct = string.Concat(zodiacs.Distinct());
        return distinct.Length == 2 ? distinct : null;
    }

    private static IEnumerable<string> NearbyIssueWindow(
        string[] lines,
        int issueIndex,
        int issue,
        int before,
        int after)
    {
        bool IsOtherIssue(string line) =>
            FindIssues(new[] { line }).Any(actual => actual != issue);

        // Values before the first printed issue are allowed for the reviewed
        // poster layout. Once another issue already exists, text before the
        // selected issue belongs to an earlier block and is not eligible.
        bool hasEarlierOtherIssue = lines.Take(issueIndex).Any(IsOtherIssue);
        int start = hasEarlierOtherIssue ? issueIndex : Math.Max(0, issueIndex - before);
        int end = Math.Min(lines.Length, issueIndex + after + 1);

        for (int index = start; index < end; index++)
        {
            if (index != issueIndex && IsOtherIssue(lines[index]))
            {
                // A later issue closes the target block. Never skip the marker
                // and continue into that issue's value rows.
                if (index > issueIndex)
                    yield break;
                continue;
            }
            yield return lines[index];
        }
    }

    // 新澳六合彩资料七张缺项卡：版式都是「期号 | 标签/括号包住的值 | 开奖格」，
    // 值各有各的形状，所以取数在这里按卡分派、校验仍交给统一类型层。
    private static string? ExtractMacauLabelledCard(string[] lines, int issue, OcrRule rule)
    {
        string label = rule.Id switch
        {
            "大赢家杀尾" => "狂赢九尾",
            "铁甲小宝" => "四头出特",
            "特头必中" => "必中四头",
            "帅铁两肖" => "精选",
            "白小姐四季" or "慈善四季" => "四季",
            _ => string.Empty   // 心水两两肖没有标签，整行就是十个生肖
        };
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string rawPayload in LabelledIssuePayload(lines, issue, label, label.Length == 0))
        {
            // 先剥掉期号标记，再检查字段里有没有多余文字：卡面只允许标签、括号、箭头、
            // 「肖/头」这类结构字，混进别的字母或数字说明这行读串了，直接判缺失。
            string payload = Regex.Replace(rawPayload, @"(?<!\d)\d{1,6}\s*期", string.Empty);
            (string Decorations, string Allowed) guard = rule.Id switch
            {
                "大赢家杀尾" => ("狂赢九尾", "[0-9]"),
                "铁甲小宝" => ("四头出特", "[0-9]"),
                "特头必中" => ("必中四头", "[0-9头]"),
                "帅铁两肖" => ("精选", $"[{Zodiac}]"),
                "心水两两肖" => (string.Empty, $"[{Zodiac}]"),
                _ => ("四季", "[四季春夏秋冬肖]")
            };
            string residue = guard.Decorations.Length > 0
                ? Regex.Replace(payload, guard.Decorations, string.Empty)
                : payload;
            residue = Regex.Replace(residue, guard.Allowed, string.Empty);
            // 只把「像字段内容」的多余字符算作读串（字母、数字、其他生肖）；
            // 冒号、括号、箭头、空格这些结构符号不算。
            if (residue.Any(character => char.IsLetterOrDigit(character)))
                continue;
            string? value = rule.Id switch
            {
                "大赢家杀尾" => MacauNineTails(payload),
                "铁甲小宝" or "特头必中" => MacauFourHeads(payload),
                "帅铁两肖" or "心水两两肖" => MacauTenZodiacs(payload),
                _ => MacauSeasons(payload)
            };
            if (value is not null)
                observed.Add(value);
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 连写的九个尾（012346789），缺一个 → 「N尾」。
    private static string? MacauNineTails(string field)
    {
        int[] digits = field.Where(char.IsDigit).Select(character => character - '0').ToArray();
        int[] tails = digits.Distinct().ToArray();
        return digits.Length == 9 && tails.Length == 9
            ? $"{Enumerable.Range(0, 10).Single(number => !tails.Contains(number))}尾"
            : null;
    }

    // 括号里的四个头（「→→→0 1 2 3」/「(( 0 1 2 4 ))」），缺一个 → 「N头」。
    private static string? MacauFourHeads(string field)
    {
        // 四个头可能连写（「0124」）也可能分开（「0 1 2 4」）：先把 0～4 的数字全部
        // 按出现顺序取出来，再要求恰好四个且互不相同，最后反推缺的那个头。
        // 只接受 0～4，出现 5 以上说明读到了别的数字（例如开奖号），直接判缺失。
        string digits = Regex.Replace(field, @"[^0-9]", string.Empty);
        if (digits.Length != 4 || digits.Any(character => character > '4'))
            return null;
        int[] heads = digits.Select(character => character - '0').ToArray();
        int[] distinct = heads.Distinct().ToArray();
        return distinct.Length == 4
            ? $"{Enumerable.Range(0, 5).Single(number => !distinct.Contains(number))}头"
            : null;
    }

    // 十个不同生肖，缺两个 → 按生肖顺序补出缺的两个。
    private static string? MacauTenZodiacs(string field)
    {
        string zodiacs = string.Concat(Regex.Matches(field, $"[{Zodiac}]").Select(match => match.Value).Distinct());
        return zodiacs.Length == 10
            ? string.Concat(Zodiac.Where(zodiac => !zodiacs.Contains(zodiac)))
            : null;
    }

    // 三个不同季节字，按属性表还原九个生肖（顺序：季节按图中、生肖按属性表）。
    private static string? MacauSeasons(string field)
    {
        string seasons = ZodiacAttributes.FamilyCharacters("四季");
        string picked = new(field.Where(seasons.Contains).ToArray());
        if (picked.Length != 3 || picked.Distinct().Count() != 3)
            return null;
        string expanded = string.Concat(picked.Select(season =>
            ZodiacAttributes.TryGetZodiacs(season.ToString(), out string zodiacs) ? zodiacs : string.Empty));
        return expanded.Length == 9 && expanded.Distinct().Count() == 9 ? expanded : null;
    }

    // 新澳高级会员五张卡：版式一样（期号 | 固定标签 + 值 + 准！ | 开奖格），
    // 值各有各的形状，所以取数只走这一处、校验按规则各自的字段：
    //   翩翩公子尾   九个不同尾数，缺一个 → 「N尾」
    //   翩翩公子五行 四个不同五行，缺一个 → 「X行」
    //   翩翩公子半波 恰好一个「红/蓝/绿 + 单/双」
    //   翩翩公子头   恰好一个「X头」，标签「公子禁止1头」里的 1头 不是值
    //   会员暴打     九个不同生肖
    private static string? ExtractMemberLabelledCard(string[] lines, int issue, OcrRule rule)
    {
        string label = rule.Id switch
        {
            "翩翩公子尾" => "公子送尾数",
            "翩翩公子五行" => "公子4行",
            "翩翩公子半波" => "公子秒杀半波",
            "翩翩公子头" => "公子禁止1头",
            _ => "会员九肖"
        };
        // 「公子送尾数」这张卡的标签可能没被读出来，此时只要本期行里是一串纯尾数
        // 加「准！」的写法，也按同一张卡取值（该规则本来就允许没有关键词）。
        bool labelOptional = rule.AllowValueWithoutKeyword;
        var observed = new HashSet<string>(StringComparer.Ordinal);
        // 翩翩公子尾的值会被开奖格切成两行（「…0123467 开??」+「89 准！」），
        // 这一条要越过开奖格继续读到「准！」为止；其余四条都停在开奖格。
        IEnumerable<string> payloads = rule.Id == "翩翩公子尾"
            ? MemberWrappedTailPayloads(lines, issue)
            : LabelledIssuePayload(lines, issue, label, labelOptional);
        foreach (string payload in payloads)
        {
            // 「准！」是这张卡自己的结尾标记，值到它就结束。
            string field = Regex.Split(payload, "准|準")[0];
            string? value = rule.Id switch
            {
                "翩翩公子尾" => MemberNineTails(field),
                "翩翩公子五行" => MemberFourElements(field),
                "翩翩公子半波" => MemberSingleHalfWave(field),
                "翩翩公子头" => MemberSingleHead(field),
                _ => MemberNineZodiacs(field)
            };
            if (value is not null)
                observed.Add(value);
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    // 翩翩公子尾专属：这张卡把尾数印在开奖格两侧，形如
    //   297期 | 公子送尾数：0 1 2 4 5 6 7 | 开?? | 8 9 准！
    // 所以从本期行开始、越过「开??」继续收到「准！」，把两侧数字拼成一个字段。
    // 遇到下一个期号立即停，绝不跨期；标签「公子送尾数」缺失时整段就是字段。
    private static IEnumerable<string> MemberWrappedTailPayloads(string[] lines, int issue)
    {
        for (int index = 0; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue))
                continue;
            var text = new System.Text.StringBuilder(SimplifyOcrText(lines[index]));
            bool closed = text.ToString().Contains('准') || text.ToString().Contains('準');
            for (int next = index + 1; next < lines.Length && !closed; next++)
            {
                string following = lines[next].Trim();
                if (following.Length == 0 || ContainsAnyIssue(following))
                    break;
                text.Append(' ').Append(SimplifyOcrText(following));
                closed = following.Contains('准') || following.Contains('準');
            }
            string payload = text.ToString();
            int at = payload.IndexOf("公子送尾数", StringComparison.Ordinal);
            if (at >= 0)
                payload = payload[(at + "公子送尾数".Length)..];
            yield return Regex.Split(payload, "准|準")[0];
        }
    }

    // 九个不同尾数（卡面可能连写「0123467」+「89」，也可能空格分开），缺一个 → 「N尾」。
    // 判定只看数字总数：必须正好九个数字且九个都不同；多一个、少一个、有重复都判缺失。
    private static string? MemberNineTails(string field)
    {
        int[] digits = field.Where(char.IsDigit).Select(character => character - '0').ToArray();
        int[] tails = digits.Distinct().ToArray();
        return digits.Length == 9 && tails.Length == 9
            ? $"{Enumerable.Range(0, 10).Single(number => !tails.Contains(number))}尾"
            : null;
    }

    // 四个不同五行，按印出顺序原样交出四个字；「缺哪个」由统一的格式层反推（输出带「行」）。
    private static string? MemberFourElements(string field)
    {
        string value = string.Concat(Regex.Matches(field, "[金木水火土]").Select(match => match.Value));
        return value.Length == 4 && value.Distinct().Count() == 4 ? value : null;
    }

    // 恰好一个半波；出现两个不同组合就判缺失。
    private static string? MemberSingleHalfWave(string field)
    {
        string[] found = Regex.Matches(field, "(?<color>[红蓝绿])\\s*波?\\s*(?<parity>[单双])")
            .Select(match => match.Groups["color"].Value + match.Groups["parity"].Value)
            .Distinct(StringComparer.Ordinal).ToArray();
        return found.Length == 1 ? found[0] : null;
    }

    // 恰好一个头。标签「公子禁止1头」本身含「1头」，必须先把标签剥掉再取值。
    private static string? MemberSingleHead(string field)
    {
        string stripped = Regex.Replace(field, @"公子禁止\s*[1一]\s*头", string.Empty);
        string[] found = Regex.Matches(stripped, @"(?<!\d)(?<head>[0-4零一二三四])\s*头")
            .Select(match => $"{ToArabicDigit(match.Groups["head"].Value[0])}头")
            .Distinct(StringComparer.Ordinal).ToArray();
        return found.Length == 1 ? found[0] : null;
    }

    // 九个不同生肖。
    private static string? MemberNineZodiacs(string field)
    {
        string value = string.Concat(Regex.Matches(field, $"[{Zodiac}]").Select(match => match.Value));
        return value.Length == 9 && value.Distinct().Count() == 9 ? value : null;
    }

    // 新澳高级会员「藏宝九肖」：卡面每期只印三个方位，
    // 这张卡是「九个生肖」用方位表达，按属性表还原成生肖再输出（东=兔虎龙…），
    // 方位顺序按原图，生肖顺序按属性表；方位固定就是 东西南北（用户确认）。
    // 本期必须恰好印三个不同方位，多/少/重复一律缺失；
    // 同期在图上出现多次（表头“263期开…”、数据行“263期”）时只认形状完整的切片，
    // 两个不同的完整答案按冲突处理。
    private static string? ExtractMemberArts(string[] lines, int issue, OcrRule rule)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (string payload in MemberArtsPayloads(lines, issue, rule))
        {
            if (!Regex.IsMatch(payload, @"^(?:[琴棋书画]肖){3}$"))
                continue;
            string value = payload.Replace("肖", "");
            if (value.Distinct().Count() != 3)
                continue;
            // 琴棋书画与生肖的对应关系只在 ZodiacAttributes 的属性表里存一份，别处不要另抄。
            // 三个属性字按卡面顺序展开成九个生肖，与四季/方位一样按「恰好 9 个不同生肖」落库。
            string expanded = string.Concat(value.Select(art =>
                ZodiacAttributes.TryGetZodiacs(art.ToString(), out string zodiacs) ? zodiacs : string.Empty));
            if (expanded.Length == 9 && expanded.Distinct().Count() == 9)
                values.Add(expanded);
        }
        return values.Count switch { 0 => null, 1 => values.Single(), _ => ConflictMarker };
    }

    // 会员琴棋与大赢家琴棋是两张不同的卡，当期行上印的字段名也不同（「会员特供」/「赢家举荐」）：
    // 定位前缀按规则取，别的规则仍走「会员特供」，行为不变。
    private static string MemberArtsFieldPrefix(OcrRule rule) =>
        rule.Id == "大赢家琴棋" ? "赢家举荐" : "会员特供";

    // 会员琴棋/大赢家琴棋的当期字段（前缀字后的那一段）：提取与失败原因共用同一段定位，
    // 诊断不再另数一遍整张图的属性字。
    private static IEnumerable<string> MemberArtsPayloads(string[] lines, int issue, OcrRule rule)
    {
        string prefix = MemberArtsFieldPrefix(rule);
        string text = Regex.Replace(SimplifyOcrText(string.Concat(lines)), @"\s+", "")
            .Replace('書', '书').Replace('畫', '画');
        foreach (Match period in Regex.Matches(text, $@"(?<!\d){issue}期"))
        {
            string block = text[(period.Index + period.Length)..];
            block = Regex.Split(block, @"(?<!\d)\d+期")[0];
            // Opening banner is not a member field; never borrow the legend above the table.
            if (!block.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            string payload = block[prefix.Length..].TrimStart(':', '：');
            yield return Regex.Split(payload, @"开|開|[鼠牛虎兔龙龍蛇马馬羊猴鸡雞狗猪豬][0-9]")[0];
        }
    }

    private static string? ExtractTreasureDirections(string[] lines, int issue, OcrRule rule)
    {
        if (rule.Id != "藏宝九肖")
            return null;

        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string row in TreasureDirectionRows(lines, issue))
        {
            MatchCollection printed = Regex.Matches(row, "[东西南北]肖");
            if (printed.Count != 3)
                continue;
            string[] directions = printed.Select(match => match.Value[..1]).ToArray();
            if (directions.Distinct(StringComparer.Ordinal).Count() != 3)
                continue;
            // 方位与生肖的对应关系只在 ZodiacAttributes 的属性表里存一份，别处不要另抄。
            string expanded = string.Concat(directions.Select(direction =>
                ZodiacAttributes.TryGetZodiacs(direction.ToString(), out string zodiacs) ? zodiacs : string.Empty));
            if (expanded.Length == 9 && expanded.Distinct().Count() == 9)
                observed.Add(expanded);
        }
        return observed.Count switch
        {
            0 => null,
            1 => observed.Single(),
            _ => ConflictMarker
        };
    }

    private static string? ExtractLeadingSplitNumberTable(
        string text, MatchCollection periods, int issue, OcrRule rule)
    {
        if (!SplitIssueNumberRuleIds.Contains(rule.Id))
            return null;
        // These explicitly reviewed cards have a physical row that can straddle
        // the issue/opening cells. Every other rule is forbidden from this repair.
        if (rule.Id == "翩翩公子杀十码")
            return null;
        if (periods.Count == 0
            || rule.Type is not { } type
            || !rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            || !int.TryParse(type.AsSpan("号码:".Length), out int expectedCount))
            return null;

        List<(int Position, int Value)>? tokens = ExtractPositionedTableNumbers(text, periods, expectedCount);
        if (tokens is null)
            return null;
        Match firstPeriod = periods[0];
        int leadingCount = tokens.Count(token => token.Position < firstPeriod.Index);
        if (leadingCount == 0 || leadingCount >= expectedCount)
            return null;

        var values = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < periods.Count; index++)
        {
            Match target = periods[index];
            if (!int.TryParse(target.Groups["issue"].Value, out int actualIssue) || actualIssue != issue)
                continue;
            int nextPeriodIndex = index + 1 < periods.Count ? periods[index + 1].Index : text.Length;
            int intervalCount = tokens.Count(token => token.Position > target.Index && token.Position < nextPeriodIndex);
            bool validInterval = index + 1 < periods.Count
                ? intervalCount == expectedCount
                    || expectedCount < 35 && index == 0 && leadingCount > 0
                        && intervalCount == expectedCount - leadingCount
                : intervalCount == expectedCount - leadingCount;
            if (!validInterval)
                return null;

            int[] numbers = tokens.Where(token => token.Position < target.Index)
                .TakeLast(leadingCount)
                .Concat(tokens.Where(token => token.Position > target.Index).Take(expectedCount - leadingCount))
                .Select(token => token.Value)
                .ToArray();
            if (numbers.Length != expectedCount || numbers.Distinct().Count() != expectedCount
                || numbers.Any(number => number is < 1 or > 49))
                return null;
            values.Add(string.Join(' ', numbers.Select(number => number.ToString("00"))));
        }
        return values.Count == 0 ? null : values.Count == 1 ? values.Single() : ConflictMarker;
    }

    private static List<(int Position, int Value)>? ExtractPositionedTableNumbers(
        string text, MatchCollection periods, int expectedCount)
    {
        var tokens = new List<(int Position, int Value)>();
        foreach (Match run in Regex.Matches(text, @"\d+"))
        {
            if (periods.Cast<Match>().Any(period => run.Index >= period.Index
                    && run.Index < period.Index + period.Length))
                continue;
            string following = text[(run.Index + run.Length)..];
            if (Regex.IsMatch(following, @"^\s*%"))
                continue;
            if (int.TryParse(run.Value, out _)
                && Regex.IsMatch(following, @"^\s*(?:个(?:中特码|特码)?|個(?:码中特碼|特碼)?|码|碼|计|計)"))
                continue;
            string preceding = text[..run.Index].TrimEnd();
            if (preceding.Length > 0 && (preceding[^1] == '开' || preceding[^1] == '特'
                    || Zodiac.Contains(preceding[^1])))
                continue;
            if (run.Length % 2 != 0)
                return null;
            for (int offset = 0; offset < run.Length; offset += 2)
            {
                string pair = run.Value.Substring(offset, 2);
                if (int.TryParse(pair, out int value))
                    tokens.Add((run.Index + offset, value));
            }
        }
        return tokens;
    }

    private static string? ExtractStrictTableValue(string block, OcrRule rule)
    {
        if (rule.Id == "翩翩公子尾" && rule.Type == "缺尾")
        {
            Match fixedCard = Regex.Match(block,
                @"公子送尾数\s*[:：]\s*(?<digits>[0-9\s]{9,})\s*准[!！]?",
                RegexOptions.Singleline);
            if (fixedCard.Success)
            {
          int[] tails = fixedCard.Groups["digits"].Value
              .Where(char.IsDigit).Select(c => c - '0').Distinct().ToArray();
          int digitCount = fixedCard.Groups["digits"].Value.Count(char.IsDigit);
          return digitCount == 9 && tails.Length == 9
              ? $"{Enumerable.Range(0, 10).Single(n => !tails.Contains(n))}尾"
                    : null;
            }
            Match wrapped = Regex.Match(block,
                $@"\A公子送尾数\s*[:：]\s*(?<first>[0-9 $]+)(?:开\s*[?？]+|[{Zodiac}]\s*\d{{2}}\s*[中错])\s*\n\s*(?<last>[0-9 ]+)\s*准[!！]?\s*\z");
            if (wrapped.Success)
                block = wrapped.Groups["first"].Value + " " + wrapped.Groups["last"].Value;
        }
        // An opening column can say "牛18中" without 开. Stop before it and
        // before 准; neither the result nor later OCR lines may fill a deficit.
        if (rule.AllowOpeningRow)
        {
            // 白小姐来钱的九肖写在开奖行上（开:狗豬…馬），行首的「开」是这一行自己的
            // 标记，不是上一期的开奖结果，所以要在按「开」截断之前先剥掉它。
            block = Regex.Replace(block.TrimStart(), @"^开\s*[:：]?\s*", string.Empty);
        }
        block = Regex.Split(block, $@"(?<!不会)(?<!不)(?<!避)开|准|準|[{Zodiac}]\s*\d{{1,2}}\s*[中错錯赢贏]")[0];
        if (rule.Folder is "68" or "香奈儿" or "战狼" or "红人馆")
        {
            block = ExtractHuangdaxianPayload(Regex.Replace(block, @"\s+", ""), rule);
            if (block.Length == 0)
                return null;
        }
        else if (rule.Folder is "各种杀" or "公式杀料" or "一套组合拳" or "综合")
        {
            block = ExtractDragonflyPayload(Regex.Replace(block, @"\s+", ""), rule);
            if (block == ConflictMarker)
                return ConflictMarker;
            if (block.Length == 0)
                return null;
        }
        else if (rule.Folder is "亚太地区" or "战澳团队")
        {
            block = ExtractAsiaPacificPayload(block, rule);
            if (block == ConflictMarker)
                return ConflictMarker;
            if (block.Length == 0)
                return null;
        }
        if (Regex.IsMatch(block, @"[?？]"))
            return null;
        // Keep the pre-cleanup raw form so number containers (brackets) reach
        // the shared integrity checker instead of being flattened into a
        // digit soup that can add two incomplete containers into one value.
        string rawContainerBlock = block;
        // Decorations are separators, not values. Keep unknown letters and
        // digits so malformed OCR cannot silently become a successful result.
        block = Regex.Replace(block, @"[^\p{L}\p{N}\s]", " ").Trim();
        if (rule.Id == "藏宝十二码" && rule.Type == "号码:12")
        {
            block = Regex.Replace(block, @"^藏宝库\s*杀一波12码\s*", "");
            block = Regex.Replace(block, @"^杀\s*[红蓝绿]\s*", "");
            block = Regex.Replace(block, @"[红蓝绿]\s*[中错]\s*$", "");
        }
        if (rule.Id == "藏宝头" && rule.Type == "头")
        {
            // The card title may live in a different strict block than the
            // selected issue, so the field pattern itself is the identity here.
            Match heads = Regex.Match(block, @"今晚买\s*[【\[]?(?<values>[0-4\s]{4,})[】\]]?\s*头\s*$");
            if (!heads.Success)
                return null;
            char[] rawDigits = heads.Groups["values"].Value
                .Where(c => c is >= '0' and <= '4')
                .ToArray();
            if (rawDigits.Length != 4 || rawDigits.Distinct().Count() != 4)
                return null;
            string digits = new(rawDigits);
            return $"{Enumerable.Range(0, 5).Single(n => !digits.Contains((char)('0' + n)))}头";
        }
        if (rule.Id == "亮剑九肖" && rule.Type == "九肖")
        {
            // 亮剑团队「9肖中特」海报没有期号：标题下面就是 3×3 九个生肖。
            // 只认「肖中特」标记之后的九个不同生肖，多一个、少一个或重复都判缺失。
            string field = SimplifyOcrText(block);
            int marker = field.IndexOf("肖中特", StringComparison.Ordinal);
            if (marker < 0)
                return null;
            MatchCollection zodiacs = Regex.Matches(
                field[(marker + "肖中特".Length)..], $"[{Zodiac}]");
            string nine = string.Concat(zodiacs.Select(match => match.Value));
            return zodiacs.Count == 9 && nine.Distinct().Count() == 9 ? nine : null;
        }
        if (rule.Id == "小骚货" && rule.Type == "九肖")
        {
            // 这张卡把「九肖」拆成两行（…快乐的骚货九 / 肖】【九个生肖…），
            // 标记必须容忍行内空白，否则拆行卡永远取不到值。
            string field = SimplifyOcrText(block);
            Match marker = Regex.Match(field, @"九\s*肖");
            if (!marker.Success)
                return null;
            field = field[(marker.Index + marker.Length)..];
            int tierEnd = new[] { "六肖", "三肖", "一肖" }
                .Select(tier => field.IndexOf(tier, StringComparison.Ordinal))
                .Where(index => index >= 0)
                .DefaultIfEmpty(field.Length)
                .Min();
            field = field[..tierEnd];
            MatchCollection zodiacs = Regex.Matches(field, $"[{Zodiac}]");
            if (zodiacs.Count != 9)
                return null;
            string nineValue = string.Concat(zodiacs.Select(match => match.Value));
            return nineValue.Distinct().Count() == 9 ? nineValue : null;
        }
        if (rule.Type == "缺两肖")
        {
            string zodiacs = string.Concat(Regex.Matches(block, $"[{Zodiac}]")
                .Select(match => match.Value).Distinct());
            return zodiacs.Length == 10
                ? string.Concat(Zodiac.Where(zodiac => !zodiacs.Contains(zodiac)))
                : null;
        }
        if (rule.Id is "曾道人小杀肖" or "心水杀肖肖肖" or "聚彩堂一肖" or "姨妈肖杀")
        {
            string zodiacs = string.Concat(Regex.Matches(block, $"[{Zodiac}]")
                .Select(match => match.Value).Distinct());
            return zodiacs.Length == 1 ? zodiacs : null;
        }
        if (rule.Id is "聚彩堂一尾" or "姨妈尾杀")
        {
            Match tail = Regex.Match(block, @"(?<tail>[0-9０-９])\s*尾");
            if (!tail.Success)
                return null;
            char digit = tail.Groups["tail"].Value[0];
            return $"{(digit is >= '０' and <= '９' ? (char)('0' + digit - '０') : digit)}尾";
        }
        if (rule.Id is "大赢家九肖" or "会员暴打")
        {
            string zodiacs = string.Concat(Regex.Matches(block, $"[{Zodiac}]")
                .Select(match => match.Value));
            return zodiacs.Length == 9 && zodiacs.Distinct().Count() == 9 ? zodiacs : null;
        }
        if (rule.Id is "祖师公肖" or "祖师公尾")
        {
            Match shared = Regex.Match(block, @"^(?:①|1)\s*杀\s*(?<zodiac>.*?)\s*肖\s*(?:①|1)\s*杀\s*(?<tail>.*?)\s*尾$",
                RegexOptions.Singleline);
            if (!shared.Success)
                return null;
            block = shared.Groups[rule.Type == "生肖" ? "zodiac" : "tail"].Value;
        }
        // These fixed cards wrap values in field labels; keep the whole payload
        // and validate every repeated symbol before using the shared type checks.
        string? fixedField = rule.Id switch
        {
            "买什么杀一肖" => @"^1\s*肖\s*今期买\s*(?<value>.+?)\s*输光光$",
            "买什么杀十码" => @"^今期\s*10\s*码\s*[:：]?\s*(?<value>.+?)\s*直接不要$",
            "买什么杀半波" => @"^1\s*肖\s*(?<value>.+)$",
            "买什么公式杀行" => @"^绝杀一行\s*(?<value>.+)$",
            "买什么公式杀尾" => @"^金牌绝杀一尾\s*(?<value>.+)$",
            "买什么公式杀肖" => @"^金牌绝杀一肖\s*(?<value>.+)$",
            "唐朝杀行" or "唐朝杀头" => @"^唐朝绝杀\s*(?<value>.+?)\s*稳\s*杀$",
            "唐朝杀尾" => @"^唐朝绝杀\s*(?<value>.+?)\s*尾\s*稳\s*杀$",
            "唐朝杀肖" => @"^唐朝绝杀\s*(?<value>.+?)\s*肖\s*稳\s*杀$",
            "黑客杀两肖" => @"^数据杀特肖\s*(?<value>.+)$",
            "老码头杀七码" => @"^绝杀\s*7\s*码\s*[:：]?\s*(?<value>.+?)\s*稳\s*稳\s*稳$",
            "锦囊杀尾" => @"^[锦錦]囊九尾\s*(?<value>.+)$",
            "天宫杀头" => @"^天宫\s*杀\s*(?<value>.+?)\s*头$",
            _ => null
        };
        if (fixedField is not null)
        {
            Match field = Regex.Match(rule.Type.StartsWith("号码:", StringComparison.Ordinal)
                ? rawContainerBlock.Trim() : block, fixedField, RegexOptions.Singleline);
            if (!field.Success)
                return null;
            rawContainerBlock = field.Groups["value"].Value.Trim();
            block = Regex.Replace(rawContainerBlock, @"[^\p{L}\p{N}\s]", " ").Trim();
            if (rule.Id is "买什么公式杀行" or "买什么公式杀尾" or "买什么公式杀肖"
                or "唐朝杀行" or "唐朝杀尾" or "唐朝杀肖" or "唐朝杀头")
            {
                string repeats = Regex.Replace(block, @"\s+", "");
                if (rule.Id == "唐朝杀头")
                {
                    if (!Regex.IsMatch(repeats, @"^(?:[零一二三四0-4]头){3}$"))
                        return null;
                    repeats = string.Concat(repeats.Where(c => c != '头').Select(ToArabicDigit));
                }
                if (repeats.Length != 3 || repeats.Distinct().Count() != 1)
                    return null;
                block = repeats[..1];
            }
        }
        string prefix = rule.Type switch
        {
            var type when type.StartsWith("号码:", StringComparison.Ordinal) =>
                @"(?:公子神算杀?10码|绝杀|庄家必杀|十不中|精杀12码|12码|包围36码|36码|36计|强哥三十六码|锁三十六码|35码\s*赛马会|杀四个特码|杀特码|吃码|稳杀|不开|杀|锦囊杀\s*10\s*码)",
            "缺尾" => @"(?:公子送尾数|狂赢九尾)",
            "缺头" => @"(?:四头出特|必中四头)",
            "生肖" => @"(?:公子杀一肖|帅铁杀一肖)",
            "生肖组合" => @"(?:祥瑞阁杀|[红绿蓝黄紫白黑灰青金银]杀|杀生肖|今期庄吃|(?:广东|福建|广西|贵州|海南|江西|湖南|上海|深圳|云南|四川)报|今晚杀|杀特肖)",
            "色单双" => @"(?:公子秒杀半波|今期特码杀|赢家必杀)",
            "半波" => @"(?:(?:红红半波|粉红半波|蓝黑半波)\s*)+",
            "五行" => @"(?:公子4行|大赢家四行|必中)",
            "头" => @"(?:公子禁止[1一]头|帅铁杀一头|今晚你买|特杀|禁|买|特码绝杀|特码避开|财神禁)",
            "尾" => @"(?:一尾绝杀|帅铁杀一尾)",
            "尾数组合" => @"(?:特码封杀|精杀)",
            "段" => "杀",
            _ => "(?!)"
        };
        block = Regex.Replace(block.Trim(), "^" + prefix + @"\s*", "");
        block = Regex.Replace(block.Trim(), @"(?:精选杀|必输|会输很惨|不会开)$", "").Trim();
        block = block.Trim(' ', '卜', 'ト', 'ł');
        if (rule.Id == "雷锋")
        {
            if (!Regex.IsMatch(block, $@"^(?:[{Zodiac}]\s*[0-9]+\s*){{4}}$"))
                return null;
            block = Regex.Replace(block, $"[{Zodiac}]", " ");
        }
        if (rule.Id == "杀料五码")
        {
            // OCR may duplicate the card's leading "杀→" label (for example
            // "杀→杀→猪虎羊猴狗08 23 29 33 48").  The repeated label is
            // structural noise, not a sixth field value; remove only leading
            // kill markers before validating the five zodiac/number pairs.
            block = Regex.Replace(block.Trim(), @"^(?:杀\s*)+", string.Empty);
            Match mixed = Regex.Match(block, $@"^(?:[{Zodiac}]\s*){{5}}(?<numbers>[0-9\s]+)$");
            if (!mixed.Success)
                return null;
            block = mixed.Groups["numbers"].Value;
        }
        string value = Regex.Replace(block, @"\s+", "");

        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal))
        {
            // 配置写坏（如 "号码:"、"号码:12码"）时按缺失返回，不能抛 FormatException
            // 中断整轮识别；数量硬校验仍由下面的计数逻辑执行。
            if (!int.TryParse(rule.Type.AsSpan("号码:".Length), out int count) || count <= 0)
                return null;
            // Strict number fields that still contain their original containers
            // are validated by the shared checker: each container must be
            // complete on its own, incomplete containers are counter-evidence
            // and two different complete answers are a conflict.
            if (!rule.DedupeNumbers
                && rule.Id is not ("藏宝十二码" or "雷锋" or "杀料五码")
                && rule.Folder is not ("68" or "香奈儿" or "战狼" or "红人馆"
                    or "各种杀" or "公式杀料" or "一套组合拳")
                && Regex.IsMatch(rawContainerBlock, @"[【\[（(】\]）)]"))
            {
                string raw = Regex.Replace(rawContainerBlock.Trim(), "^" + prefix + @"\s*", "");
                raw = Regex.Replace(raw.Trim(), @"(?:精选杀|必输|会输很惨|不会开)$", "").Trim();
                string outsideContainers = Regex.Replace(raw, @"[【\[（(][^】\]）)]*[】\]）)]", "");
                if (Regex.IsMatch(outsideContainers, @"[【\[（(】\]）)]"))
                    return null;
                return ExtractNumbers(raw, count);
            }
            string numberText = Regex.Replace(block, @"\s+", " ").Trim();
            if (!Regex.IsMatch(numberText, @"^[0-9]+(?: [0-9]+)*$"))
                return null;
            var numbers = new List<int>();
            foreach (string run in numberText.Split(' '))
            {
                if (run.Length % 2 != 0)
                    return null;
                for (int offset = 0; offset < run.Length; offset += 2)
                    numbers.Add(int.Parse(run.Substring(offset, 2)));
            }
            if (rule.DedupeNumbers)
            {
                // Opt-in exception: a printed duplicate is de-duplicated in order
                // instead of failing the whole row. 去重只吞掉重复值，不放松数量：
                // 去重后仍必须正好是配置的个数，否则等于把「漏读几个」判成成功。
                int[] unique = numbers.Distinct().ToArray();
                return unique.Length == count
                    && unique.All(number => number is >= 1 and <= 49)
                        ? string.Join(' ', unique.Select(number => number.ToString("00"))) : null;
            }
            return numbers.Count == count && numbers.Distinct().Count() == count
                && numbers.All(number => number is >= 1 and <= 49)
                    ? string.Join(' ', numbers.Select(number => number.ToString("00"))) : null;
        }

        if (rule.Type == "缺尾")
            return Regex.IsMatch(value, "^[0-9]{9}$") && value.Distinct().Count() == 9
                ? $"{Enumerable.Range(0, 10).Single(number => !value.Contains((char)('0' + number)))}尾" : null;
        // 缺五行：卡面印四个五行字（可能重复），输出五行里没出现的那个，带「行」后缀。
        if (rule.Type == "缺五行")
        {
            char[] printed = value.Where("金木水火土".Contains).Distinct().ToArray();
            return printed.Length == 4
                ? $"{"金木水火土".Single(c => !printed.Contains(c))}行" : null;
        }
        // 缺半波：六种半波里印出五种，输出没出现的那个，形如「蓝单」。
        if (rule.Type == "缺半波")
        {
            string[] all = ["红单", "红双", "蓝单", "蓝双", "绿单", "绿双"];
            var printed = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match wave in Regex.Matches(
                SimplifyOcrText(value), @"(?<color>[红蓝绿])波?(?<parity>[单双])"))
                printed.Add(wave.Groups["color"].Value + wave.Groups["parity"].Value);
            string[] missing = all.Where(item => !printed.Contains(item)).ToArray();
            return printed.Count == 5 && missing.Length == 1 ? missing[0] : null;
        }
        if (rule.Type == "五行")
        {
            value = Regex.Replace(value, "码$", "");
            return Regex.IsMatch(value, "^[金木水火土]{4}$") && value.Distinct().Count() == 4 ? value : null;
        }
        if (rule.Type == "生肖")
            return Regex.IsMatch(value, $"^[{Zodiac}]{{1,3}}$") && value.Distinct().Count() == 1
                && (rule.Id == "翩翩公子肖" || value.Length == 1) ? value[..1] : null;
        if (rule.Type == "生肖组合")
            return Regex.IsMatch(value, $"^[{Zodiac}]{{2}}$") && value.Distinct().Count() == 2 ? value : null;
        if (rule.Type is "色单双" or "半波")
            return Regex.IsMatch(value, "^[红蓝绿]波?[单双]$") ? value.Replace("波", "") : null;
        if (rule.Type == "头")
            return Regex.IsMatch(value, "^[0-4零一二三四]头?$") ? $"{ToArabicDigit(value[0])}头" : null;
        if (rule.Type == "尾")
        {
            value = Regex.Replace(value, "尾$", "");
            return Regex.IsMatch(value, "^[0-9]+$") && value.Distinct().Count() == 1
                && (value.Length == 1 || rule.Id == "宝典尾" && value.Length == 5) ? $"{value[0]}尾" : null;
        }
        if (rule.Type == "尾数组合")
        {
            if (!Regex.IsMatch(value, "^[0-9]尾?[0-9]尾?$"))
                return null;
            value = value.Replace("尾", "");
            return value[0] != value[1] ? $"{value[0]}尾+{value[1]}尾" : null;
        }
        if (rule.Type == "缺头")
        {
            value = Regex.Replace(value, "头$", "");
            return Regex.IsMatch(value, "^[0-4]{4}$") && value.Distinct().Count() == 4
                ? $"{Enumerable.Range(0, 5).Single(n => !value.Contains((char)('0' + n)))}头" : null;
        }
        if (rule.Type == "段")
            return Regex.IsMatch(value, "^[0-7]段$") ? value : null;
        if (rule.Type == "四季")
        {
            // 四季型资料（白小姐四季）的期行只写三个季节字：四季:夏肖秋肖冬肖。
            // 这张卡是「九个生肖」用四季表达，按属性表还原成生肖再输出
            // （夏秋冬 → 马蛇羊鸡猴狗鼠猪牛），季节与生肖的对应关系只在
            // ZodiacAttributes 里存一份，别处不要另抄。
            string seasons = ZodiacAttributes.FamilyCharacters("四季");
            string picked = new(value.Where(seasons.Contains).ToArray());
            if (picked.Length != 3 || picked.Distinct().Count() != 3)
                return null;
            string expanded = string.Concat(picked.Select(season =>
                ZodiacAttributes.TryGetZodiacs(season.ToString(), out string zodiacs) ? zodiacs : string.Empty));
            return expanded.Length == 9 && expanded.Distinct().Count() == 9 ? expanded : null;
        }
        if (rule.Type == "九肖")
        {
            return Regex.IsMatch(value, $"^[{Zodiac}]{{9}}$") && value.Distinct().Count() == 9 ? value : null;
        }
        if (rule.Type == "单五行")
            return Regex.IsMatch(value, "^[金木水火土]$") ? value : null;
        if (rule.Type == "合")
        {
            Match sum = Regex.Match(value, @"^(?<value>0?[1-9]|1[0-3])合$");
            return sum.Success ? $"{int.Parse(sum.Groups["value"].Value):00}合" : null;
        }
        if (rule.Type == "半头")
            return Regex.IsMatch(value, "^[0-4]头[单双]$") ? value : null;
        return null;
    }

    private static bool FormulaBlockAppliesToRule(string block, OcrRule rule)
    {
        MatchCollection markers = Regex.Matches(block, @"[=＝]\s*杀");
        if (markers.Count == 0)
            return false;
        Match marker = markers[^1];
        string payload = block[(marker.Index + marker.Length)..];
        bool hasZodiac = Regex.IsMatch(payload, $"[{Zodiac}]");
        return rule.Type == "生肖组合" ? hasZodiac
            : rule.Type == "尾数组合" && !hasZodiac && Regex.IsMatch(payload, @"\d.*尾");
    }

    private static string ExtractDragonflyPayload(string block, OcrRule rule)
    {
        if (rule.Id is "黑字杀头" or "黑字杀行" or "黑字杀合")
        {
            // 黑字共享表：每行固定「N期 | X头 | 五行 | NN合 | 开:…」，三列各取一列。
            // 只认本期那一行，别的期号一律不参与；三列里缺自己那一列就判缺失。
            string row = Regex.Replace(block, @"[^\p{L}\p{N}]", "");
            Match columns = Regex.Match(row,
                @"^(?<head>[0-4]头)?(?<element>[金木水火土])?(?<sum>(?:0?[1-9]|1[0-3])合)?$");
            if (!columns.Success)
                return string.Empty;
            string value = rule.Id switch
            {
                "黑字杀头" => columns.Groups["head"].Value,
                "黑字杀行" => columns.Groups["element"].Value,
                _ => columns.Groups["sum"].Value
            };
            return value.Length > 0 ? value : string.Empty;
        }

        string pattern = rule.Id switch
        {
            "绿格子双杀" => @"绝杀2肖",
            "公式杀两肖肖" or "公式杀两尾尾" => @"[=＝]杀",
            "红蜻蜓" => @"红蜻蜓必中(?:⑨|9)肖",
            "红蜻蜓杀一肖" => @"红蜻蜓绝杀一肖",
            "神奇宇宙" => @"神秘宇宙绝杀半波",
            "墨羽" => @"墨羽尘曦(?:精)?杀一肖",
            _ => "(?!)"
        };
        MatchCollection markers = Regex.Matches(block, pattern, RegexOptions.Singleline);
        if (markers.Count == 0)
            return string.Empty;
        bool formulaRule = rule.Id is "公式杀两肖肖" or "公式杀两尾尾";
        if (!formulaRule && markers.Count > 1)
        {
            var repeatedValues = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < markers.Count; index++)
            {
                Match repeatedMarker = markers[index];
                int start = repeatedMarker.Index + repeatedMarker.Length;
                int end = index + 1 < markers.Count ? markers[index + 1].Index : block.Length;
                string segment = block[start..end];
                string candidate = ExtractImmediateDragonflyValue(segment);
                if (candidate.Length == 0 || candidate == ConflictMarker)
                    return ConflictMarker;
                repeatedValues.Add(candidate);
            }
            if (repeatedValues.Count != 1)
                return ConflictMarker;
            return repeatedValues.Single();
        }

        Match marker = formulaRule ? markers[^1] : markers[0];
        string payload = block[(marker.Index + marker.Length)..];
        if (rule.Id == "绿格子双杀")
        {
            MatchCollection framedValues = Regex.Matches(payload,
                @"【(?<value>[^】]+)】|\[(?<value>[^\]]+)\]|（(?<value>[^）]+)）|\((?<value>[^\)]+)\)");
            if (framedValues.Count != 1)
                return string.Empty;
            return framedValues[0].Groups["value"].Value;
        }
        string framedPayload = Regex.Replace(payload, @"^[：:，,、\-\s]*", "");
        var leadingFrames = new List<string>();
        int frameOffset = 0;
        while (frameOffset < framedPayload.Length)
        {
            Match frame = Regex.Match(framedPayload[frameOffset..],
                @"^(?:【(?<value>[^】]+)】|\[(?<value>[^\]]+)\]|（(?<value>[^）]+)）|\((?<value>[^\)]+)\)|《(?<value>[^》]+)》|『(?<value>[^』]+)』|\{(?<value>[^}]+)\})");
            if (!frame.Success)
                break;
            leadingFrames.Add(frame.Groups["value"].Value);
            frameOffset += frame.Length;
            Match separator = Regex.Match(framedPayload[frameOffset..], @"^[：:，,、+\-\s]*");
            frameOffset += separator.Length;
        }
        if (leadingFrames.Count > 0)
        {
            string[] distinctFrames = leadingFrames.Distinct(StringComparer.Ordinal).ToArray();
            return distinctFrames.Length == 1 ? distinctFrames[0] : ConflictMarker;
        }
        return Regex.Split(payload, @"发|發")[0].Trim();
    }

    private static string ExtractImmediateDragonflyValue(string payload)
    {
        string framedPayload = Regex.Replace(payload, @"^[：:，,、\-\s]*", "");
        var leadingFrames = new List<string>();
        int frameOffset = 0;
        while (frameOffset < framedPayload.Length)
        {
            Match frame = Regex.Match(framedPayload[frameOffset..],
                @"^(?:【(?<value>[^】]+)】|\[(?<value>[^\]]+)\]|（(?<value>[^）]+)）|\((?<value>[^\)]+)\)|《(?<value>[^》]+)》|『(?<value>[^』]+)』|\{(?<value>[^}]+)\})");
            if (!frame.Success)
                break;
            leadingFrames.Add(frame.Groups["value"].Value);
            frameOffset += frame.Length;
            Match separator = Regex.Match(framedPayload[frameOffset..], @"^[：:，,、+\-\s]*");
            frameOffset += separator.Length;
        }
        if (leadingFrames.Count > 0)
        {
            string[] distinct = leadingFrames.Distinct(StringComparer.Ordinal).ToArray();
            return distinct.Length == 1 ? distinct[0] : ConflictMarker;
        }

        string unframed = Regex.Split(payload, @"发|發")[0].Trim();
        return unframed;
    }

    // 新澳高手「亚太地区 / 战澳团队」两张系列卡：每张卡自己的字段名不同，但写法一样——
    //   亚太：297期：【3124】 / 【04567238尾】 / 【兔羊狗龙牛鼠猪鸡虎猴】 / 【火木金土】 / 【绿波蓝波】
    //   战澳：297期团队四头：4210 / 团队八尾:18463950 / 团队十肖：… / 团队四行:金木火土
    // 只认本期行里自己字段名之后、括号内（或无括号时紧随的字符）那一段；
    // 个数、重复、范围不对一律判缺失，绝不从别处凑。
    private static string ExtractAsiaPacificPayload(string block, OcrRule rule)
    {
        string field = rule.Id switch
        {
            "亚太一头" => @"亚太地区四头",
            "亚太尾" => @"亚太地区八尾",
            "亚太两肖" => @"亚太地区十肖",
            "亚太五行" => @"亚太地区四行",
            "战澳头" => @"团队四头",
            "战澳尾" => @"团队八尾",
            "战澳两肖" => @"团队十肖",
            "战澳五行" => @"团队四行",
            _ => string.Empty
        };
        if (field.Length == 0)
            return string.Empty;
        int at = block.IndexOf(field, StringComparison.Ordinal);
        if (at < 0)
            return string.Empty;
        string payload = block[(at + field.Length)..];
        // 有括号就只认括号内；没有括号（战澳那几张）取紧随的连续字符，遇别的字就停。
        Match framed = Regex.Match(payload, @"[【\[〖『（(]\s*(?<value>[^】\]〗』）)]+)\s*[】\]〗』）)]");
        if (framed.Success)
            return framed.Groups["value"].Value.Trim();
        Match bare = Regex.Match(payload,
            $@"^[：:，,\s]*(?<value>(?:[{Zodiac}]|[0-9]|[金木水火土]|尾|波|单|双)+)");
        return bare.Success ? bare.Groups["value"].Value.Trim() : string.Empty;
    }

    private static string ExtractHuangdaxianPayload(string block, OcrRule rule)
    {
        string pattern = rule.Id switch
        {
            "68小陈" => @"小陈.*?杀一段",
            "68赵高" => @"赵高.*?杀一头",
            "68宝爷" => @"宝爷.*?杀一肖",
            "68兴旺" => @"兴旺.*?杀一行",
            "68老大" => @"九肖中特",
            "68旺仔" => @"旺仔.*?杀一尾",
            "68凯哥" => @"凯哥.*?杀五码",
            "68波波" => @"波波.*?杀半波",
            "香奈风清扬" => @"四头出特",
            "香奈微风细雨" => @"(?:⑨|9)肖中特",
            "香奈老大" => @"四行中特",
            "香奈肖肖" => @"禁肖",
            "香奈尾" => @"禁尾",
            "战狼八戒" => @"绝杀二肖",
            "战狼小馒头" => @"杀",
            "战狼天空" => @"九肖",
            "战狼老王" => @"老王.*?杀半头",
            "红人关公肖" => @"关公.*?杀一肖",
            "红人关公尾" => @"关公.*?杀一尾",
            "红人极点肖" => @"极点.*?杀一肖",
            "战狼九点" or "战狼蔷薇" => "^",
            _ => "(?!)"
        };
        Match marker = Regex.Match(block, pattern, RegexOptions.Singleline);
        if (!marker.Success)
            return string.Empty;
        string payload = block[(marker.Index + marker.Length)..];
        if (rule.Id == "68宝爷")
        {
            // This card prints 来 before the opening result. Require a complete
            // answer before it; 来 inside a broken answer must not truncate it.
            Match answer = Regex.Match(payload, $@"\A(?<answer>【[{Zodiac}]】|[{Zodiac}])来");
            if (answer.Success)
                return answer.Groups["answer"].Value;
        }
        // 68 团队的三条缺头卡：卡面固定「赵高杀一头【0134】」，方括号里四个头，
        // 缺一个就是答案。三条卡用的框不一样（【】/『』/🍀…🍀），统一认这几种包围符；
        // 框外的开奖号码、期号数字一律不参与。
        if (rule.Id is "68赵高" or "香奈风清扬" or "战狼蔷薇")
        {
            Match frame = Regex.Match(payload,
                @"[【\[『《（(](?<heads>[0-4\s,，、\-－]{3,20})[】\]』》）)]");
            string? heads = frame.Success
                ? new string(frame.Groups["heads"].Value.Where(character => character is >= '0' and <= '4').ToArray())
                // 战狼蔷薇的卡用「🍀0 1 2 3🍀」这种表情包包围，不是文字括号：
                // 此时只认紧跟在标签后的四个头字符，遇到别的文字就停。
                : rule.Id == "战狼蔷薇"
                    ? new string(payload
                        .SkipWhile(character => character is not (>= '0' and <= '4'))
                        .TakeWhile(character => character is >= '0' and <= '4'
                            || character == ' ' || character == '\u3000' || character == '，' || character == ',')
                        .Where(character => character is >= '0' and <= '4').ToArray())
                    : null;
            if (heads is null)
                return string.Empty;
            return heads.Length == 4 && heads.Distinct().Count() == 4 ? heads : string.Empty;
        }
        // 香奈老大：卡面「四行中特【金 木 火 土】」，方括号里四个五行，缺一个。
        if (rule.Id == "香奈老大")
        {
            Match frame = Regex.Match(payload, @"[【\[](?<elements>[金木水火土\s,，、]{4,15})[】\]]");
            if (!frame.Success)
                return string.Empty;
            string elements = new(frame.Groups["elements"].Value.Where("金木水火土".Contains).ToArray());
            return elements.Length == 4 && elements.Distinct().Count() == 4 ? elements : string.Empty;
        }
        // 战狼老王：卡面「老王杀半头【4头单】」，只认这一个半头。
        if (rule.Id == "战狼老王")
        {
            Match halfHead = Regex.Match(payload, @"(?<!\d)(?<head>[0-4])\s*头\s*(?<parity>[单双])");
            return halfHead.Success
                ? $"{halfHead.Groups["head"].Value}头{halfHead.Groups["parity"].Value}"
                : string.Empty;
        }
        if (rule.Id == "68凯哥")
            payload = Regex.Split(payload, @"来")[0];
        if (rule.Id == "香奈肖肖")
            payload = Regex.Split(payload, @"禁尾|特")[0];
        else if (rule.Id == "香奈尾")
            payload = Regex.Split(payload, @"特")[0];
        else if (rule.Id == "战狼蔷薇")
            payload = Regex.Split(payload, @"特")[0];
        return payload.Trim();
    }

    // 标题模板一张都没匹配上：图片可能就在目录里，只是版式对不上，不能报成「未找到对应图片」。
    public const string TemplateUnmatchedReason = "模板未命中";

    public static string DescribeMissing(bool foundImage, bool recognizedText) =>
        !foundImage ? "未找到对应图片"
        : !recognizedText ? "图片文字识别失败"
        : "未识别到当期目标数据";

    public static string[] FormatOutput(
        IEnumerable<OcrRule> rules,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, string>? missingReasons = null) =>
        rules.Select(rule => ResultValues.IsConflict(values, rule.Id)
            ? $"缺失（同一期结果冲突，待核对） {rule.OutputLabel}"
            : values.TryGetValue(rule.Id, out string? value)
            ? $"{FormatForOutput(rule, value)} {rule.OutputLabel}"
            : missingReasons is not null && missingReasons.TryGetValue(rule.Id, out string? reason)
                ? $"缺失（{reason}） {rule.OutputLabel}"
                : $"缺失 {rule.OutputLabel}").ToArray();

    public static bool IsCanonicalValueValid(OcrRule rule, string value)
    {
        value = SimplifyOcrText(value.Trim());
        if (rule.Type == "五行")
        {
            return value.Length == 1 && "金木水火土".Contains(value[0])
                || value.Length == 4 && value.All("金木水火土".Contains)
                    && value.Distinct().Count() == 4;
        }
        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal))
            return IsFormattedOutputValueValid(rule, Regex.Replace(value, @"\s+", ","));
        if (rule.Type is "尾数组合" or "头数组合")
            return IsFormattedOutputValueValid(rule, value.Replace('+', ' '));
        return IsFormattedOutputValueValid(rule, value);
    }

    public static bool IsFormattedOutputValueValid(OcrRule rule, string value)
    {
        value = SimplifyOcrText(value.Trim());
        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(rule.Type.AsSpan("号码:".Length), out int count))
        {
            string[] numbers = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (rule.DedupeNumbers)
                return numbers.Length == count
                    && numbers.Distinct(StringComparer.Ordinal).Count() == numbers.Length
                    && numbers.All(number => number.Length == 2 && int.TryParse(number, out int parsed)
                        && parsed is >= 1 and <= 49);
            return numbers.Length == count && numbers.Distinct(StringComparer.Ordinal).Count() == count
                && numbers.All(number => number.Length == 2 && int.TryParse(number, out int parsed)
                    && parsed is >= 1 and <= 49);
        }
        if (rule.Type is "生肖" or "单生肖")
            return value.Length == 1 && Zodiac.Contains(value[0]);
        if (rule.Type == "生肖组合")
            return value.Length == 2 && value.All(Zodiac.Contains) && value.Distinct().Count() == 2;
        if (rule.Type == "缺两肖")
            return value.Length == 2 && value.All(Zodiac.Contains) && value.Distinct().Count() == 2;
        if (rule.Type is "九肖" or "四季" or "方位" or "琴棋书画")
            return value.Length == 9 && value.All(Zodiac.Contains) && value.Distinct().Count() == 9;
        // 方位型资料（藏宝九肖）、四季型资料（白小姐四季）和琴棋书画型资料（会员琴棋）落库时
        // 都已按属性表还原成九个生肖，校验与九肖一致；印在卡上的属性字不再落库。
        if (rule.Type == "统计生肖")
            return value.Length > 0 && value.Length <= Zodiac.Length && value.All(Zodiac.Contains)
                && value.Distinct().Count() == value.Length;
        if (rule.Type == "头")
            return Regex.IsMatch(value, "^[0-4]头$");
        if (rule.Type == "尾")
        {
            if (rule.Id is "亚太尾" or "战澳尾")
            {
                string[] tails = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return tails.Length == 2 && tails.Distinct(StringComparer.Ordinal).Count() == 2
                    && tails.All(item => Regex.IsMatch(item, "^[0-9]尾$"));
            }
            return Regex.IsMatch(value, "^[0-9]尾$");
        }
        if (rule.Type == "尾数组合")
        {
            string[] tails = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return tails.Length == 2 && tails.Distinct(StringComparer.Ordinal).Count() == 2
                && tails.All(item => Regex.IsMatch(item, "^[0-9]尾$"));
        }
        if (rule.Type == "头数组合")
        {
            string[] heads = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return heads.Length > 0 && heads.Length <= 5 && heads.Distinct(StringComparer.Ordinal).Count() == heads.Length
                && heads.All(item => Regex.IsMatch(item, "^[0-4]头$"));
        }
        if (rule.Type == "缺尾") return Regex.IsMatch(value, "^[0-9]尾$");
        if (rule.Type == "缺头") return Regex.IsMatch(value, "^[0-4]头$");
        if (rule.Type is "五行" or "单五行" or "缺五行") return Regex.IsMatch(value, "^[金木水火土]行?$");
        if (rule.Type is "色单双" or "半波" or "缺半波") return Regex.IsMatch(value, "^[红蓝绿][单双]$");
        if (rule.Type == "合") return Regex.IsMatch(value, "^(?:0[1-9]|1[0-3])合$");
        if (rule.Type == "段") return Regex.IsMatch(value, "^[0-7]段$");
        if (rule.Type == "半头") return Regex.IsMatch(value, "^[0-4]头[单双]$");
        return false;
    }

    private static string FormatForOutput(OcrRule rule, string value)
    {
        if (rule.Type.StartsWith("号码:", StringComparison.Ordinal))
            return Regex.Replace(value.Trim(), @"\s+", ",");
        if (rule.Type is "尾数组合" or "头数组合")
            return value.Replace('+', ' ');
        if (rule.Type == "五行")
        {
            if (value.Length == 1 && "金木水火土".Contains(value[0]))
                return value + "行";
            string missing = string.Concat("金木水火土".Where(element => !value.Contains(element)));
            return missing.Length == 1 ? missing + "行" : value;
        }
        if (rule.Type == "单五行")
            return value.EndsWith('行') ? value : value + "行";
        return value;
    }

    private static string? ExtractTypedForRule(string tail, OcrRule rule)
    {
        if (rule.StopAtPlus)
        {
            Match plus = Regex.Match(tail, @"[+＋]");
            if (plus.Success)
                tail = tail[..plus.Index];
        }
        // A ten-zodiac combo card lists 10 of the 12 zodiacs; the answer is the
        // pair that is missing.
        if (rule.TenZodiacCombo && rule.Type == "生肖组合")
        {
            string tenText = BeforeOpeningResult(SimplifyOcrText(tail));
            string tenZodiacs = string.Concat(Regex.Matches(tenText, $"[{Zodiac}]")
                .Select(match => match.Value).Distinct());
            return tenZodiacs.Length == 10
                ? string.Concat(Zodiac.Where(zodiac => !tenZodiacs.Contains(zodiac)))
                : null;
        }
        if (rule.Id == "翩翩公子肖" && rule.Type == "生肖")
        {
            string beforeOpening = BeforeOpeningResult(SimplifyOcrText(tail));
            MatchCollection matches = Regex.Matches(beforeOpening, $"[{Zodiac}]");
            string[] distinct = matches.Select(match => match.Value).Distinct(StringComparer.Ordinal).ToArray();
            return distinct.Length > 1 ? ConflictMarker
                : matches.Count > 0 && distinct.Length == 1 ? distinct[0] : null;
        }
        if (HasConflictingSingleValues(tail, rule.Type))
            return rule.SkipConflictingRows ? null : ConflictMarker;
        return ExtractTyped(tail, rule.Type, rule);
    }

    private static bool MatchesFamilyIdentity(string tail, OcrRule? rule, params string[] phrases) =>
        phrases.Any(phrase => tail.Contains(phrase, StringComparison.Ordinal)
            || rule is not null
            && (Normalize(rule.Keyword).Equals(Normalize(phrase), StringComparison.Ordinal)
                || Normalize(rule.RequiredKeyword ?? string.Empty).Equals(Normalize(phrase), StringComparison.Ordinal)));

    private static readonly char[] FamilyStatusMarks =
        ['√', '✓', '×', 'x', 'X', '?', '？', '准', '準', '对', '對', '错', '錯', '!', '！', '.', '。'];

    // Payload for the 四头/八尾/四行/十肖 cards: either the text after the
    // field marker, or the last bracketed payload when the title sits on its
    // own row and the value row carries only 【...】 + a status mark.
    private static string? FamilyPayload(string text, params string[] markers)
    {
        string? raw = ExactSemanticField(text, markers);
        if (raw is null)
        {
            MatchCollection brackets = Regex.Matches(text, @"[【\[](?<payload>[^】\]]+)[】\]]");
            if (brackets.Count > 0)
                raw = brackets[^1].Groups["payload"].Value;
        }
        if (raw is null)
            return null;
        raw = raw.Trim();
        while (true)
        {
            string next = raw.TrimEnd(FamilyStatusMarks).TrimEnd('尾');
            if (next.Length == raw.Length)
                break;
            raw = next;
        }
        return raw.Length == 0 ? null : raw;
    }

    private static string? ExactSemanticField(string text, params string[] markers)
    {
        string simplified = SimplifyOcrText(text);
        int markerIndex = -1;
        int markerLength = 0;
        foreach (string markerText in markers.Select(SimplifyOcrText))
        {
            int found = simplified.IndexOf(markerText, StringComparison.Ordinal);
            if (found >= 0 && (markerIndex < 0 || found < markerIndex))
            {
                markerIndex = found;
                markerLength = markerText.Length;
            }
        }
        if (markerIndex < 0)
            return null;
        string raw = simplified[(markerIndex + markerLength)..].Trim();
        raw = raw.TrimStart(':', '：').Trim();
        return raw.Length == 0 ? null : raw;
    }

    private static bool HasConflictingSingleValues(string tail, string type)
    {
        string text = BeforeOpeningResult(SimplifyOcrText(tail));
        IEnumerable<string> values = type switch
        {
            "合" => Regex.Matches(text, @"(?<!\d)(?:0?[1-9]|1[0-3])\s*合")
                .Select(match => $"{int.Parse(Regex.Replace(match.Value[..^1], @"\s+", "")):00}合"),
            "段" => Regex.Matches(text, @"(?<!\d)[0-7]\s*段")
                .Select(match => Regex.Replace(match.Value, @"\s+", "")),
            "尾" => TailValueCandidates(text),
            "尾数组合" => TailPairCandidates(text),
            "头" => HeadValueCandidates(text),
            "单五行" => Regex.Matches(text, @"[金木水火土]").Select(match => match.Value),
            "半头" => Regex.Matches(text, @"(?<!\d)[0-4]\s*头\s*[单双]")
                .Select(match => Regex.Replace(match.Value, @"\s+", "")),
            "色单双" or "半波" => Regex.Matches(text, @"[红蓝绿](?:波)?[单双]")
                .Select(match => match.Value.Replace("波", "", StringComparison.Ordinal)),
            "生肖" or "单生肖" => Regex.Matches(text, $"[{Zodiac}]").Select(match => match.Value),
            _ => Array.Empty<string>()
        };
        return values.Distinct(StringComparer.Ordinal).Take(2).Count() > 1;
    }

    private static IEnumerable<string> HeadValueCandidates(string text)
    {
        var values = new List<string>();
        int delimiter = Math.Max(text.LastIndexOf('：'), text.LastIndexOf(':'));
        if (delimiter >= 0)
        {
            string field = text[(delimiter + 1)..];
            values.AddRange(Regex.Matches(field,
                    @"(?<!\d)(?<head>[0-4零一二三四])\s*头")
                .Where(match => !Regex.IsMatch(
                    field[..match.Index], @"(?:杀|殺|禁|禁止)\s*$"))
                .Select(match => $"{ToArabicDigit(match.Groups["head"].Value[0])}头"));
        }
        values.AddRange(Regex.Matches(text, @"[【\[](?<head>[0-4])\s*[】\]]\s*头")
            .Select(match => $"{match.Groups["head"].Value}头"));
        // Brackets may wrap the value itself (【1头】【2头】 or 【1】【2】)
        // and ordinary OCR may omit the colon. Keep every candidate in the
        // field so the caller can report a conflict instead of taking the first.
        values.AddRange(Regex.Matches(text, @"[【\[](?<head>[0-4])\s*头\s*[】\]]")
            .Select(match => $"{match.Groups["head"].Value}头"));
        values.AddRange(Regex.Matches(text, @"(?<!杀|殺|禁)[\s【\[](?<head>[0-4])\s*[】\]](?=\s*(?:头)?(?:\s|$|[【\[]))")
            .Select(match => $"{match.Groups["head"].Value}头"));
        Match marker = Regex.Match(text, @"(?:杀|殺)\s*[一二三四五六七八九十0-9]*\s*头");
        if (marker.Success)
        {
            string field = text[(marker.Index + marker.Length)..];
            values.AddRange(Regex.Matches(field, @"(?<!\d)(?<head>[0-4])\s*头")
                .Select(match => $"{match.Groups["head"].Value}头"));
            values.AddRange(Regex.Matches(field, @"[【\[](?<head>[0-4])\s*[】\]]")
                .Select(match => $"{match.Groups["head"].Value}头"));
        }
        values.AddRange(Regex.Matches(text, @"买\s*(?<head>[0-4零一二三四])\s*头")
            .Select(match => $"{ToArabicDigit(match.Groups["head"].Value[0])}头"));
        return values;
    }

    private static IEnumerable<string> TailValueCandidates(string text)
    {
        var values = Regex.Matches(text, @"(?<!\d)[0-9]\s*尾")
            .Select(match => Regex.Replace(match.Value, @"\s+", ""))
            .ToList();
        int marker = text.LastIndexOf('尾');
        if (marker < 0)
            return values;
        // 尾标记之后的无尾数字只有在值位上才算值：后面必须是结尾、另一个数字
        // 值或状态/结构词。否则“最后修改:1分钟前[日志]”这类页脚数字会被误当
        // 第二个尾值。
        const string valueSuffixes = "尾开開期准準中错錯√×○✗0123456789";
        foreach (Match run in Regex.Matches(text[(marker + 1)..], @"(?<!\d)[0-9]+(?!\d)"))
        {
            if (run.Value.Distinct().Count() != 1)
                continue;
            string after = text[(marker + 1 + run.Index + run.Length)..].TrimStart(' ', '，', ',', '。', '、', ':');
            if (after.Length > 0 && !valueSuffixes.Contains(after[0]))
                continue;
            values.Add($"{run.Value[0]}尾");
        }
        return values;
    }

    private static IEnumerable<string> TailPairCandidates(string text)
    {
        var values = new List<string>();
        foreach (Match pair in Regex.Matches(text,
            @"(?<!\d)(?<first>[0-9])\s*尾?\s*(?:\+|＋|、|,|，|\.|。|-|－)\s*(?<second>[0-9])\s*尾?"))
        {
            char[] digits = [pair.Groups["first"].Value[0], pair.Groups["second"].Value[0]];
            Array.Sort(digits);
            values.Add(new string(digits));
        }
        foreach (Match compact in Regex.Matches(text, @"(?<!\d)(?<digits>[0-9]{2})\s*尾"))
        {
            char[] digits = compact.Groups["digits"].Value.ToCharArray();
            Array.Sort(digits);
            values.Add(new string(digits));
        }
        return values;
    }

    // 九肖卡“期号+开奖表头在同一行、九个生肖在下一行”时，开奖截断会把生肖表切掉。
    // 这里只从“九肖”标记之后取，遇到下一个档位词或期号就停：同图的六肖、三肖、一肖
    // 绝不能参与凑数。开奖标记先作为第一道边界（避免把开奖生肖算进来），切完不足
    // 9 个时再放开开奖边界重试一次——表头行尾的“开:?”会在生肖表之前，属于同一张卡。
    private static string? ExtractBoundedNineZodiacs(string tail)
    {
        int marker = tail.LastIndexOf("九肖", StringComparison.Ordinal);
        if (marker < 0)
            return null;
        string body = tail[(marker + "九肖".Length)..];
        string? strict = TakeNineZodiacs(body, includeOpeningStop: true);
        return strict ?? TakeNineZodiacs(body, includeOpeningStop: false);
    }

    private static string? TakeNineZodiacs(string body, bool includeOpeningStop)
    {
        int cut = body.Length;
        var stops = new List<string>
        {
            "期", "上期", "下期", "六肖", "三肖", "二肖", "一肖", "四肖", "五肖",
            "七肖", "八肖", "十肖", "十二肖"
        };
        if (includeOpeningStop)
            stops.AddRange(["开", "開"]);
        foreach (string stop in stops)
        {
            int at = body.IndexOf(stop, StringComparison.Ordinal);
            if (at >= 0 && at < cut)
                cut = at;
        }
        string segment = body[..cut];
        string[] zodiacs = Regex.Matches(segment, $"[{Zodiac}]")
            .Select(match => match.Value).ToArray();
        if (zodiacs.Length != 9)
            return null;
        string value = string.Concat(zodiacs);
        return value.Distinct().Count() == 9 ? value : null;
    }

    private static string? ExtractTyped(string tail, string type, OcrRule? rule = null)
    {
        tail = SimplifyOcrText(tail);
        string beforeOpening = BeforeOpeningResult(tail);
        string withoutIssueBeforeOpening = RemoveIssue(beforeOpening);
        if (type == "尾" && MatchesFamilyIdentity(tail, rule, "亚太地区八尾", "团队八尾"))
        {
            string? raw = FamilyPayload(withoutIssueBeforeOpening, "亚太地区八尾", "团队八尾");
            if (raw is null || !Regex.IsMatch(raw, @"^[0-9](?:\s*[0-9]){7}$"))
                return null;
            string digits = string.Concat(raw.Where(char.IsDigit));
            if (digits.Distinct().Count() != 8)
                return null;
            int[] present = digits.Select(c => c - '0').ToArray();
            int[] missing = Enumerable.Range(0, 10).Where(n => !present.Contains(n)).ToArray();
            return missing.Length == 2
                ? string.Join(' ', missing.Select(n => $"{n}尾"))
                : null;
        }
        if (type == "五行" && MatchesFamilyIdentity(tail, rule, "亚太地区四行", "团队四行"))
        {
            string? raw = FamilyPayload(withoutIssueBeforeOpening, "亚太地区四行", "团队四行");
            if (raw is null || !Regex.IsMatch(raw, @"^[金木水火土](?:\s*[金木水火土]){3}$"))
                return null;
            string elements = string.Concat(raw.Where("金木水火土".Contains));
            return elements.Distinct().Count() == 4
                ? string.Concat("金木水火土".Where(e => !elements.Contains(e)))
                : null;
        }
        if (type == "生肖组合" && MatchesFamilyIdentity(tail, rule, "亚太地区十肖", "团队十肖"))
        {
            string? raw = FamilyPayload(withoutIssueBeforeOpening, "亚太地区十肖", "团队十肖");
            if (raw is null || !Regex.IsMatch(raw, $@"^[{Zodiac}](?:\s*[{Zodiac}]){{9}}$"))
                return null;
            string zodiacs = string.Concat(raw.Where(Zodiac.Contains));
            return zodiacs.Distinct().Count() == 10
                ? string.Concat(Zodiac.Where(z => !zodiacs.Contains(z)))
                : null;
        }
        if (type == "头" && MatchesFamilyIdentity(tail, rule, "亚太地区四头", "团队四头"))
        {
            string? raw = FamilyPayload(withoutIssueBeforeOpening, "亚太地区四头", "团队四头");
            if (raw is null || !Regex.IsMatch(raw, @"^[0-4](?:\s*[0-4]){3}$"))
                return null;
            string heads = string.Concat(raw.Where(c => c is >= '0' and <= '4'));
            return heads.Distinct().Count() == 4
                ? $"{Enumerable.Range(0, 5).Single(n => !heads.Contains((char)('0' + n)))}头"
                : null;
        }
        if (type.StartsWith("号码:", StringComparison.Ordinal)
            && int.TryParse(type.AsSpan("号码:".Length), out int numberCount))
            return ExtractNumbers(beforeOpening, numberCount);

        if (type == "缺尾")
        {
            string withoutIssue = RemoveIssue(beforeOpening);
            int marker = withoutIssue.LastIndexOf("公子送尾数", StringComparison.Ordinal);
            string tailText = marker >= 0
                ? withoutIssue[(marker + "公子送尾数".Length)..]
                : withoutIssue;
            int[] rawTails = Regex.Matches(tailText, @"(?<!\d)[0-9]+(?!\d)")
                .SelectMany(match => marker >= 0 || match.Value.Length == 1 || match.Value.Length == 9
                    ? match.Value.Select(character => character - '0')
                    : [])
                .ToArray();
            int[] tails = rawTails.Distinct().ToArray();
            if (rawTails.Length != 9 || tails.Length != 9)
                return null;
            int missing = Enumerable.Range(0, 10).Single(number => !tails.Contains(number));
            return $"{missing}尾";
        }

        if (type == "单生肖")
        {
            MatchCollection matches = Regex.Matches(beforeOpening, $"[{Zodiac}]");
            return matches.Count == 1 ? matches[0].Value : null;
        }

        if (type == "缺肖")
        {
            string zodiacs = string.Concat(Regex.Matches(beforeOpening, $"[{Zodiac}]")
                .Select(match => match.Value).Distinct());
            return zodiacs.Length == 11
                ? string.Concat(Zodiac.Where(zodiac => !zodiacs.Contains(zodiac)))
                : null;
        }

        if (type is "生肖组合" or "九肖")
        {
            // 高山流水 prints the 9-zodiac tier first; a merged OCR row (cloud
            // retry) concatenates the later tiers, so take exactly the nine
            // zodiacs that follow the first 精选肖 label and stop at the next
            // label or issue marker.
            if (type == "九肖" && rule?.Id == "高山流水")
            {
                foreach (string marker in new[] { "精选⑨肖", "精选肖" })
                {
                    int label = tail.IndexOf(marker, StringComparison.Ordinal);
                    if (label < 0)
                        continue;
                    string segment = tail[(label + marker.Length)..];
                    int labelCut = segment.IndexOf("精选", StringComparison.Ordinal);
                    if (labelCut >= 0)
                        segment = segment[..labelCut];
                    int issueCut = segment.IndexOf('期');
                    if (issueCut >= 0)
                        segment = segment[..issueCut];
                    string[] zodiacs = Regex.Matches(segment, $"[{Zodiac}]")
                        .Select(match => match.Value).ToArray();
                    string nine = string.Concat(zodiacs);
                    if (zodiacs.Length == 9 && nine.Distinct().Count() == 9)
                        return nine;
                    break;
                }
            }
            int nineMarker = beforeOpening.LastIndexOf("解九肖", StringComparison.Ordinal);
            string zodiacText = type == "九肖" && nineMarker >= 0
                ? beforeOpening[(nineMarker + "解九肖".Length)..]
                : beforeOpening;
            MatchCollection matches = Regex.Matches(zodiacText, $"[{Zodiac}]");
            int expected = type == "九肖" ? 9 : 2;
            if (matches.Count != expected)
            {
                // Some 九肖 cards print the issue + result header on one row and
                // the nine zodiacs on the next row, so the opening cut removes
                // the list. Retry on the text after the 九肖 label in the raw
                // candidate, still requiring exactly nine distinct zodiacs.
                // 回退必须限定在“九肖”标记之后、下一个档位/期号/开奖之前：直接用整段
                // tail 会把同图的六肖、三肖凑进来，凑够 9 个就成了假成功。
                if (type == "九肖")
                {
                    string? widerValue = ExtractBoundedNineZodiacs(tail);
                    if (widerValue is not null)
                        return widerValue;
                }
                return null;
            }
            string value = string.Concat(matches.Select(match => match.Value));
            return value.Distinct().Count() == expected ? value : null;
        }

        if (type == "单五行")
        {
            Match element = Regex.Match(beforeOpening, @"[【\[]?(?<element>[金木水火土])[】\]]?");
            return element.Success ? element.Groups["element"].Value : null;
        }

        if (type == "五行")
        {
            // 嫣然心水「小黄人五行」：卡面下方一行「297期资料网 澳门【土木火水】开:?00准」，
            // 括号里四个五行，缺的那个就是答案（输出带「行」）。
            // 上方那张「金行/木行/…」号码对照表不是本期值，不能参与。
            if (rule?.Id == "小黄人五行")
            {
                Match framed = Regex.Match(beforeOpening, @"[【\[](?<elements>[金木水火土\s]{4,12})[】\]]");
                if (!framed.Success)
                    return null;
                string picked = new(framed.Groups["elements"].Value.Where("金木水火土".Contains).ToArray());
                return picked.Length == 4 && picked.Distinct().Count() == 4
                    ? Enumerable.Range(0, 5).Select(n => "金木水火土"[n]).Single(e => !picked.Contains(e)) + "行"
                    : null;
            }
            string[] elements = Regex.Matches(beforeOpening, "[金木水火土]")
                .Select(match => match.Value)
                .ToArray();
            return elements.Length == 4 && elements.Distinct(StringComparer.Ordinal).Count() == 4
                ? string.Concat(elements)
                : null;
        }

        if (type == "尾数组合")
        {
            Match combination = Regex.Match(beforeOpening, @"(?<!\d)(?<first>[0-9])\s*尾?\s*(?:\+|＋|、|,|，|\.|。|-|－)\s*(?<second>[0-9])\s*尾?");
            if (!combination.Success)
                combination = Regex.Match(beforeOpening, @"[【\[](?<first>[0-9])\s*(?<second>[0-9])\s*[】\]]?尾");
            if (combination.Success)
            {
                string first = combination.Groups["first"].Value;
                string second = combination.Groups["second"].Value;
                return first != second ? $"{first}尾+{second}尾" : null;
            }

            // Formula sheets often compact two tails as a two-digit value (e.g. “杀15尾”).
            Match compact = Regex.Match(beforeOpening, @"(?<!\d)(?<digits>[0-9]{2})\s*尾");
            return compact.Success && compact.Groups["digits"].Value[0] != compact.Groups["digits"].Value[1]
                ? $"{compact.Groups["digits"].Value[0]}尾+{compact.Groups["digits"].Value[1]}尾"
                : null;
        }

        if (type is "头数组合" or "缺头")
        {
            string withoutIssue = RemoveIssue(beforeOpening);
            // 注意：这张卡的★标的是「开奖结果落在哪个头」，不是答案。
            // 例：296期「1头,2头,★3头,4头」开的是 3头，而答案（缺的头）是 0头。
            // 所以★一律不参与取值，只按四头反推缺的那个。
            int marker = withoutIssue.LastIndexOf("四头出特", StringComparison.Ordinal);
            string valueText = marker >= 0 ? withoutIssue[(marker + "四头出特".Length)..] : withoutIssue;
            char[] rawHeads = valueText
                .Where(character => character is >= '0' and <= '4')
                .ToArray();
            char[] heads = rawHeads.Distinct().ToArray();
            if (heads.Length != 4 || type == "缺头" && rawHeads.Length != 4)
                return null;
            if (type == "缺头")
            {
                int missing = Enumerable.Range(0, 5).Single(number => !heads.Contains((char)('0' + number)));
                return $"{missing}头";
            }
            return string.Join('+', heads.Select(head => $"{head}头"));
        }

        if (type == "半头")
        {
            Match halfHead = Regex.Match(beforeOpening, @"(?<!\d)(?<head>[0-4])\s*头\s*(?<parity>[单双])");
            return halfHead.Success
                ? $"{halfHead.Groups["head"].Value}头{halfHead.Groups["parity"].Value}"
                : null;
        }

        if (type is "色单双" or "半波")
        {
            Match colorParity = Regex.Match(beforeOpening, "(?<color>[红蓝绿])(?:波)?(?<parity>[单双])");
            return colorParity.Success
                ? colorParity.Groups["color"].Value + colorParity.Groups["parity"].Value
                : null;
        }

        if (type == "段")
        {
            Match bracketedSegment = Regex.Match(beforeOpening, @"[【\[](?<segment>[0-7])\s*段[】\]]");
            if (bracketedSegment.Success)
                return $"{bracketedSegment.Groups["segment"].Value}段";
        }

        if (type == "尾")
        {
            Match bracketedTail = Regex.Match(beforeOpening, @"[【\[](?<tail>[0-9])\s*尾[】\]]");
            if (bracketedTail.Success)
                return $"{bracketedTail.Groups["tail"].Value}尾";
        }

        if (type == "头")
        {
            int delimiter = Math.Max(beforeOpening.LastIndexOf('：'), beforeOpening.LastIndexOf(':'));
            if (delimiter >= 0)
            {
                MatchCollection valueHeads = Regex.Matches(
                    beforeOpening[(delimiter + 1)..],
                    @"(?<!\d)(?<head>[0-4零一二三四])\s*头");
                if (valueHeads.Count > 0)
                {
                    Match valueHead = valueHeads[^1];
                    return $"{ToArabicDigit(valueHead.Groups["head"].Value[0])}头";
                }
            }
        }

        if (type == "生肖")
            return ExtractSingleZodiac(beforeOpening);

        Match match = type switch
        {
            "生肖" => Match.Empty,
            "合" => Regex.Match(beforeOpening, @"(?<!\d)(0?[1-9]|1[0-3])\s*合"),
            "段" => Regex.Match(beforeOpening, @"(?<!\d)[0-7]段"),
            "尾" => Regex.Match(beforeOpening, @"(?<!\d)[0-9]尾"),
            "头" => Regex.Match(beforeOpening, @"(?<!\d)[0-4]头"),
            "五行" => Regex.Match(beforeOpening, @"[金木水火土]{2,5}"),
            _ => Match.Empty
        };

        if (!match.Success && type == "尾")
        {
            int marker = beforeOpening.LastIndexOf('尾');
            if (marker >= 0)
            {
                match = Regex.Match(beforeOpening[(marker + 1)..], @"(?<!\d)[0-9](?!\d)");
                if (!match.Success)
                    match = Regex.Match(beforeOpening[(marker + 1)..], @"(?<!\d)(?<tail>[0-9])\k<tail>+(?!\d)");
            }
        }

        if (!match.Success && type == "头")
        {
            Match bracketedHead = Regex.Match(beforeOpening, @"[【\[](?<head>[0-4])\s*[】\]]\s*头");
            if (bracketedHead.Success)
                return $"{bracketedHead.Groups["head"].Value}头";
            Match chineseHead = Regex.Match(beforeOpening, @"买\s*(?<head>[零一二三四])头");
            if (chineseHead.Success)
                return $"{ToArabicDigit(chineseHead.Groups["head"].Value[0])}头";
            // 曾经在这里兜底「最后一个『头』字之后任意一个 0-4 单数字」，会把
            // 「4行必中」这类说明文字的数字当成本期头。只认 X头 / 【X】头 / 买X头，
            // 三种结构都没有就判缺失。
        }

        if (!match.Success)
            return null;
        if (type == "合")
            return $"{int.Parse(match.Groups[1].Value):00}合";
        if (type == "尾" && match.Groups["tail"].Success)
            return $"{match.Groups["tail"].Value}尾";
        if (type == "尾" && match.Value.Length == 1)
            return $"{match.Value}尾";
        if (type == "头" && match.Value.Length == 1)
            return $"{ToArabicDigit(match.Value[0])}头";
        return match.Value;
    }

    private static string? ExtractNumbersAroundIssue(
        string[] lines, int scopeStart, int issue, int expectedCount, OcrRule rule)
    {
        if (SplitIssueNumberRuleIds.Contains(rule.Id))
        {
            var splitObserved = new HashSet<string>(StringComparer.Ordinal);
            for (int index = scopeStart; index < lines.Length; index++)
            {
                if (!ContainsIssue(lines[index], issue))
                    continue;
                string? split = ExtractReviewedSplitNumberWindow(
                    lines, scopeStart, index, issue, expectedCount, rule);
                if (split is not null)
                    splitObserved.Add(split);
            }
            if (splitObserved.Count > 0)
                return splitObserved.Count == 1 ? splitObserved.Single() : ConflictMarker;
        }

        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (int index = scopeStart; index < lines.Length; index++)
        {
            if (!ContainsIssue(lines[index], issue))
                continue;

            string target = Normalize(lines[index]);
            bool followingTable = rule.Id == "蓝色" || target.Contains("特码开在", StringComparison.Ordinal);
            bool precedingTable = rule.Id == "彩图" || target.Contains("开奖结果", StringComparison.Ordinal);
            string? directional = followingTable
                ? ExtractDirectionalNumberTable(lines, index, issue, expectedCount, forward: true, rule)
                : precedingTable
                    ? ExtractDirectionalNumberTable(lines, index, issue, expectedCount, forward: false, rule)
                    : null;
            if (directional is not null)
            {
                observed.Add(directional);
                continue;
            }
            if (followingTable || precedingTable)
                continue;

            var parts = new List<string>();
            string firstText = TextAfterIssue(lines[index], issue);
            string first = ScopeNumberPayload(firstText, rule, expectedCount);
            string firstSimplified = SimplifyOcrText(firstText);
            bool explicitIssueField = Regex.IsMatch(firstSimplified,
                @"^\s*(?:(?:杀|殺)(?:\s*[:：]\s*(?:杀|殺))?|(?:绝杀|絕殺)[一二三四五六七八九十0-9]*[码碼])");
            // Field ownership established on the issue row is inherited by the
            // continuation rows. A continuation must never fall back to the
            // looser scoping that drops pre-bracket digits or odd extras.
            bool firstOwned = explicitIssueField
                || IsNumberContinuation(firstText, expectedCount, rule, issue)
                || ContainsOwnNumberIdentity(lines[index], rule);
            if (firstOwned && Regex.IsMatch(first, @"\d"))
                parts.Add(first);
            for (int next = index + 1; next < lines.Length; next++)
            {
                if (ContainsIssueBoundary(lines[next], issue))
                {
                    // A standalone compact pair row may still be the tail of a
                    // field whose issue row visibly wraps an opening/result
                    // cell, but only when the next row closes that field with a
                    // non-target issue marker. Opening text plus a matching
                    // count alone do not prove ownership, and a trailing bare
                    // number with no following boundary is unattributable.
                    if (next == index + 1
                        && IsWrappedCompactFieldTail(
                            lines, index, next, issue, expectedCount, rule, parts))
                    {
                        parts.Add(ScopeNumberPayload(lines[next], rule, expectedCount));
                        continue;
                    }
                    break;
                }
                if (SplitIssueNumberRuleIds.Contains(rule.Id) && IsOpeningOnlySeparator(lines[next]))
                    continue;
                // Row-major readings insert a region boundary between the
                // period cell and its opening cell; a 36-number围 field only
                // crosses that boundary before it has started.
                if (OcrLayoutMarkers.IsBoundary(lines[next]))
                {
                    if (parts.Count == 0 && expectedCount >= 36)
                        continue;
                    break;
                }
                // A 36-number围 card prints the period header row as
                // "253期" + "开00准" and the numbers below the opening cell.
                // Only before the field starts may that opening-only cell be
                // stepped over, and only for full 36-number tables.
                if (parts.Count == 0 && expectedCount >= 36 && IsOpeningResultOnly(lines[next]))
                    continue;
                // Split rows print the field label on its own line ("257期" /
                // "月禁" / "41.29.05"): a pure 禁 label may be stepped over
                // before the field starts so the numeric row below is read.
                if (parts.Count == 0 && IsPureForbiddenLabel(lines[next]))
                    continue;
                if (!IsNumberContinuation(lines[next], expectedCount, rule, issue))
                    break;
                parts.Add(firstOwned && !IsPureNumericBracketRow(lines[next])
                    ? ScopedContinuationPayload(lines[next], rule)
                    : ScopeNumberPayload(lines[next], rule, expectedCount));
            }

            string? value = ExtractNumbers(string.Join(' ', parts), expectedCount);
            if (value is null
                && ContainsOwnNumberIdentity(lines[index], rule)
                && HasUnclosedBracket(lines[index])
                && index + 1 < lines.Length
                && !ContainsIssueBoundary(lines[index + 1], issue)
                && !ContainsPeerIdentity(lines[index + 1], rule))
            {
                // The author banner wrapped mid-line and the payload sits on
                // the next row ("正253期正【长安之星***100新澳" + "门六合彩100***杀码】【47,19】开").
                value = ExtractNumbers(
                    ScopeNumberPayload(lines[index + 1], rule, expectedCount), expectedCount);
            }
            if (value is not null)
                observed.Add(value);
        }
        return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;
    }

    private static bool HasCurrentFieldStartAfterEarlierIssue(
        string[] lines, int scopeStart, int issueIndex, int issue, OcrRule rule, int expectedCount)
    {
        int previousIssue = -1;
        for (int index = issueIndex - 1; index >= scopeStart; index--)
        {
            if (!ContainsIssue(lines[index], issue)
                && !ContainsIssueBoundary(lines[index], issue))
                continue;
            previousIssue = index;
            break;
        }
        if (previousIssue < 0)
            return true;

        for (int index = previousIssue + 1; index < issueIndex; index++)
        {
            if (IsNumberRowStart(lines[index]) || ContainsOwnNumberIdentity(lines[index], rule))
                return true;
        }

        if (expectedCount >= 36
            && Enumerable.Range(previousIssue + 1, issueIndex - previousIssue - 1)
                .Any(index => IsOpeningOnlySeparator(lines[index])))
        {
            int immediate = issueIndex - 1;
            if (immediate > previousIssue
                && !ContainsAnyIssue(lines[immediate])
                && IsNumberContinuation(lines[immediate], expectedCount, rule))
                return true;
        }
        return false;
    }

    private static bool ContainsOwnNumberIdentity(string line, OcrRule rule)
    {
        string normalized = Normalize(line);
        return new[] { rule.Keyword, rule.Label ?? string.Empty, rule.RequiredKeyword ?? string.Empty, rule.Section ?? string.Empty }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .Any(alias => normalized.Contains(alias, StringComparison.Ordinal));
    }

    private static bool HasUnclosedBracket(string line)
    {
        int opening = line.IndexOfAny(['【', '[']);
        return opening >= 0 && line[(opening + 1)..].IndexOfAny(['】', ']']) < 0;
    }

    private static bool IsReviewedCompactTrailingCompletion(
        string[] lines,
        int index,
        int issueIndex,
        int issue,
        int expectedCount,
        OcrRule rule,
        IEnumerable<string> currentParts,
        bool sawRightPayload)
    {
        if (!SplitIssueNumberRuleIds.Contains(rule.Id)
            || index <= issueIndex + 1
            || !ContainsIssueBoundary(lines[index], issue))
            return false;

        string trimmed = SimplifyOcrText(lines[index]).Trim();
        if (!Regex.IsMatch(trimmed, @"^\d{4,6}$")
            || IsIssueLikeBareNumber(trimmed, issue))
            return false;

        string scoped = ScopeNumberPayload(lines[index], rule, expectedCount);
        if (ExtractNumbers(string.Join(' ', currentParts.Append(scoped)), expectedCount) is null)
            return false;

        // An ambiguous compact row is only data when the physical field
        // structure proves it: either the opening separator is immediately
        // before it, or it sits between an already-consumed numeric
        // continuation and the opening separator. Count alone is not proof.
        return IsOpeningOnlySeparator(lines[index - 1])
            || sawRightPayload && index + 1 < lines.Length && IsOpeningOnlySeparator(lines[index + 1]);
    }

    // Generic (non-reviewed) number rules: a compact pair row directly after
    // the issue row is only a field tail when that issue row visibly wraps an
    // opening/result cell AND the row after the compact pair proves the field
    // ended at a non-target issue marker. A bare number at the very end of the
    // input has no proven owner and stays a boundary.
    private static bool IsWrappedCompactFieldTail(
        string[] lines,
        int issueIndex,
        int compactIndex,
        int issue,
        int expectedCount,
        OcrRule rule,
        IEnumerable<string> currentParts)
    {
        string compactLine = lines[compactIndex];
        if (!IsAmbiguousCompactRow(compactLine, issue)
            || int.TryParse(SimplifyOcrText(compactLine).Trim(), out int compactValue)
                && compactValue == issue)
            return false;
        string simplified = SimplifyOcrText(lines[issueIndex]);
        if (BeforeOpeningResult(simplified).Length == simplified.Length)
            return false;
        int followingIndex = compactIndex + 1;
        if (followingIndex >= lines.Length
            || !ContainsIssueBoundary(lines[followingIndex], issue)
            || ContainsIssue(lines[followingIndex], issue))
            return false;
        string scoped = ScopeNumberPayload(compactLine, rule, expectedCount);
        return ExtractNumbers(string.Join(' ', currentParts.Append(scoped)), expectedCount) is not null;
    }

    private static bool IsAmbiguousCompactRow(string line, int selectedIssue)
    {
        string trimmed = SimplifyOcrText(line).Trim();
        return Regex.IsMatch(trimmed, @"^\d{4,6}$")
            && !IsIssueLikeBareNumber(trimmed, selectedIssue);
    }

    private static string? ExtractStrictCenteredNumberWindow(
        string[] lines, int issueIndex, int issue, int expectedCount, OcrRule rule)
    {
        int previous = issueIndex - 1;
        if (previous < 0 || ContainsAnyIssue(lines[previous])
            || IsOpeningOnlySeparator(lines[previous])
            || !HasCurrentFieldStartAfterEarlierIssue(lines, 0, issueIndex, issue, rule, expectedCount))
            return null;

        string immediate = ScopeNumberPayload(lines[previous], rule, expectedCount);
        if (!IsNumberContinuation(lines[previous], expectedCount, rule, issue))
            return null;

        var leftCandidates = new List<List<string>>
        {
            new() { immediate }
        };

        var expanded = new List<string> { immediate };
        bool foundFieldStart = IsNumberRowStart(lines[previous]);
        for (int index = previous - 1; !foundFieldStart && index >= 0; index--)
        {
            if (ContainsIssueBoundary(lines[index], issue) || IsOpeningOnlySeparator(lines[index])
                || Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))
                break;
            if (!IsNumberContinuation(lines[index], expectedCount, rule, issue))
                break;
            expanded.Insert(0, ScopeNumberPayload(lines[index], rule, expectedCount));
            if (IsNumberRowStart(lines[index]))
                foundFieldStart = true;
        }
        // Any contiguous numeric run directly above the issue cell is a valid
        // left candidate once the scan stopped at a real boundary (issue line,
        // opening separator or a non-number row); it does not need a labeled
        // row start of its own.
        if (expanded.Count > 1)
            leftCandidates.Add(expanded);

        string centreText = SimplifyOcrText(TextAfterIssue(lines[issueIndex], issue));
        centreText = Regex.Split(centreText,
            $@"(?<!不会)(?<!不)开|准|準|[{Zodiac}]\s*\d{{1,2}}\s*[中错錯赢贏]")[0];
        string centre = ScopeNumberPayload(centreText, rule, expectedCount);

        var candidates = leftCandidates
            .Select(left => new List<string>(left) { centre })
            .ToArray();
        return ScanStrictNumberWindowRight(lines, issueIndex, issue, expectedCount, rule, candidates);
    }

    // Reviewed wrapped cards may print the whole field after the issue cell
    // instead of a left cell (the issue cell sits at the left edge of its row).
    // This variant never borrows any preceding row: candidates start from the
    // issue row itself and only forward rows can complete the field.
    private static string? ExtractStrictTrailingNumberWindow(
        string[] lines, int issueIndex, int issue, int expectedCount, OcrRule rule)
    {
        string centreText = SimplifyOcrText(TextAfterIssue(lines[issueIndex], issue));
        centreText = Regex.Split(centreText,
            $@"(?<!不会)(?<!不)开|准|準|[{Zodiac}]\s*\d{{1,2}}\s*[中错錯赢贏]")[0];
        string centre = ScopeNumberPayload(centreText, rule, expectedCount);
        // Only a field that genuinely continues after the issue row needs this
        // window. If the issue row alone already holds the complete answer the
        // ordinary strict block parser adjudicates it (including foreign
        // content between the field and its opening marker).
        if (ExtractNumbers(centre, expectedCount) is not null)
            return null;
        return ScanStrictNumberWindowRight(
            lines, issueIndex, issue, expectedCount, rule, [new List<string> { centre }]);
    }

    // Right-side scan shared by the left-borrow and trailing windows. The
    // count never authorizes ownership; only the next issue marker or a proven
    // next-card leading row ends the field.
    private static string? ScanStrictNumberWindowRight(
        string[] lines,
        int issueIndex,
        int issue,
        int expectedCount,
        OcrRule rule,
        List<string>[] candidates)
    {
        string? ResolveComplete()
        {
            string[] complete = candidates
                .Select(parts => ExtractNumbers(string.Join(' ', parts), expectedCount))
                .Where(value => value is not null)
                .Select(value => value!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return complete.Length == 1 ? complete[0] : null;
        }

        bool sawRightPayload = false;
        bool completedAfterOpening = false;
        string issueTail = SimplifyOcrText(TextAfterIssue(lines[issueIndex], issue));
        bool sawOpeningSeparator = BeforeOpeningResult(issueTail).Length != issueTail.Length;
        for (int index = issueIndex + 1; index < lines.Length; index++)
        {
            bool reviewedTrailingCompletion = candidates.Any(parts =>
                IsReviewedCompactTrailingCompletion(
                    lines, index, issueIndex, issue, expectedCount, rule, parts, sawRightPayload));
            bool ambiguousTail = sawOpeningSeparator && sawRightPayload && IsAmbiguousCompactRow(lines[index], issue);
            if (ContainsIssueBoundary(lines[index], issue) && !reviewedTrailingCompletion && !ambiguousTail)
                break;
            if (Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))
                break;
            if (IsOpeningOnlySeparator(lines[index]))
            {
                sawOpeningSeparator = true;
                continue;
            }
            if (IsNumberRowStart(lines[index]) && sawRightPayload)
                break;
            // The count never authorizes ownership. A pure numeric row closed by
            // a non-target issue marker is only the next card's leading cell
            // when the field genuinely completed AFTER its opening/result cell
            // (a wrapped reviewed card). A field already complete before the
            // opening has no proven owner for later rows: they are unresolved
            // surplus and invalidate the field.
            bool completeNow = candidates.Any(candidate =>
                ExtractNumbers(string.Join(' ', candidate), expectedCount) is not null);
            if (completeNow
                && !Regex.IsMatch(SimplifyOcrText(lines[index]), @"\p{L}")
                && (ParseNumbers(ScopeNumberPayload(lines[index], rule, expectedCount))?.Length ?? 0) >= 2
                && LeadingRunClosesAtNextIssue(lines, index, issue, rule))
            {
                if (sawOpeningSeparator && !completedAfterOpening)
                    return null;
                break;
            }
            if (!reviewedTrailingCompletion && !ambiguousTail
                && !IsNumberContinuation(lines[index], expectedCount, rule, issue))
            {
                if (IsNumberRowStart(lines[index]))
                    break;
                continue;
            }

            string right = ScopeNumberPayload(lines[index], rule, expectedCount);
            foreach (List<string> candidate in candidates)
                candidate.Add(right);
            sawRightPayload = true;
            if (sawOpeningSeparator
                && candidates.Any(candidate => ExtractNumbers(string.Join(' ', candidate), expectedCount) is not null))
                completedAfterOpening = true;
        }
        // Reaching the expected count is only a candidate; the entire physical
        // field must be consumed before accepting it.
        return ResolveComplete();
    }

    // A completed field may stop at the next card's leading row(s): a run of
    // numeric rows that closes at a non-target issue marker before reaching any
    // other field content. The run may span several physical rows (a 36-code
    // grid prints its leading cells on multiple lines), so look ahead over
    // pure numeric rows instead of checking only the immediate next line.
    private static bool LeadingRunClosesAtNextIssue(
        string[] lines, int index, int issue, OcrRule rule)
    {
        for (int next = index + 1; next < lines.Length && next <= index + 40; next++)
        {
            if (OcrLayoutMarkers.IsBoundary(lines[next]))
                continue;
            if (ContainsIssueBoundary(lines[next], issue))
                return !ContainsIssue(lines[next], issue);
            if ((ParseNumbers(ScopeNumberPayload(lines[next], rule))?.Length ?? 0) == 0)
                return false;
        }
        return false;
    }

    private static string? ExtractReviewedSplitNumberWindow(
        string[] lines, int scopeStart, int issueIndex, int issue, int expectedCount, OcrRule rule)
    {
        bool mayUseLeft = HasCurrentFieldStartAfterEarlierIssue(lines, scopeStart, issueIndex, issue, rule, expectedCount);
        int left = issueIndex;
        if (mayUseLeft)
        {
            for (int index = issueIndex - 1; index >= scopeStart; index--)
            {
                if (ContainsIssueBoundary(lines[index], issue))
                    break;
                left = index;
                if (IsNumberRowStart(lines[index]))
                    break;
            }
        }

        var parts = new List<string>();
        for (int index = left; index < issueIndex; index++)
        {
            if (Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))
                return null;
            if (IsOpeningOnlySeparator(lines[index]))
                continue;
            if (IsNumberContinuation(lines[index], expectedCount, rule, issue))
                parts.Add(ScopeNumberPayload(lines[index], rule, expectedCount));
        }
        string centre = ScopeNumberPayload(TextAfterIssue(lines[issueIndex], issue), rule, expectedCount);
        if (!string.IsNullOrWhiteSpace(centre))
            parts.Add(centre);
        bool sawRightPayload = false;
        bool completedAfterOpening = false;
        string issueTail = SimplifyOcrText(TextAfterIssue(lines[issueIndex], issue));
        bool sawOpeningSeparator = BeforeOpeningResult(issueTail).Length != issueTail.Length;
        for (int index = issueIndex + 1; index < lines.Length; index++)
        {
            bool reviewedTrailingCompletion = IsReviewedCompactTrailingCompletion(
                lines, index, issueIndex, issue, expectedCount, rule, parts, sawRightPayload);
            bool ambiguousTail = sawOpeningSeparator && sawRightPayload && IsAmbiguousCompactRow(lines[index], issue);
            if (ContainsIssueBoundary(lines[index], issue) && !reviewedTrailingCompletion && !ambiguousTail)
                break;
            if (Regex.IsMatch(SimplifyOcrText(lines[index]), @"参考|旁栏|排行|统计|说明"))
                break;
            if (IsNumberRowStart(lines[index]) && sawRightPayload)
                break;
            if (IsOpeningOnlySeparator(lines[index]))
            {
                sawOpeningSeparator = true;
                continue;
            }
            // The count never authorizes ownership. A pure numeric row closed
            // by a non-target issue marker is only the next card's leading cell
            // when the field genuinely completed AFTER its opening/result cell.
            // A field already complete before the opening has no proven owner
            // for later rows: they invalidate the field instead of succeeding.
            bool completeNow = ExtractNumbers(string.Join(' ', parts), expectedCount) is not null;
            if (completeNow
                && !Regex.IsMatch(SimplifyOcrText(lines[index]), @"\p{L}")
                && (ParseNumbers(ScopeNumberPayload(lines[index], rule, expectedCount))?.Length ?? 0) >= 2
                && LeadingRunClosesAtNextIssue(lines, index, issue, rule))
            {
                if (sawOpeningSeparator && !completedAfterOpening)
                    return null;
                break;
            }
            if (!reviewedTrailingCompletion && !ambiguousTail
                && !IsNumberContinuation(lines[index], expectedCount, rule, issue))
            {
                if (IsNumberRowStart(lines[index]))
                    break;
                continue;
            }
            parts.Add(ScopeNumberPayload(lines[index], rule, expectedCount));
            sawRightPayload = true;
            if (sawOpeningSeparator
                && ExtractNumbers(string.Join(' ', parts), expectedCount) is not null)
                completedAfterOpening = true;
        }
        return ExtractNumbers(string.Join(' ', parts), expectedCount);
    }

    private static string? ExtractDirectionalNumberTable(
        string[] lines, int issueIndex, int issue, int expectedCount, bool forward, OcrRule rule)
    {
        int start;
        int end;
        if (forward)
        {
            start = issueIndex + 1;
            end = start;
            while (end < lines.Length && !ContainsIssueBoundary(lines[end], issue))
                end++;
        }
        else
        {
            end = issueIndex;
            start = issueIndex - 1;
            while (start >= 0 && !ContainsIssueBoundary(lines[start], issue))
                start--;
            start++;
        }

        var payload = new List<string>();
        bool started = false;
        for (int index = start; index < end; index++)
        {
            if (ContainsIssueBoundary(lines[index], issue))
                break;
            if (IsOpeningOnlySeparator(lines[index]))
                continue;
            if (!IsNumberContinuation(lines[index], expectedCount, rule, issue))
            {
                if (started)
                    break;
                continue;
            }
            payload.Add(ScopeNumberPayload(RemoveIssue(lines[index]), rule, expectedCount));
            started = true;
        }
        return payload.Count == 0 ? null : ExtractNumbers(string.Join(' ', payload), expectedCount);
    }

    private static string ScopeNumberPayload(
        string line, OcrRule rule, int expectedCount = 0)
    {
        string text = BeforeOpeningResult(SimplifyOcrText(RemoveIssue(line)));

        // When the line carries this rule's own identity, take the bounded raw
        // field: everything after the identity and its field label, up to the
        // first foreign column/status marker. All brackets, odd single digits
        // and unknown characters stay inside and are validated downstream; the
        // scoper must not pick a prettier subset.
        (bool bounded, string ownField) = TrimToOwnNumberField(text, rule);
        if (bounded)
            return ownField;

        // No own identity on this line: keep the legacy scoping used by
        // reviewed issue rows and continuation lines.
        text = Regex.Replace(text,
            @"(?<!\d)\d{1,2}\s*(?:个(?:中特码|特码)?|個(?:码中特碼|特碼)?|码|碼|计|計)",
            " ");

        MatchCollection bracketPayloads = Regex.Matches(text,
            @"[【\[](?<payload>[^】\]]+)[】\]]");
        if (bracketPayloads.Count == 1)
        {
            Match bracket = bracketPayloads[0];
            string payload = bracket.Groups["payload"].Value;
            if (Regex.IsMatch(payload, @"^[0-9 ,，.。\s]+$"))
            {
                int bracketEnd = bracket.Index + bracket.Length;
                var extras = new List<string>();
                foreach (Match run in Regex.Matches(text, @"(?<!\d)[0-9]+(?!\d)"))
                {
                    if (run.Index < bracketEnd || run.Value.Length % 2 != 0)
                        continue;
                    if (!IsOwnFieldLeadIn(text[bracketEnd..run.Index]))
                        continue;
                    extras.Add(run.Value);
                }
                return extras.Count == 0 ? payload : payload + " " + string.Join(' ', extras);
            }
            return payload.Contains('\n')
                ? text.Remove(bracket.Index, bracket.Length)
                : text;
        }
        if (bracketPayloads.Count > 1)
            return text;

        Match firstNumber = Regex.Match(text, @"\d");
        if (!firstNumber.Success)
            return text;

        foreach (Match word in Regex.Matches(text, @"\p{L}+"))
        {
            if (word.Index <= firstNumber.Index)
                continue;
            if (word.Value.All(Zodiac.Contains))
                continue;
            return text[..word.Index].TrimEnd();
        }
        return text;
    }

    // Raw value scoping for a continuation of an already owned field. Unlike
    // the legacy no-identity scoper, it never selects a bracket payload or
    // drops odd/pre-bracket digits: the full raw text reaches the shared
    // integrity checker so counter-evidence stays visible.
    private static string ScopedContinuationPayload(string line, OcrRule rule)
    {
        string text = BeforeOpeningResult(SimplifyOcrText(RemoveIssue(line)));
        text = Regex.Replace(text,
            @"(?<!\d)\d{1,2}\s*(?:个(?:中特码|特码)?|個(?:码中特碼|特碼)?|码|碼|计|計)",
            " ");
        return text;
    }

    // Returns the bounded raw field for lines that carry this rule's own
    // identity. bounded=false means the identity is absent and the caller keeps
    // the legacy scoping. A foreign column or status marker closes the field
    // wherever it appears, and an unrecognized lead-in between identity and
    // payload closes it immediately.
    private static (bool Bounded, string Text) TrimToOwnNumberField(string text, OcrRule rule)
    {
        string[] identities = new[]
        {
            rule.Keyword, rule.Label ?? string.Empty,
            rule.Section ?? string.Empty, rule.RequiredKeyword ?? string.Empty
        }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (identities.Length == 0)
            return (false, text);

        int firstDigit = -1;
        for (int index = 0; index < text.Length;)
        {
            if (!char.IsDigit(text[index]))
            {
                index++;
                continue;
            }
            int runStart = index;
            while (index < text.Length && char.IsDigit(text[index]))
                index++;
            // Printed cardinalities (“36码”, “36计”) are layout metadata and
            // must not be mistaken for the payload that closes the author cell.
            if (Regex.IsMatch(text[index..], @"^\s*(?:个(?:中特码|特码)?|個(?:码中特碼|特碼)?|码|碼|计|計)"))
                continue;
            firstDigit = runStart;
            break;
        }
        if (firstDigit < 0)
            return (false, text);

        (string normalized, int[] map) = NormalizeWithMap(text);
        int firstDigitNormalized = Array.IndexOf(map, firstDigit);
        if (firstDigitNormalized < 0)
            return (false, text);

        int ownEnd = -1;
        foreach (string identity in identities)
        {
            int found = normalized.LastIndexOf(identity, firstDigitNormalized, StringComparison.Ordinal);
            if (found >= 0)
                ownEnd = Math.Max(ownEnd, found + identity.Length);
        }
        if (ownEnd < 0)
            return (false, text);

        int ownEndRaw = ownEnd - 1 >= 0 && ownEnd - 1 < map.Length
            ? map[ownEnd - 1]
            : -1;
        int rawNext = ownEndRaw >= 0 ? ownEndRaw + 1 : text.Length;
        // A banner may wrap the author identity in a decorated bracket
        // (“【长安之星***700新澳门六合彩700***杀码】”). That bracket is not data:
        // skip it whole so its decoration digits cannot enter the count.
        bool bannerSkipped = false;
        if (ownEndRaw >= 0)
        {
            foreach (Match bracket in Regex.Matches(text, @"[【\[（(][^】\]）)]*[】\]）)]"))
            {
                if (ownEndRaw < bracket.Index || ownEndRaw >= bracket.Index + bracket.Length)
                    continue;
                string payload = bracket.Value[1..^1];
                if (Regex.IsMatch(payload, @"^[0-9 ,，.。\s]+$"))
                    continue;
                string tail = text[(bracket.Index + bracket.Length)..];
                if (!Regex.IsMatch(tail, @"[【\[（(]\s*[0-9 ,，.。]+[】\]）)]"))
                    continue;
                rawNext = bracket.Index + bracket.Length;
                bannerSkipped = true;
                break;
            }
        }
        if (!bannerSkipped)
        {
            if (rawNext > firstDigit)
                rawNext = firstDigit;
            if (!IsOwnFieldLeadIn(text[rawNext..firstDigit]))
                return (true, string.Empty);
        }

        string field = text[rawNext..];
        int marker = EarliestForeignMarker(field);
        return (true, marker < 0 ? field : field[..marker]);
    }

    private static int EarliestForeignMarker(string text)
    {
        int earliest = -1;
        foreach (string marker in new[] { "其他栏目", "待更新", "参考", "旁栏", "排行", "统计", "说明" })
        {
            int found = text.IndexOf(marker, StringComparison.Ordinal);
            if (found >= 0 && (earliest < 0 || found < earliest))
                earliest = found;
        }
        return earliest;
    }

    private static bool IsOwnFieldLeadIn(string segment)
    {
        if (segment.Contains("其他", StringComparison.Ordinal)
            || segment.Contains("栏目", StringComparison.Ordinal)
            || segment.Contains("待更新", StringComparison.Ordinal))
            return false;
        MatchCollection words = Regex.Matches(segment, @"\p{L}+");
        if (words.Count == 0)
            return true;
        // Only a trailing field label may sit between the author identity and
        // the payload (“新澳门六合彩杀码”, “三十六码”). A foreign column label
        // such as “其他栏目” does not end with a field suffix and closes it.
        string last = words[^1].Value;
        return Regex.IsMatch(last, @"^(?:开|開|禁|.*(?:杀|殺|码|碼|计|計|围|圍))$");
    }

    // Char-aligned normalization: every kept output character maps back to its
    // source index, so a match found on the normalized text can be sliced from
    // the original characters (brackets, spaces and punctuation kept).
    private static (string Text, int[] Map) NormalizeWithMap(string raw)
    {
        var builder = new System.Text.StringBuilder(raw.Length);
        var map = new List<int>(raw.Length);
        for (int index = 0; index < raw.Length; index++)
        {
            char value = SimplifyOcrText(raw[index].ToString())[0];
            value = value switch
            {
                '①' => '1', '②' => '2', '③' => '3', '④' => '4',
                '⑤' => '5', '⑥' => '6', '⑦' => '7', _ => value
            };
            if (!char.IsLetter(value) && !char.IsNumber(value))
                continue;
            builder.Append(value);
            map.Add(index);
        }
        return (builder.ToString(), map.ToArray());
    }

    private static bool IsPureForbiddenLabel(string line) =>
        Regex.IsMatch(Normalize(line), @"^(?:月?禁(?:止)?)$");

    private static bool IsNumberContinuation(
        string line, int expectedCount, OcrRule rule, int selectedIssue = 0)
    {
        string simplified = SimplifyOcrText(line);
        if (ContainsAnyIssue(line)
            || selectedIssue > 0 && IsBareIssueBoundary(line, selectedIssue)
            || ContainsPeerIdentity(line, rule)
            || Regex.IsMatch(simplified, @"参考|旁栏|排行|统计|说明|其他栏目|待更新"))
            return false;

        string scoped = ScopeNumberPayload(simplified, rule, expectedCount);
        string[]? numbers = ParseNumbers(scoped);
        if (numbers is null || numbers.Length == 0)
            return false;

        // Opening/result suffixes are not part of field ownership. Evaluate the
        // decoration on the data side of “开”, so “杀02...开??” remains a proven
        // kill-number field and “48开888” remains a pure numeric continuation.
        string ownershipText = BeforeOpeningResult(simplified);
        bool hasLetters = Regex.IsMatch(ownershipText, @"\p{L}");
        if (!hasLetters)
            return true;

        string normalized = Normalize(line);
        bool ownIdentity = ContainsOwnNumberIdentity(line, rule);
        bool zodiacOnly = Regex.Matches(ownershipText, @"\p{L}+")
            .Cast<Match>()
            .All(match => match.Value.All(Zodiac.Contains));
        string decoration = Regex.Replace(
            ownershipText, @"[0-9\s,，.。:：*【】\[\]()（）?？!！←→]+", string.Empty);
        bool structuralField = Regex.IsMatch(decoration,
            @"^(?:(?:杀|殺){1,3}|开|開|禁|月禁|本月禁|今期禁|杀码|殺碼|杀特码|殺特碼|不开|不開|精选杀|精選殺|码|碼|特码|特碼|码中特码|码中特碼|计|計|包围码|包圍碼|锁三十六码|鎖三十六碼|庄家必杀|莊家必殺|绝杀|絕殺|绝杀[一二三四五六七八九十0-9]+码|絕殺[一二三四五六七八九十0-9]+碼|封杀|封殺|准|準)$")
            || Regex.IsMatch(decoration, @"^\p{L}{1,6}(?:杀|殺|码|碼|计|計)$");
        // 六合日报三六 的号码表以「包准：」开头，这是该卡自己的字段标签，不是别人的数据行；
        // 只对这条已复核规则放行，其他号码资料仍不接受该标签。
        bool ownCardLabel = rule.Id == "六合日报三六"
            && Regex.IsMatch(decoration, @"^(?:包准|包準)$");
        if (!ownIdentity && !structuralField && !zodiacOnly && !ownCardLabel)
            return false;

        // Brackets prove grouping only after ownership has been established.
        // Generic kill words plus a complete bracket can never claim a foreign field.
        return true;
    }

    private static bool IsBareIssueBoundary(string line, int selectedIssue)
    {
        string trimmed = SimplifyOcrText(line).Trim();
        // A bracket-wrapped bare number (【2302】/(2302)/[2302]) is the same
        // input class as a bare row: one classification pass, not a value a
        // later compatibility branch may split back into two business numbers.
        if (trimmed.Length >= 2
            && "([【（".Contains(trimmed[0])
            && ")]】）".Contains(trimmed[^1]))
            trimmed = trimmed[1..^1].Trim();
        if (!Regex.IsMatch(trimmed, @"^\d{4,6}$"))
            return false;
        // Being the selected issue is a query filter, not input classification.
        // A standalone 4-6 digit row is always an issue/ambiguous marker, so it
        // can never be consumed as two business numbers of the previous field,
        // even when its text equals the selected issue.
        return true;
    }

    // Issue-like rows are never data. Classification is relative to the query
    // context, not a global 1000-1999 range: a compact pair row such as 4445
    // stays data, while a value within the surrounding issue neighborhood (or
    // any year+issue six-digit form) stays an issue marker.
    private static bool IsIssueLikeBareNumber(string trimmed, int selectedIssue)
    {
        if (!Regex.IsMatch(trimmed, @"^\d{4,6}$")
            || !int.TryParse(trimmed, out int actual))
            return false;
        if (trimmed.Length >= 6)
            return true;
        return actual is >= 1000 and <= 1999
            || actual == selectedIssue
            || Math.Abs(actual - selectedIssue) <= 60;
    }

    private static bool HasExactBracketPayload(string line, int expectedCount)
    {
        foreach (Match bracket in Regex.Matches(
            BeforeOpeningResult(line), @"[【\[（(](?<value>[^】\]）)]*)[】\]）)]"))
        {
            if (FormatNumbers(ParseNumbers(bracket.Groups["value"].Value), expectedCount) is not null)
                return true;
        }
        return false;
    }

    private static bool IsOpeningOnlySeparator(string line) =>
        Regex.IsMatch(SimplifyOcrText(line).Trim(), @"^开[?？中错錯对對准準]*$");

    private static bool IsOpeningResultOnly(string line) =>
        Regex.IsMatch(
            SimplifyOcrText(line).Trim(),
            @"^开\s*[:：]?\s*(?:[?？]?\d{1,2}|[?？])(?:\s*[中错錯对對准準])*$");

    // A continuation row that is exactly one numeric bracket ("[01.04...]")
    // is a wrapped part of the owned field, not a standalone answer container.
    private static bool IsPureNumericBracketRow(string line)
    {
        string trimmed = SimplifyOcrText(line).Trim();
        if (trimmed.Length < 3
            || !"([【（".Contains(trimmed[0])
            || !")]】）".Contains(trimmed[^1]))
            return false;
        string inner = trimmed[1..^1];
        return inner.Length > 0
            && inner.Any(char.IsDigit)
            && Regex.IsMatch(inner, @"^[0-9 ,，.。\s]+$");
    }

    private static bool IsNumberRowStart(string line) =>
        Regex.IsMatch(line, @"\p{L}.*\d") && !ContainsAnyIssue(line);

    private static string? ExtractNumbers(string text, int expectedCount)
    {
        string beforeOpening = RemoveIssue(BeforeOpeningResult(text));
        Match[] brackets = Regex.Matches(
            beforeOpening, @"[【\[（(](?<value>[^】\]）)]*)[】\]）)]").Cast<Match>().ToArray();

        // Every bracket is part of the raw field. A multi-number bracket is its
        // own answer container; a single number inside brackets is only an
        // annotation/separator and flows into the surrounding raw field; a
        // short non-numeric bracket is unknown content (counter-evidence); a
        // multi-line non-numeric bracket is title decoration and is dropped.
        var containers = new List<string>();
        string data = beforeOpening;
        for (int index = brackets.Length - 1; index >= 0; index--)
        {
            Match bracket = brackets[index];
            string payload = bracket.Groups["value"].Value;
            if (Regex.IsMatch(payload, @"^[0-9 ,，.。\s]+$"))
            {
                string[]? parsed = ParseNumbers(payload);
                if (parsed is null)
                    return null;
                data = parsed.Length > 1
                    ? data.Remove(bracket.Index, bracket.Length).Insert(bracket.Index, " ")
                    : data.Remove(bracket.Index, bracket.Length).Insert(bracket.Index, " " + payload + " ");
                if (parsed.Length > 1)
                    containers.Add(payload);
                continue;
            }
            if (!payload.Contains('\n'))
                return null;
            data = data.Remove(bracket.Index, bracket.Length).Insert(bracket.Index, " ");
        }

        // Outside containers, letters at or after the first number/bracket must
        // be zodiac or an explicit structural label. An unknown marker there is
        // a different column (or OCR noise) and invalidates this field; without
        // this check "01 02 03 04 05 06X" would silently equal "...06".
        int valueStart = -1;
        if (brackets.Length > 0)
            valueStart = brackets[0].Index;
        Match firstNumber = Regex.Match(beforeOpening, @"\d");
        if (firstNumber.Success && (valueStart < 0 || firstNumber.Index < valueStart))
            valueStart = firstNumber.Index;
        if (valueStart >= 0)
        {
            foreach (Match word in Regex.Matches(beforeOpening, @"\p{L}+"))
            {
                if (word.Index < valueStart)
                    continue;
                if (brackets.Any(bracket =>
                        word.Index >= bracket.Index && word.Index < bracket.Index + bracket.Length))
                    continue;
                if (word.Value.All(Zodiac.Contains) || IsStructuralFieldWord(word.Value))
                    continue;
                return null;
            }
        }

        // Containers are validated independently and never merged: an
        // incomplete/invalid container is counter-evidence, and two different
        // complete answers are a conflict, not an over-count Missing.
        var candidates = new List<string>(containers);
        if (Regex.IsMatch(data, @"\d"))
            candidates.Add(data);
        if (candidates.Count == 0)
            candidates.Add(data);

        var complete = new HashSet<string>(StringComparer.Ordinal);
        foreach (string candidate in candidates)
        {
            string[]? numbers = ParseNumbers(RemoveIssue(candidate));
            if (numbers is null)
                return null;
            string? formatted = FormatNumbers(numbers, expectedCount);
            if (formatted is null)
                return null;
            complete.Add(formatted);
        }
        return complete.Count == 0 ? null : complete.Count == 1 ? complete.Single() : ConflictMarker;
    }

    private static bool IsStructuralFieldWord(string word) => Regex.IsMatch(word,
        @"^(?:杀|殺|开|開|禁|准|準|红|蓝|绿|红波|蓝波|绿波|绝杀|絕殺|封杀|封殺|精选杀|精選殺|庄家必杀|莊家必殺|杀特码|殺特碼|特码|特碼|码中特码|码中特碼|包围码|包圍碼|锁三十六码|鎖三十六碼|金木水火土|(?:(?:杀|殺)?[0-9零一二三四五六七八九十]{1,4})?(?:码|碼|计|計))$");

    private static string BeforeOpeningResult(string text)
    {
        string simplified = SimplifyOcrText(text);
        Match opening = Regex.Match(simplified,
            $@"(?<!不)(?<!不会)开(?=\s*[:：]?\s*(?:[?？]+|[0-9]+|[{Zodiac}]|$))");
        int cut = opening.Success ? opening.Index : simplified.Length;
        // A run of two or more plus signs is a column separator on these cards
        // ("253区杀-猪++牛"): the value field ends before it. A single '+' stays
        // a normal data separator (e.g. 生肖组合 "杀狗+兔").
        Match plus = Regex.Match(simplified, @"[+＋]{2,}");
        if (plus.Success && plus.Index < cut)
            cut = plus.Index;
        return simplified[..cut];
    }

    private static string[]? ParseNumbers(string text)
    {
        text = Regex.Replace(text, @"(?<!\d)\d{1,2}\s*(?:个(?:中特码|特码)?|码|计|計)", "");
        text = Regex.Replace(text, @"(?<!\d)\d+\s*%", "");
        text = Regex.Replace(text, @"[开特發发]\s*\d{2}\s*准?", "");
        var numbers = new List<string>();
        foreach (Match match in Regex.Matches(text, @"(?<!\d)[0-9]+(?!\d)"))
        {
            string run = match.Value;
            if (run.Length % 2 != 0)
                return null;
            string[] pairs = Enumerable.Range(0, run.Length / 2)
                .Select(index => run.Substring(index * 2, 2))
                .ToArray();
            if (!pairs.All(pair => int.TryParse(pair, out int number) && number is >= 1 and <= 49))
                return null;
            numbers.AddRange(pairs);
        }
        return numbers.ToArray();
    }

    private static string? FormatNumbers(string[]? numbers, int expectedCount) =>
        numbers is null || numbers.Length != expectedCount || numbers.Distinct().Count() != expectedCount
            ? null
            : string.Join(' ', numbers);

    private static string RemoveIssue(string text) =>
        YearIssueRegex.Replace(
            BareIssueRegex.Replace(
                CompactYearIssueRegex.Replace(IssueRegex.Replace(text, ""), ""),
                match => match.Groups["issue"].Value.Length == 3 ? "" : match.Value),
            "");

    private static char ToArabicDigit(char value) => value switch
    {
        '零' => '0', '一' => '1', '二' => '2', '三' => '3', '四' => '4', _ => value
    };

    private static bool ContainsIssueBoundary(string line, int selectedIssue)
    {
        if (ContainsAnyIssue(line))
            return true;
        return IsBareIssueBoundary(line, selectedIssue);
    }

    private static bool ContainsAnyIssue(string line)
    {
        if (IssueRegex.IsMatch(line))
            return true;
        if (CompactYearIssueRegex.IsMatch(line))
            return true;
        Match leading = BareIssueRegex.Match(line);
        return leading.Success && leading.Groups["issue"].Value.Length == 3
            || YearIssueRegex.IsMatch(line);
    }

    // 行内出现「别的期号」才算越界证据；本期期号本身可能被 OCR 重复识别在相邻行，
    // 不能当成本期数据的边界。
    private static bool ContainsOtherIssue(string line, int issue) =>
        FindIssues([line]).Any(found => found != issue);

    private static bool ContainsIssue(string line, int issue)
    {
        foreach (Match match in IssueRegex.Matches(line))
        {
            if (int.TryParse(match.Groups["issue"].Value, out int actual) && actual == issue)
                return true;
        }

        foreach (Match match in CompactYearIssueRegex.Matches(line))
        {
            if (int.TryParse(match.Groups["issue"].Value, out int actual) && actual == issue)
                return true;
        }

        Match leading = BareIssueRegex.Match(line);
        if (leading.Success
            && int.TryParse(leading.Groups["issue"].Value, out int leadingIssue)
            && leadingIssue == issue)
            return true;

        Match merged = MergedRowIndexIssueRegex.Match(line);
        if (merged.Success
            && int.TryParse(merged.Groups["issue"].Value, out int mergedIssue)
            && mergedIssue == issue)
            return true;

        Match yearIssue = YearIssueRegex.Match(line);
        return yearIssue.Success
            && int.TryParse(yearIssue.Groups["issue"].Value, out int yearIssueNumber)
            && yearIssueNumber == issue;
    }

    private static string SimplifyFixedCardText(string text) => SimplifyOcrText(text)
        .Replace('圖', '图').Replace('帥', '帅').Replace('鐵', '铁')
        .Replace('殺', '杀').Replace('碼', '码').Replace('頭', '头')
        .Replace('開', '开').Replace('獎', '奖').Replace('結', '结')
        .Replace('報', '报').Replace('圍', '围').Replace('絕', '绝')
        .Replace('鎖', '锁').Replace('數', '数').Replace('穩', '稳')
        .Replace('買', '买').Replace('輸', '输').Replace('會', '会')
        .Replace('贏', '赢').Replace('慘', '惨').Replace('計', '计');

    private static string SimplifyOcrText(string text) => text
        .Replace('馬', '马')
        .Replace('龍', '龙')
        .Replace('雞', '鸡')
        .Replace('豬', '猪')
        .Replace('點', '点')
        .Replace('宵', '肖')
        .Replace('紅', '红')
        .Replace('藍', '蓝')
        .Replace('綠', '绿')
        .Replace('門', '门')
        .Replace('無', '无')
        .Replace('錯', '错')
        .Replace('雙', '双')
        .Replace('單', '单')
        .Replace('開', '开')
        .Replace('兰', '蓝');

    public static string Normalize(string text) => Regex.Replace(SimplifyOcrText(text), @"[^\p{L}\p{N}]", "")
        .Replace("梁薇薇", "梁微微", StringComparison.Ordinal)
        .Replace('①', '1')
        .Replace('②', '2')
        .Replace('③', '3')
        .Replace('④', '4')
        .Replace('⑤', '5')
        .Replace('⑥', '6')
        .Replace('⑦', '7')
        .Replace('⑧', '8')
        .Replace('⑨', '9');
}
