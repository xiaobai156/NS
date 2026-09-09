from pathlib import Path
import re

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

# 1) Do not treat instruction heads (e.g. 禁止1头 / 杀一头) as a second result.
old = '''            "头" => Regex.Matches(text, @"(?<!\\d)[0-4零一二三四]\\s*头")
                .Select(match => $"{ToArabicDigit(match.Value.First(c => c is >= '0' and <= '4' or '零' or '一' or '二' or '三' or '四'))}头"),'''
new = '''            // Head cards often contain an instruction count ("杀一头" / "禁止1头")
            // before the actual value. Existing head extraction already resolves the
            // value field; do not reinterpret the instruction as a conflicting result.
            "头" => Array.Empty<string>(),'''
if text.count(old) != 1:
    raise RuntimeError(f'head conflict anchor mismatch: {text.count(old)}')
text = text.replace(old, new, 1)

# 2) Replace ScopeNumberPayload with count-aware field scoping.
pattern = re.compile(r'''    private static string ScopeNumberPayload\(string line, OcrRule rule\)\n    \{.*?\n    \}\n\n    private static bool IsNumberContinuation''', re.S)
match = pattern.search(text)
if not match:
    raise RuntimeError('ScopeNumberPayload method not found')
replacement = r'''    private static string ScopeNumberPayload(
        string line, OcrRule rule, int expectedCount = 0)
    {
        string text = BeforeOpeningResult(SimplifyOcrText(RemoveIssue(line)));

        // Printed cardinalities are layout metadata, not lottery numbers.
        // Examples: 35码赛马会, 36计, 12码→, [12个特码].
        text = Regex.Replace(text,
            @"(?<!\d)\d{1,2}\s*(?:个(?:中特码|特码)?|個(?:码中特碼|特碼)?|码|碼|计|計)",
            " ");

        // If OCR left title noise (including 100/888 etc.) before a complete
        // bracketed field, prefer the bracket only when its own cardinality is
        // exactly the configured rule count. This does not make a foreign bracket
        // valid: IsNumberContinuation still proves ownership before accepting it.
        if (expectedCount > 0)
        {
            string[] exact = Regex.Matches(text,
                    @"[【\[](?<payload>[^】\]]+)[】\]]")
                .Select(match => match.Groups["payload"].Value)
                .Where(payload =>
                {
                    string[]? parsed = ParseNumbers(payload);
                    return parsed is not null && parsed.Length == expectedCount
                        && parsed.Distinct(StringComparer.Ordinal).Count() == expectedCount;
                })
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (exact.Length == 1)
                return exact[0];
            // More than one complete bracket is deliberately left intact so the
            // normal complete-field conflict checks can reject ambiguity.
        }

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

    private static bool IsNumberContinuation'''
text = text[:match.start()] + replacement + text[match.end():]

# 3) Pass expectedCount into every number payload scoping call in the hardened
# number-provenance block. Avoid changing unrelated methods.
text = re.sub(r'ScopeNumberPayload\(([^;\n]+?), rule\)', r'ScopeNumberPayload(\1, rule, expectedCount)', text)
# Restore the method declaration if the broad substitution touched it (normally it cannot).
text = text.replace('ScopeNumberPayload(string line, OcrRule rule, int expectedCount = 0, expectedCount)',
                    'ScopeNumberPayload(string line, OcrRule rule, int expectedCount = 0)')

# 4) A reviewed 35/36-code centred table may legitimately put the next row's
# unlabeled left cell after the previous row's opening/result separator. That
# layout proof is specific to large reviewed tables; do not enable it for 10-code
# kill material (the F01 cross-period regression).
old_sig = '''    private static bool HasCurrentFieldStartAfterEarlierIssue(
        string[] lines, int scopeStart, int issueIndex, OcrRule rule)'''
new_sig = '''    private static bool HasCurrentFieldStartAfterEarlierIssue(
        string[] lines, int scopeStart, int issueIndex, OcrRule rule, int expectedCount)'''
if text.count(old_sig) != 1:
    raise RuntimeError(f'field-start signature mismatch: {text.count(old_sig)}')
text = text.replace(old_sig, new_sig, 1)

old_tail = '''        for (int index = previousIssue + 1; index < issueIndex; index++)
        {
            if (IsNumberRowStart(lines[index]) || ContainsOwnNumberIdentity(lines[index], rule))
                return true;
        }
        return false;
    }'''
new_tail = '''        for (int index = previousIssue + 1; index < issueIndex; index++)
        {
            if (IsNumberRowStart(lines[index]) || ContainsOwnNumberIdentity(lines[index], rule))
                return true;
        }

        if (expectedCount >= 35
            && Enumerable.Range(previousIssue + 1, issueIndex - previousIssue - 1)
                .Any(index => IsOpeningOnlySeparator(lines[index])))
        {
            int immediate = issueIndex - 1;
            if (immediate > previousIssue
                && !ContainsIssueBoundary(lines[immediate], 0)
                && IsNumberContinuation(lines[immediate], expectedCount, rule))
                return true;
        }
        return false;
    }'''
if text.count(old_tail) != 1:
    raise RuntimeError(f'field-start tail mismatch: {text.count(old_tail)}')
text = text.replace(old_tail, new_tail, 1)

text = text.replace(
    'HasCurrentFieldStartAfterEarlierIssue(lines, 0, issueIndex, rule)',
    'HasCurrentFieldStartAfterEarlierIssue(lines, 0, issueIndex, rule, expectedCount)')
text = text.replace(
    'HasCurrentFieldStartAfterEarlierIssue(lines, scopeStart, issueIndex, rule)',
    'HasCurrentFieldStartAfterEarlierIssue(lines, scopeStart, issueIndex, rule, expectedCount)')

# The selectedIssue=0 helper call above must not classify any numeric line by
# proximity; IsNumberContinuation itself will validate shape. Simplify the guard.
text = text.replace(
    '''                && !ContainsIssueBoundary(lines[immediate], 0)
                && IsNumberContinuation(lines[immediate], expectedCount, rule))''',
    '''                && !ContainsAnyIssue(lines[immediate])
                && IsNumberContinuation(lines[immediate], expectedCount, rule))''')

path.write_text(text, encoding='utf-8')
print('Applied safe compatibility refinements')
