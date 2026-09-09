from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')


def repl(old, new, label, expected=1):
    global text
    count = text.count(old)
    if count != expected:
        raise RuntimeError(f'{label}: expected {expected}, found {count}')
    text = text.replace(old, new, expected)

old = '''            for (int index = 0; index < lines.Length; index++)
            {
                if (!ContainsIssue(lines[index], issue))
                    continue;
                string? split = ExtractReviewedSplitNumberWindow(
                    lines, 0, index, issue, reviewedExpectedCount);
                if (split is not null)
                    reviewed.Add(split);
            }'''
new = '''            for (int index = 0; index < lines.Length; index++)
            {
                if (!ContainsIssue(lines[index], issue))
                    continue;

                // The special adapter is only for a row that genuinely straddles
                // the issue cell: a numeric payload must already exist immediately
                // before this issue without crossing another issue. Ordinary
                // complete target blocks keep using the strict block validator.
                int previous = index - 1;
                while (previous >= 0 && IsOpeningOnlySeparator(lines[previous]))
                    previous--;
                if (previous < 0 || ContainsAnyIssue(lines[previous])
                    || !HasNumberPayload(BeforeOpeningResult(lines[previous])))
                    continue;

                string? split = ExtractReviewedSplitNumberWindow(
                    lines, 0, index, issue, reviewedExpectedCount);
                if (split is not null)
                    reviewed.Add(split);
            }'''
repl(old, new, 'strict reviewed split requires leading payload')

old = '''        parts.Add(BeforeOpeningResult(TextAfterIssue(lines[issueIndex], issue)));

        bool sawRightPayload = false;'''
new = '''        parts.Add(BeforeOpeningResult(TextAfterIssue(lines[issueIndex], issue)));
        string? alreadyComplete = ExtractNumbers(string.Join(' ', parts), expectedCount);
        if (alreadyComplete is not null)
            return alreadyComplete;

        bool sawRightPayload = false;'''
repl(old, new, 'reviewed split early complete before right')

old = '''            parts.Add(BeforeOpeningResult(lines[index]));
            sawRightPayload = true;
        }
        return ExtractNumbers(string.Join(' ', parts), expectedCount);'''
new = '''            parts.Add(BeforeOpeningResult(lines[index]));
            sawRightPayload = true;
            // Stop at the first complete reviewed physical row. A following
            // pure-number line may already be the next row even when it has no
            // title, so waiting for the next period can over-consume it.
            string? complete = ExtractNumbers(string.Join(' ', parts), expectedCount);
            if (complete is not null)
                return complete;
        }
        return ExtractNumbers(string.Join(' ', parts), expectedCount);'''
repl(old, new, 'reviewed split first complete row')

path.write_text(text, encoding='utf-8')
print('Refined reviewed split window')
