using System.Text.RegularExpressions;

namespace OcrLineTool;

// The same publisher prints several hundred unrelated tables with the same header and result rows.
// Only lettering inside the title band may establish a second route to a card.
internal sealed class MacauTitleIndex
{
    private sealed record Title(VisualTemplateDefinition Template, string[] Names);
    private readonly Title[] titles;
    private readonly Dictionary<string, (VisualTemplateMatch Match, bool OtherIssue)> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> rejected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<int>> observedIssues = new(StringComparer.OrdinalIgnoreCase);
    private int? targetIssue;
    internal const string SingleCardRule = "三怪肖肖";
    internal const string SelectionName = "标题模板及本地标题核对";
    internal const string MissingReason = "未找到完整资料标题或对应模板";

    public MacauTitleIndex(IReadOnlyList<VisualTemplateDefinition> templates, IReadOnlyDictionary<string, OcrRule> rules)
    {
        titles = templates.Select(t => new Title(t, Names(rules[t.RuleIds[0]]).Select(Normalize).ToArray())).ToArray();
    }

    private static string Normalize(string s) => RuleEngine.Normalize(RuleEngine.SimplifyFixedCardText(s))
        .Replace('黃', '黄').Replace('個', '个').Replace('費', '费').Replace('寶', '宝').Replace('庫', '库').Replace('網', '网');

    private static string[] Names(OcrRule r) => r.Id switch
    {
        "庄家" => [r.Keyword, "庄家必吃10码让您省心"],
        "帅铁尾" => [r.Keyword, "份好的杀料决定了中奖的机会"],
        "帅铁头" => ["既然你已经看到这里了说明你已经掌握了致"],
        "藏宝头" => ["藏宝库无错四头"],
        "超级赢家半波波" => ["大赢家绝杀半波"],
        "黄大仙半波" => ["黄大仙杀半个波色"],
        "江西两肖" => [r.Keyword, "江西必杀两肖"],
        "白小姐四季" => ["白小姐四季生肖", "白小姐一四季生肖"],
        "白小姐来钱" => ["白小姐九肖来钱"],
        "神算子避头" => ["神算子避开1头"],
        "近期开奖员" => ["近期开奖员九肖中特"],
        "毛老二" => ["老毛头九肖中特"],
        "大赢家九肖" => ["大赢家狂赢九肖"],
        "亮剑九肖" => ["亮剑团队9肖中特", "亮剑团队九肖中特"],
        "小精肖肖" => [r.Keyword, "小精禁肖"],
        _ => [r.Keyword + r.RequiredKeyword]
    };

    public bool Read(OcrEvidence image, int width, int issue)
    {
        targetIssue = issue;
        var tokens = (image.TokenItems ?? image.Items).Where(t => t.Box != null && t.Box.CenterY <= width * .55)
            .Select(t => (Text: Normalize(t.Text), Box: t.Box!, t.ViewId)).Where(t => t.Text.Length > 0).ToArray();
        int[] issues = tokens.SelectMany(x => Regex.Matches(x.Text, @"^第?(?:20\d{2})?(\d{1,3})期$")
            .Select(m => int.Parse(m.Groups[1].Value))).Distinct().ToArray();
        if (issues.Length > 0)
        {
            if (!observedIssues.TryGetValue(image.SourcePath, out HashSet<int>? observed))
                observedIssues[image.SourcePath] = observed = [];
            observed.UnionWith(issues);
        }

        var matches = new List<Title>();
        foreach (var title in titles)
        {
            var t = title.Template;
            if (t.SourceFolder is { Length: > 0 } folder && !RuleEngine.MatchesFolder(image.SourcePath, folder)) continue;
            double top = (t.FingerprintTopWidthRatio ?? .1125) - .045;
            double bottom = (t.FingerprintBottomWidthRatio ?? .2125) + .045;
            foreach (var sameView in tokens.GroupBy(x => x.ViewId))
            {
                bool Has(string part) => sameView.Any(x => x.Text.Contains(part, StringComparison.Ordinal));
                if (t.Id switch
                {
                    "时点半" => !Has("36码"),
                    "老墨两肖" => !Has("上期开奖结果"),
                    "伯公绝杀" => !Has("杀四个特码"),
                    "帅铁头" => !Has("富密码") || !Has("帅铁杀一头"),
                    "帅铁尾" => !Has("帅铁杀一尾"),
                    _ => false
                }) continue;
                var view = sameView.Where(x => x.Box.CenterY >= width * top && x.Box.CenterY <= width * bottom);
                // A split title can share a baseline. Never join disconnected columns or different result rows.
                var rows = new List<(OcrBox Last, string Text)>();
                foreach (var token in view.OrderBy(x => x.Box.X))
                {
                    int i = rows.FindLastIndex(r => Math.Abs(r.Last.CenterY - token.Box.CenterY) < Math.Min(r.Last.Height, token.Box.Height) * .55 && token.Box.X - r.Last.Right <= width * .12 && token.Box.X >= r.Last.Right - width * .055);
                    if (i < 0) rows.Add((token.Box, token.Text)); else rows[i] = (token.Box, rows[i].Text + token.Text);
                }
                if (rows.Select(r => r.Text).Concat(view.Select(x => x.Text)).Any(text => title.Names.Any(n =>
                    t.Id == "禁止肖肖" ? text == n : text.Contains(n, StringComparison.Ordinal))))
                { matches.Add(title); break; }
            }
        }
        if (matches.Count != 1)
        {
            if (matches.Count > 1) rejected.Add(image.SourcePath);
            return false;
        }
        var template = matches[0].Template;
        bool otherIssue = template.Id == SingleCardRule &&
            observedIssues.TryGetValue(image.SourcePath, out HashSet<int>? issueValues) &&
            issueValues.Count > 0 && !issueValues.Contains(issue);
        entries[image.SourcePath] = (new(image.SourcePath, template, -1, 0, image.SourceHash), otherIssue);
        return true;
    }

    public IReadOnlyList<VisualTemplateMatch> Combine(IReadOnlyList<VisualTemplateMatch> visual)
    {
        var all = new List<VisualTemplateMatch>();
        foreach (var m in visual)
        {
            bool otherIssue = m.Template.Id == SingleCardRule &&
                targetIssue is int issue &&
                observedIssues.TryGetValue(m.SourcePath, out HashSet<int>? observed) &&
                observed.Count > 0 && !observed.Contains(issue);
            if (rejected.Contains(m.SourcePath) || otherIssue)
                continue;
            if (entries.TryGetValue(m.SourcePath, out var entry))
            {
                if (!entry.OtherIssue && entry.Match.Template.Id == m.Template.Id)
                    all.Add(m with { RequiresTitleVerification = false });
            }
            else if (!m.RequiresTitleVerification)
            {
                all.Add(m);
            }
        }
        foreach (var entry in entries.Values.Where(e => !e.OtherIssue && !rejected.Contains(e.Match.SourcePath)))
            if (!all.Any(m => m.Template.Id == entry.Match.Template.Id && StringComparer.OrdinalIgnoreCase.Equals(m.SourcePath, entry.Match.SourcePath))) all.Add(entry.Match);
        HashSet<string> singleCardPaths = all.Where(match => match.Template.Id == SingleCardRule)
            .Select(match => match.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(match => match.Template.Id != SingleCardRule || singleCardPaths.Contains(match.SourcePath))
            .ToArray();
    }
}
