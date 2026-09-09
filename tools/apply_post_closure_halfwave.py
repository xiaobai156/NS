from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

replacements = [
    (
'''            "色单双" => Regex.Matches(text, @"[红蓝绿](?:波)?[单双]")
                .Select(match => match.Value.Replace("波", "", StringComparison.Ordinal)),''',
'''            "色单双" or "半波" => Regex.Matches(text, @"[红蓝绿](?:波)?[单双]")
                .Select(match => match.Value.Replace("波", "", StringComparison.Ordinal)),''',
'conflict type alias'),
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
'generic type alias'),
    (
'''            "色单双" => @"公子秒杀半波",''',
'''            "色单双" => @"公子秒杀半波",
            "半波" => @"(?:红红半波|粉红半波|蓝黑半波)",''',
'strict prefix alias'),
    (
'''        if (rule.Type == "色单双")
            return Regex.IsMatch(value, "^[红蓝绿]波?[单双]$") ? value.Replace("波", "") : null;''',
'''        if (rule.Type is "色单双" or "半波")
            return Regex.IsMatch(value, "^[红蓝绿]波?[单双]$") ? value.Replace("波", "") : null;''',
'strict value alias'),
    (
'''        if (rule.Type == "色单双") return Regex.IsMatch(value, "^[红蓝绿][单双]$");''',
'''        if (rule.Type is "色单双" or "半波") return Regex.IsMatch(value, "^[红蓝绿][单双]$");''',
'formatted value alias'),
]

for old, new, label in replacements:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{label}: expected 1 occurrence, found {count}')
    text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8')
print('Applied half-wave alias using the existing color-parity contract')
