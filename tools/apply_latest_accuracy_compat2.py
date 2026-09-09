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
# hardened ownership predicate as continuation rows. Also preserve explicit
# issue-row number fields beginning with 杀/绝杀: these are reviewed production
# layouts and may be incomplete only because the final pair is on the next pure
# numeric row. Unknown labels do not satisfy this proof. A self-contained complete
# scoped field is also accepted for generic/legacy rows.
old = '''            var parts = new List<string>();
            string first = ScopeNumberPayload(TextAfterIssue(lines[index], issue), rule, expectedCount);
            if (!string.IsNullOrWhiteSpace(first))
                parts.Add(first);'''
new = '''            var parts = new List<string>();
            string firstText = TextAfterIssue(lines[index], issue);
            string first = ScopeNumberPayload(firstText, rule, expectedCount);
            string firstSimplified = SimplifyOcrText(firstText);
            bool explicitIssueField = Regex.IsMatch(firstSimplified,
                @"^\\s*(?:(?:杀|殺)(?:\\s*[:：]\\s*(?:杀|殺))?|(?:绝杀|絕殺)[一二三四五六七八九十0-9]*[码碼])");
            bool firstOwned = explicitIssueField
                || IsNumberContinuation(firstText, expectedCount, rule, issue)
                || ExtractNumbers(first, expectedCount) is not null;
            if (firstOwned && !string.IsNullOrWhiteSpace(first))
                parts.Add(first);'''
if text.count(old) != 1:
    raise RuntimeError(f'issue-row ownership anchor mismatch: {text.count(old)}')
text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8')
print('Applied explicit target-row number field compatibility refinements')