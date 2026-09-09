from pathlib import Path

path = Path('OcrLineTool.App/PaddleLocalOcrClient.cs')
text = path.read_text(encoding='utf-8')

def repl(old: str, new: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'expected one anchor, found {count}: {old[:160]!r}')
    text = text.replace(old, new, 1)

repl(
'''    private readonly Dictionary<string, string> imageErrors = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> LastImageErrors => imageErrors;
''',
'''    private readonly Dictionary<string, string> imageErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OcrEvidence> imageEvidence = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> LastImageErrors => imageErrors;
    public IReadOnlyDictionary<string, OcrEvidence> LastEvidence => imageEvidence;
''')

repl(
'''        imageErrors.Clear();
        cancellationToken.ThrowIfCancellationRequested();''',
'''        imageErrors.Clear();
        imageEvidence.Clear();
        cancellationToken.ThrowIfCancellationRequested();''')

repl(
'''            if (cache.TryGetValue(key, out CacheEntry? entry))
            {
                results[path] = entry.Texts;
                cached.Add(path);
            }''',
'''            if (cache.TryGetValue(key, out CacheEntry? entry))
            {
                results[path] = entry.Texts;
                imageEvidence[path] = BuildEvidence(path, entry.Texts, entry.Items);
                cached.Add(path);
            }''')

repl(
'''                        string[] texts = result.Texts ?? [];
                        results[result.Path] = texts;
                        // Empty results are not durable success-cache entries.
                        if (texts.Any(line => !string.IsNullOrWhiteSpace(line)))
                            cache[key] = new CacheEntry(key, texts);''',
'''                        string[] texts = result.Texts ?? [];
                        PaddleItem[] items = result.Items ?? [];
                        results[result.Path] = texts;
                        imageEvidence[result.Path] = BuildEvidence(result.Path, texts, items);
                        // Empty results are not durable success-cache entries.
                        if (texts.Any(line => !string.IsNullOrWhiteSpace(line)))
                            cache[key] = new CacheEntry(key, texts, items);''')

# Insert evidence conversion before cache read helpers.
repl(
'''    private async Task<Dictionary<string, CacheEntry>> ReadCacheAsync(CancellationToken cancellationToken)
    {''',
'''    private static OcrEvidence BuildEvidence(string path, string[] texts, PaddleItem[]? items)
    {
        var raw = new List<OcrLineEvidence>();
        if (items is { Length: > 0 })
        {
            foreach (PaddleItem item in items.Where(item => !string.IsNullOrWhiteSpace(item.Text)))
            {
                OcrBox? box = item.Box is { Length: >= 4 }
                    ? new OcrBox(item.Box[0], item.Box[1], Math.Max(0, item.Box[2]), Math.Max(0, item.Box[3]))
                    : null;
                raw.Add(new(
                    item.Text,
                    box,
                    item.Confidence,
                    string.IsNullOrWhiteSpace(item.ViewId) ? "paddle" : "paddle/" + item.ViewId,
                    box is null ? "unpositioned" : "main"));
            }
        }
        else
        {
            raw.AddRange(texts.Where(line => !string.IsNullOrWhiteSpace(line))
                .Select((line, index) => new OcrLineEvidence(line, null, null, "paddle/legacy", $"unpositioned-{index}")));
        }

        IReadOnlyList<OcrLineEvidence> partitioned = OcrEvidenceLayout.Partition(raw);
        string hash = LocalOcrIdentity.Image(path);
        return new OcrEvidence(path, path, hash, hash, "paddle", partitioned);
    }

    private async Task<Dictionary<string, CacheEntry>> ReadCacheAsync(CancellationToken cancellationToken)
    {''')

repl(
'''    private sealed record PaddleResponse(List<PaddleResult>? Results, string? Error);
    private sealed record PaddleResult(string Path, string[]? Texts, string? Error);
    private sealed record CacheEntry(string Key, string[] Texts);
}''',
'''    private sealed record PaddleResponse(List<PaddleResult>? Results, string? Error);
    private sealed record PaddleResult(string Path, string[]? Texts, PaddleItem[]? Items, string? Error);
    private sealed record PaddleItem(string Text, double? Confidence, int[]? Box, string? ViewId);
    private sealed record CacheEntry(string Key, string[] Texts, PaddleItem[]? Items = null);
}''')

path.write_text(text, encoding='utf-8')
print('Applied Paddle structured evidence bridge')
