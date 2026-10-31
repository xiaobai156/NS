namespace OcrLineTool;

public sealed record LocalCandidatePlan(
    string Path,
    IReadOnlyList<OcrRule> Rules,
    bool IsPrimary);

public static class LocalCandidatePlanner
{
    // Only unresolved row-table rules receive this review, within their own folder.
    // Returned plans are added after normal selection so a review cannot change
    // a sibling zodiac rule's original small-model candidate.
    internal static async Task<IReadOnlyList<LocalCandidatePlan>> ReviewRowCandidatesAsync(
        IReadOnlyList<string> imagePaths,
        IReadOnlyList<LocalCandidatePlan> selected,
        IReadOnlyList<OcrRule> rules,
        IReadOnlyList<OcrRule> catalog,
        int issue,
        PaddleLocalOcrClient client,
        CancellationToken cancellationToken,
        IProgress<LocalOcrProgress>? progress = null)
    {
        var pending = rules.Where(rule => rule.RequireRowStructure
            && !selected.Any(plan => plan.Rules.Any(item => item.Id == rule.Id))).ToArray();
        string[] paths = imagePaths.Where(path => RuleCatalog.PathBelongsToGroup(path, "嫣然心水")
            && pending.Any(rule => RuleEngine.MatchesRuleFolder(path, rule))).ToArray();
        if (paths.Length == 0)
            return [];
        await client.RecognizeBatchAsync(paths, progress, titleRatio: 1,
            model: PaddleOcrModels.LocalPrimary, cancellationToken: cancellationToken);
        var results = client.LastEvidence
            .Where(pair => !client.LastImageErrors.ContainsKey(pair.Key)
                && !client.LastInvalidImagePaths.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => RuleEngine.RowCandidateLines(pair.Value), StringComparer.OrdinalIgnoreCase);
        return Build(paths, results, pending, issue, catalog);
    }

