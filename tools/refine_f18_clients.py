from pathlib import Path

path = Path('OcrLineTool.App/OcrClients.cs')
text = path.read_text(encoding='utf-8')

def repl(old: str, new: str, count: int = 1) -> None:
    global text
    found = text.count(old)
    if found != count:
        raise RuntimeError(f'expected {count} anchors, found {found}: {old[:140]!r}')
    text = text.replace(old, new, count)

# Public parser helpers are legacy/test-facing string APIs. Preserve their old
# no-position result shape. Evidence-aware runtime calls still retain isolated
# unpositioned regions via ParseEvidenceItems.
old = '''    public static IReadOnlyList<string> ParseLines(string json) =>
        OcrEvidence.SafeLines(ParseEvidenceItems(json));
'''
new = '''    public static IReadOnlyList<string> ParseLines(string json)
    {
        IReadOnlyList<OcrLineEvidence> items = ParseEvidenceItems(json);
        return items.All(item => item.Box is null)
            ? items.Select(item => item.Text).ToArray()
            : OcrEvidence.SafeLines(items);
    }
'''
repl(old, new, count=2)

# HTTP status is transport truth. Do not try to parse a 503 HTML/body as OCR JSON.
repl(
'''            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            IReadOnlyList<OcrLineEvidence> items = ParseEvidenceItems(json);
            if (!response.IsSuccessStatusCode)
                throw new OcrException($"百度 OCR 请求失败（HTTP {(int)response.StatusCode}）。");
            OcrHttp.EnsureImageUnchanged(imagePath, bytes);''',
'''            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new OcrException($"百度 OCR 请求失败（HTTP {(int)response.StatusCode}）。");
            IReadOnlyList<OcrLineEvidence> items = ParseEvidenceItems(json);
            OcrHttp.EnsureImageUnchanged(imagePath, bytes);''')

path.write_text(text, encoding='utf-8')
print('Refined F18 client compatibility without weakening evidence runtime')
