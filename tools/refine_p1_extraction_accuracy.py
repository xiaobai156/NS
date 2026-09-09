from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')
old = '''            MatchCollection zodiacs = Regex.Matches(field, $"[{Zodiac}]");
            if (zodiacs.Count != 9)
                return null;
            string value = string.Concat(zodiacs.Select(match => match.Value));
            return value.Distinct().Count() == 9 ? value : null;
'''
new = '''            MatchCollection zodiacs = Regex.Matches(field, $"[{Zodiac}]");
            if (zodiacs.Count != 9)
                return null;
            string nineValue = string.Concat(zodiacs.Select(match => match.Value));
            return nineValue.Distinct().Count() == 9 ? nineValue : null;
'''
if text.count(old) != 1:
    raise RuntimeError(f'Expected one Xiaosaohuo strict block, found {text.count(old)}')
path.write_text(text.replace(old, new, 1), encoding='utf-8')
print('Refined P1 local variable name')
