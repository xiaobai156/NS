from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

old = '''        bool hasLetters = Regex.IsMatch(line, @"\\p{L}");
        if (!hasLetters)
            return true;

        string normalized = Normalize(line);
        bool ownIdentity = ContainsOwnNumberIdentity(line, rule);
        string decoration = Regex.Replace(
            simplified, @"[0-9\\s,，.。:：*【】\\[\\]()（）?？←→]+", string.Empty);
        bool structuralField = Regex.IsMatch(decoration,
            @"^(?:开|開|禁|杀|殺|杀码|殺碼|杀特码|殺特碼|不开|不開|精选杀|精選殺|码|碼|特码|特碼|码中特码|码中特碼|计|計|包围码|包圍碼|锁三十六码|鎖三十六碼|庄家必杀|莊家必殺)$");'''
new = '''        // Opening/result suffixes are not part of field ownership. Evaluate the
        // decoration on the data side of “开”, so “杀02...开??” remains a proven
        // kill-number field and “48开888” remains a pure numeric continuation.
        string ownershipText = BeforeOpeningResult(simplified);
        bool hasLetters = Regex.IsMatch(ownershipText, @"\\p{L}");
        if (!hasLetters)
            return true;

        string normalized = Normalize(line);
        bool ownIdentity = ContainsOwnNumberIdentity(line, rule);
        string decoration = Regex.Replace(
            ownershipText, @"[0-9\\s,，.。:：*【】\\[\\]()（）?？←→]+", string.Empty);
        bool structuralField = Regex.IsMatch(decoration,
            @"^(?:(?:杀|殺){1,3}|开|開|禁|杀码|殺碼|杀特码|殺特碼|不开|不開|精选杀|精選殺|码|碼|特码|特碼|码中特码|码中特碼|计|計|包围码|包圍碼|锁三十六码|鎖三十六碼|庄家必杀|莊家必殺|绝杀[一二三四五六七八九十0-9]+码|絕殺[一二三四五六七八九十0-9]+碼|封杀|封殺)$");'''
count = text.count(old)
if count != 1:
    raise RuntimeError(f'number field decoration anchor mismatch: {count}')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')
print('Applied proven number-field continuation compatibility')
