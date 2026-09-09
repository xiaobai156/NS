from pathlib import Path

path = Path('tools/apply_residual_contract_stage_c.py')
text = path.read_text(encoding='utf-8')

old = '''text = repl(text,
''' + "'''" + '''        if (observed.Count > 0)
            return observed.Count == 1 ? observed.Single() : null;''' + "'''" + ''',
''' + "'''" + '''        if (observed.Count > 0)
            return observed.Count == 1 ? observed.Single() : ConflictMarker;''' + "'''" + ''',
"generic observed conflict")'''
new = '''text = repl(text,
''' + "'''" + '''        if (observed.Count > 0)
            return observed.Count == 0 ? null : observed.Count == 1 ? observed.Single() : ConflictMarker;''' + "'''" + ''',
''' + "'''" + '''        if (observed.Count > 0)
            return observed.Count == 1 ? observed.Single() : ConflictMarker;''' + "'''" + ''',
"generic observed conflict")'''
if old not in text:
    raise RuntimeError('stage C generic conflict patch anchor not found')
text = text.replace(old, new, 1)

old = '''                HashSet<string> own = new[] { rule.Keyword, rule.Label ?? string.Empty }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToHashSet(StringComparer.Ordinal);
                string[] peers = output
                    .Where(other => other.Id != rule.Id
                        && string.Equals(other.Folder, rule.Folder, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(other => new[] { other.Keyword, other.Label ?? string.Empty })'''
new = '''                HashSet<string> own = new[]
                {
                    rule.Keyword, rule.Label ?? string.Empty, rule.Section ?? string.Empty,
                    rule.RequiredKeyword ?? string.Empty
                }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToHashSet(StringComparer.Ordinal);
                string[] peers = output
                    .Where(other => other.Id != rule.Id
                        && string.Equals(other.Folder, rule.Folder, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(other => new[]
                    {
                        other.Keyword, other.Label ?? string.Empty, other.Section ?? string.Empty,
                        other.RequiredKeyword ?? string.Empty
                    })'''
if old not in text:
    raise RuntimeError('stage C peer identity patch anchor not found')
text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8')
print('Fixed stage C patch script ordering and peer section boundaries')
