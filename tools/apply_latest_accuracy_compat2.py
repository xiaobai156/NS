from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

# A complete bracket is acceptable with an explicit number-field marker, even
# when OCR leaves unrelated result/forum text after the bracket. A generic label
# such as “其他栏目【...】” is still not ownership proof.
old = '''        if (!ownIdentity && !structuralField)
            return false;

        // Brackets prove value grouping only after the line itself has been
        // proven to belong to this field; a foreign bracket cannot claim it.
        if (HasExactBracketPayload(scoped, expectedCount))
            return true;'''
new = '''        bool explicitBracketField = HasExactBracketPayload(simplified, expectedCount)
            && Regex.IsMatch(simplified,
                @"(?:杀码|殺碼|杀码|杀(?:特)?码|殺(?:特)?碼|绝杀|絕殺|禁码|禁碼)[^【\\[]*[【\\[]");
        if (!ownIdentity && !structuralField && !explicitBracketField)
            return false;

        // Brackets prove value grouping only after the line itself has been
        // proven to belong to this field; a foreign bracket cannot claim it.
        if (HasExactBracketPayload(scoped, expectedCount)
            || explicitBracketField)
            return true;'''
if text.count(old) != 1:
    raise RuntimeError(f'bracket ownership anchor mismatch: {text.count(old)}')
text = text.replace(old, new, 1)

# Strip an opening/result cell before number scoping. Otherwise “兔40中” can be
# truncated to “兔40” by field scoping and the result number 40 becomes data.
old = '''        string centre = ScopeNumberPayload(TextAfterIssue(lines[issueIndex], issue), rule, expectedCount);
        centre = Regex.Split(centre,
            $@"(?<!不会)(?<!不)开|准|準|[{Zodiac}]\\s*\\d{{1,2}}\\s*[中错錯赢贏]")[0];'''
new = '''        string centreText = SimplifyOcrText(TextAfterIssue(lines[issueIndex], issue));
        centreText = Regex.Split(centreText,
            $@"(?<!不会)(?<!不)开|准|準|[{Zodiac}]\\s*\\d{{1,2}}\\s*[中错錯赢贏]")[0];
        string centre = ScopeNumberPayload(centreText, rule, expectedCount);'''
if text.count(old) != 1:
    raise RuntimeError(f'centre result anchor mismatch: {text.count(old)}')
text = text.replace(old, new, 1)

# The selected issue row is not automatically a data field. Reuse the same
# hardened ownership predicate as continuation rows. Additionally, when the
# scoped target row by itself already forms one complete valid field, accept it:
# this preserves generic/legacy rows such as pure numbers, concatenated pairs and
# zodiac-number annotations without reopening cross-field completion. A row such
# as “青苹果 ... 01..05 其他栏目06” is scoped to five values first, so it still
# cannot become a six-number success.
old = '''            var parts = new List<string>();
            string first = ScopeNumberPayload(TextAfterIssue(lines[index], issue), rule, expectedCount);
            if (!string.IsNullOrWhiteSpace(first))
                parts.Add(first);'''
new = '''            var parts = new List<string>();
            string firstText = TextAfterIssue(lines[index], issue);
            string first = ScopeNumberPayload(firstText, rule, expectedCount);
            bool firstOwned = IsNumberContinuation(firstText, expectedCount, rule, issue)
                || ExtractNumbers(first, expectedCount) is not null;
            if (firstOwned && !string.IsNullOrWhiteSpace(first))
                parts.Add(first);'''
if text.count(old) != 1:
    raise RuntimeError(f'issue-row ownership anchor mismatch: {text.count(old)}')
text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8')
print('Applied final target-row and bracket compatibility refinements')