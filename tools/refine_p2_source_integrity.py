from pathlib import Path

path = Path('OcrLineTool.App/OwnedResultWriter.cs')
text = path.read_text(encoding='utf-8')
old = '''internal static class OwnedResultWriter\n{\n    private sealed record OwnedLine(string SourceGroup, string Label, string Line);\n\n    internal static async Task<IReadOnlySet<string>> ApplyAsync(string targetPath, string sourceGroup,\n        string[] configuredLines, IReadOnlySet<string> revokedLabels, string? marker, bool blankLineBeforeMarker)\n'''
new = '''internal static class OwnedResultWriter\n{\n    private sealed record OwnedLine(string SourceGroup, string Label, string Line);\n\n    // Compatibility overload: callers that only add/update successful values\n    // retain the original behavior. Revocation is opt-in and used only when a\n    // same-issue conflict is explicitly propagated by ResultDistributor.\n    internal static Task<IReadOnlySet<string>> ApplyAsync(string targetPath, string sourceGroup,\n        string[] configuredLines, string? marker, bool blankLineBeforeMarker) =>\n        ApplyAsync(targetPath, sourceGroup, configuredLines,\n            new HashSet<string>(StringComparer.Ordinal), marker, blankLineBeforeMarker);\n\n    internal static async Task<IReadOnlySet<string>> ApplyAsync(string targetPath, string sourceGroup,\n        string[] configuredLines, IReadOnlySet<string> revokedLabels, string? marker, bool blankLineBeforeMarker)\n'''
if text.count(old) != 1:
    raise RuntimeError(f'Expected exactly one OwnedResultWriter signature anchor, found {text.count(old)}')
path.write_text(text.replace(old, new, 1), encoding='utf-8')
print('Added backward-compatible OwnedResultWriter overload')
