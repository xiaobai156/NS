from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

replacements = [
    (
        '!HasNumberPayload(BeforeOpeningResult(lines[previous]))',
        '!IsNumberContinuation(lines[previous], reviewedExpectedCount, rule)',
        'strict reviewed precheck', 1),
    (
        'if (!HasNumberPayload(immediate))\n            return null;',
        'if (!IsNumberContinuation(lines[previous], expectedCount, rule))\n            return null;',
        'strict centered immediate', 1),
    (
        'if (!HasNumberPayload(candidate))\n                break;',
        'if (!IsNumberContinuation(lines[index], expectedCount, rule))\n                break;',
        'strict centered backscan', 1),
    (
        '''            string candidate = BeforeOpeningResult(lines[index]);
            if (HasNumberPayload(candidate)
                || HasExactBracketPayload(candidate, expectedCount))
                parts.Add(candidate);''',
        '''            string candidate = BeforeOpeningResult(lines[index]);
            if (IsNumberContinuation(lines[index], expectedCount, rule))
                parts.Add(candidate);''',
        'reviewed preceding rows', 1),
]
for old, new, label, expected in replacements:
    actual = text.count(old)
    if actual != expected:
        raise RuntimeError(f'{label}: expected {expected}, found {actual}')
    text = text.replace(old, new, expected)

old_structural = '''            bool structuralField = Regex.IsMatch(decoration,
                @"^(?:开|開|禁|杀|殺|杀码|殺碼|不开|不開|精选杀|精選殺|码|碼|特码|特碼|码中特码|码中特碼)$");
            if (!ownIdentity && !structuralField)
                return false;
        }

        if (numbers.Length >= 2 || !hasLetters'''
new_structural = '''            bool structuralField = Regex.IsMatch(decoration,
                @"^(?:开|開|禁|杀|殺|杀码|殺碼|杀特码|殺特碼|不开|不開|精选杀|精選殺|码|碼|特码|特碼|码中特码|码中特碼|计|計|包围码|包圍碼|锁三十六码|鎖三十六碼|庄家必杀|莊家必殺)$");
            if (!ownIdentity && !structuralField)
                return false;
            // A proven field-start label may legitimately carry only the first
            // number; subsequent pure-number lines complete the same field.
            return true;
        }

        if (numbers.Length >= 2 || !hasLetters'''
if text.count(old_structural) != 1:
    raise RuntimeError(f'structural field block: expected 1, found {text.count(old_structural)}')
text = text.replace(old_structural, new_structural, 1)

# All value-producing reviewed number paths now use the rule-aware predicate.
# Remove the old broad helper so it cannot be reused accidentally.
old_helper = '''    private static bool HasNumberPayload(string line)
    {
        string[]? numbers = ParseNumbers(RemoveIssue(line));
        return numbers is not null && (numbers.Length >= 2
            || numbers.Length == 1 && (!Regex.IsMatch(line, @"\\p{L}")
                || line.Contains(':') || line.Contains('：') || line.Contains('←') || line.Contains('→')));
    }

'''
if text.count(old_helper) != 1:
    raise RuntimeError(f'old number payload helper: expected 1, found {text.count(old_helper)}')
text = text.replace(old_helper, '', 1)

path.write_text(text, encoding='utf-8')
print('Applied preceding number-field boundary patch')
