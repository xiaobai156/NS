from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

replacements = [
    (
'''            "色单双" => Regex.Matches(text, @"[红蓝绿](?:波)?[单双]")
                .Select(match => match.Value.Replace("波", "", StringComparison.Ordinal)),''',
'''            "色单双" or "半波" => Regex.Matches(text, @"[红蓝绿](?:波)?[单双]")
                .Select(match => match.Value.Replace("波", "", StringComparison.Ordinal)),''',
'冲突检测类型别名'),
    (
'''        if (type == "色单双")
        {
            Match colorParity = Regex.Match(beforeOpening, "(?<color>[红蓝绿])(?:波)?(?<parity>[单双])");
            return colorParity.Success
                ? colorParity.Groups["color"].Value + colorParity.Groups["parity"].Value
                : null;
        }''',
'''        if (type is "色单双" or "半波")
        {
            Match colorParity = Regex.Match(beforeOpening, "(?<color>[红蓝绿])(?:波)?(?<parity>[单双])");
            return colorParity.Success
                ? colorParity.Groups["color"].Value + colorParity.Groups["parity"].Value
                : null;
        }''',
'通用类型别名'),
    (
'''            "色单双" => @"公子秒杀半波",''',
'''            "色单双" => @"公子秒杀半波",
            "半波" => @"(?:(?:红红半波|粉红半波|蓝黑半波)\\s*)+",''',
'严格前缀类型别名'),
    (
'''        if (rule.Type == "色单双")
            return Regex.IsMatch(value, "^[红蓝绿]波?[单双]$") ? value.Replace("波", "") : null;''',
'''        if (rule.Type is "色单双" or "半波")
            return Regex.IsMatch(value, "^[红蓝绿]波?[单双]$") ? value.Replace("波", "") : null;''',
'严格值类型别名'),
    (
'''        if (rule.Type == "色单双") return Regex.IsMatch(value, "^[红蓝绿][单双]$");''',
'''        if (rule.Type is "色单双" or "半波") return Regex.IsMatch(value, "^[红蓝绿][单双]$");''',
'格式化值类型别名'),
    (
'''        int scopeStart = 0;
        if (!string.IsNullOrWhiteSpace(rule.Section))
        {
            string section = Normalize(rule.Section);
            scopeStart = Array.FindIndex(lines, line => Normalize(line).Contains(section, StringComparison.Ordinal));
            if (scopeStart < 0)
                return null;
        }''',
'''        // 半波卡片允许“目标期行本身明确写出完整资料名”替代单独的 section 标题。
        // 只接受目标期行上的精确资料身份，不能借用上一期标题或同图其他半波资料。
        bool explicitHalfWaveIssueIdentity = rule.StrictIssueBlock
            && rule.Type == "半波"
            && lines.Any(line => ContainsIssue(line, issue)
                && HasExplicitHalfWaveIdentity(line, rule));
        int scopeStart = 0;
        if (!string.IsNullOrWhiteSpace(rule.Section) && !explicitHalfWaveIssueIdentity)
        {
            string section = Normalize(rule.Section);
            scopeStart = Array.FindIndex(lines, line => Normalize(line).Contains(section, StringComparison.Ordinal));
            if (scopeStart < 0)
                return null;
        }''',
'半波目标期精确身份作用域'),
    (
'''            if (!string.IsNullOrWhiteSpace(rule.Section)
                && !HasSectionForIssueRow(lines, index, scopeStart, issue, rule))
                continue;''',
'''            if (!string.IsNullOrWhiteSpace(rule.Section)
                && !HasExplicitHalfWaveIdentity(line, rule)
                && !HasSectionForIssueRow(lines, index, scopeStart, issue, rule))
                continue;''',
'半波目标期精确身份归属'),
    (
'''            if (!string.IsNullOrWhiteSpace(rule.Section)
                && !Normalize(block).Contains(Normalize(rule.Section), StringComparison.Ordinal))
                continue;''',
'''            if (!string.IsNullOrWhiteSpace(rule.Section)
                && !Normalize(block).Contains(Normalize(rule.Section), StringComparison.Ordinal)
                && !HasExplicitHalfWaveIdentity(block, rule))
                continue;''',
'半波严格区块归属'),
]

for old, new, label in replacements:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{label}: expected 1 occurrence, found {count}')
    text = text.replace(old, new, 1)

anchor = '''    private static bool HasSectionForIssueRow(
        string[] lines, int issueIndex, int scopeStart, int issue, OcrRule rule)
    {'''
helper = '''    private static bool HasExplicitHalfWaveIdentity(string text, OcrRule rule)
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

'''
if text.count(anchor) != 1:
    raise RuntimeError(f'半波精确身份辅助函数插入点数量异常: {text.count(anchor)}')
text = text.replace(anchor, helper + anchor, 1)

path.write_text(text, encoding='utf-8')
print('已应用半波类型兼容，并仅允许目标期行上的精确半波身份替代 section 标题')