    public static IReadOnlyList<LocalCandidatePlan> Build(
        IReadOnlyList<string> imagePaths,
        IReadOnlyDictionary<string, IReadOnlyList<string>> localResults,
        IReadOnlyList<OcrRule> rules, int? issue = null,
        IReadOnlyList<OcrRule>? completeRules = null)
    {
        var options = new List<CandidateOption>();
        foreach (string path in imagePaths)
        {
            if (!localResults.TryGetValue(path, out IReadOnlyList<string>? lines))
                continue;

            HashSet<string> titleRuleIds = RuleEngine.FindMatches(lines, rules)
                .Select(rule => rule.Id)
                .ToHashSet(StringComparer.Ordinal);
            bool isSummary = RuleEngine.IsZodiacSummary(lines);
            IReadOnlyList<OcrRule> matchedRules = RuleEngine.FindMatches(path, lines, rules, completeRules);
            // 汇总图通常包含所有肖名，但本地 OCR 可能只识别到“统计表”标题。
            // 仍把生肖规则交给云 OCR，避免误报“未找到对应图片”。
            if (isSummary)
            {
                string imageFolder = Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty;
                matchedRules = matchedRules
                    .Concat(rules.Where(rule => rule.Type is "生肖" or "单生肖" or "生肖组合" or "统计生肖")
                        .Where(rule => rule.Type != "统计生肖"
                            || string.IsNullOrWhiteSpace(rule.Folder)
                            || imageFolder.Equals(rule.Folder, StringComparison.OrdinalIgnoreCase))
                        .Where(rule => string.IsNullOrWhiteSpace(rule.Folder)
                            || imageFolder.Equals(rule.Folder, StringComparison.OrdinalIgnoreCase)
                            || titleRuleIds.Contains(rule.Id)))
                    .DistinctBy(rule => rule.Id, StringComparer.Ordinal)
                    .ToArray();
            }

            foreach (OcrRule rule in matchedRules)
            {
                string expectedFolder = rule.Folder ?? rule.Keyword;
                // A folder-identity rule carries no material name on its cards,
                // so value probing for ranking must not require the keyword.
                OcrRule rankingRule = rule.AllowFolderIdentity
                    ? rule with { AllowValueWithoutKeyword = true }
                    : rule;
                // Author cards print their name in the header (【玫瑰香】NEW心水).
                // Shared statistics sheets mention the author only in a data row,
                // so a header match must outrank them for primary_only rules,
                // otherwise the sheet's cross-author rows can win by filename
                // order and feed conflicting values for the same issue.
                bool headerMatched = RuleEngine.FindMatches(
                        lines.Where(line => !string.IsNullOrWhiteSpace(line)).Take(2).ToArray(), [rule])
                    .Any(matched => matched.Id.Equals(rule.Id, StringComparison.Ordinal));
                options.Add(new CandidateOption(
                    path,
                    rule,
                    titleRuleIds.Contains(rule.Id),
                    RuleEngine.HasValueForAnyIssue(lines, rankingRule),
                    issue is int target && RuleEngine.ExtractFinalValue(lines, target, rankingRule) is not null,
                    (Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty)
                        .Equals(expectedFolder, StringComparison.OrdinalIgnoreCase),
                    isSummary,
                    headerMatched));
            }
        }

        var selectedPathByRule = new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, int> ruleOrder = rules
            .Select((rule, index) => (rule.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        foreach (OcrRule rule in rules)
        {
            CandidateOption[] ruleOptions = options
                .Where(option => option.Rule.Id.Equals(rule.Id, StringComparison.Ordinal))
                .ToArray();
            // header_identity rules only ever use the author's own card: the
            // name must appear in the image header. Shared statistics sheets
            // are ignored entirely, even when the dedicated card is missing.
            if (rule.HeaderIdentity)
                ruleOptions = ruleOptions.Where(option => option.HeaderMatched).ToArray();
            if (ruleOptions.Length == 0)
                continue;

            CandidateOption[] explicitOptions = ruleOptions.Where(option => option.TitleMatched).ToArray();
            // lock_to_folder：这条资料的作者名与目录名不一致（如「梁微微」在「梁薇薇」目录里），
            // 只认同夹的卡，避免同群别处的同名行被选上。规则自己声明，不再按 Id 写死。
            CandidateOption[] lockedFolderOptions = rule.LockToFolder
                ? ruleOptions.Where(option => option.ExpectedFolder).ToArray()
                : [];
            IEnumerable<CandidateOption> preferred = lockedFolderOptions.Length > 0
                ? lockedFolderOptions
                : rule.AllowFolderIdentity ? ruleOptions
                : explicitOptions.Length > 0 ? explicitOptions : ruleOptions;
            // 决胜信号只用「这张图有多像该规则的卡」：取到本期值 > 汇总表生肖 > 取到任意值 >
            // 同夹 > 作者名印在图头。最后按完整路径升序，只为让结果稳定可复现 ——
            // 文件名是抓图时间序号，与卡片对不对无关，不能用来决胜。
            CandidateOption best = preferred
                .OrderByDescending(option => option.HasTargetValue)
                .ThenByDescending(option => option.Rule.Type == "生肖" && option.IsSummary)
                .ThenByDescending(option => option.HasAnyValue)
                .ThenByDescending(option => option.ExpectedFolder)
                .ThenByDescending(option => option.HeaderMatched)
                .ThenBy(option => option.Path, StringComparer.OrdinalIgnoreCase)
                .First();
            selectedPathByRule[rule.Id] = best.Path;
        }

        var plans = new List<LocalCandidatePlan>();
        foreach (string path in imagePaths)
        {
            OcrRule[] assigned = rules
                .Where(rule => selectedPathByRule.TryGetValue(rule.Id, out string? selected)
                    && selected.Equals(path, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (assigned.Length > 0)
                plans.Add(new LocalCandidatePlan(path, assigned, IsPrimary: true));
        }

        // 嫣然心水只用主图，不准备备选候选，避免同夹混入的卡再给同一条规则喂第二个值。
        // 判定按「这张备选图自己」所在群，不再看整批里有没有嫣然心水路径 —— 否则
        // 批里混进一张嫣然心水的图，会把其他群规则的备选候选一起禁掉。
        foreach (string path in imagePaths)
        {
            OcrRule[] alternatives = options
                .Where(option => option.Path.Equals(path, StringComparison.OrdinalIgnoreCase)
                    && !option.Rule.PrimaryOnly
                    && selectedPathByRule.TryGetValue(option.Rule.Id, out string? selected)
                    && !selected.Equals(path, StringComparison.OrdinalIgnoreCase))
                .Select(option => option.Rule)
                .DistinctBy(rule => rule.Id, StringComparer.Ordinal)
                .OrderBy(rule => ruleOrder[rule.Id])
                .ToArray();
            if (alternatives.Length > 0 && !RuleCatalog.PathBelongsToGroup(path, "嫣然心水"))
                plans.Add(new LocalCandidatePlan(path, alternatives, IsPrimary: false));
        }

        return plans;
    }

    private sealed record CandidateOption(
        string Path,
        OcrRule Rule,
        bool TitleMatched,
        bool HasAnyValue,
        bool HasTargetValue,
        bool ExpectedFolder,
        bool IsSummary,
        bool HeaderMatched);
}
