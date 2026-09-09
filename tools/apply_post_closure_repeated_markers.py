from pathlib import Path

path = Path('OcrLineTool.App/RuleEngine.cs')
text = path.read_text(encoding='utf-8')

old = '''        Match marker = rule.Id is "公式杀两肖肖" or "公式杀两尾尾" ? markers[^1] : markers[0];
        string payload = block[(marker.Index + marker.Length)..];'''
new = '''        bool formulaRule = rule.Id is "公式杀两肖肖" or "公式杀两尾尾";
        if (!formulaRule && markers.Count > 1)
        {
            var repeatedValues = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < markers.Count; index++)
            {
                Match repeatedMarker = markers[index];
                int start = repeatedMarker.Index + repeatedMarker.Length;
                int end = index + 1 < markers.Count ? markers[index + 1].Index : block.Length;
                string segment = block[start..end];
                string candidate = ExtractImmediateDragonflyValue(segment);
                if (candidate.Length == 0 || candidate == ConflictMarker)
                    return ConflictMarker;
                repeatedValues.Add(candidate);
            }
            if (repeatedValues.Count != 1)
                return ConflictMarker;
            return repeatedValues.Single();
        }

        Match marker = formulaRule ? markers[^1] : markers[0];
        string payload = block[(marker.Index + marker.Length)..];'''
if text.count(old) != 1:
    raise RuntimeError(f'repeated marker anchor mismatch: {text.count(old)}')
text = text.replace(old, new, 1)

anchor = '''    private static string ExtractHuangdaxianPayload(string block, OcrRule rule)
    {'''
helper = r'''    private static string ExtractImmediateDragonflyValue(string payload)
    {
        string framedPayload = Regex.Replace(payload, @"^[：:，,、\-\s]*", "");
        var leadingFrames = new List<string>();
        int frameOffset = 0;
        while (frameOffset < framedPayload.Length)
        {
            Match frame = Regex.Match(framedPayload[frameOffset..],
                @"^(?:【(?<value>[^】]+)】|\[(?<value>[^\]]+)\]|（(?<value>[^）]+)）|\((?<value>[^\)]+)\)|《(?<value>[^》]+)》|『(?<value>[^』]+)』|\{(?<value>[^}]+)\})");
            if (!frame.Success)
                break;
            leadingFrames.Add(frame.Groups["value"].Value);
            frameOffset += frame.Length;
            Match separator = Regex.Match(framedPayload[frameOffset..], @"^[：:，,、+\-\s]*");
            frameOffset += separator.Length;
        }
        if (leadingFrames.Count > 0)
        {
            string[] distinct = leadingFrames.Distinct(StringComparer.Ordinal).ToArray();
            return distinct.Length == 1 ? distinct[0] : ConflictMarker;
        }

        string unframed = Regex.Split(payload, @"发|發")[0].Trim();
        return unframed;
    }

'''
if text.count(anchor) != 1:
    raise RuntimeError(f'helper insertion anchor mismatch: {text.count(anchor)}')
text = text.replace(anchor, helper + anchor, 1)

path.write_text(text, encoding='utf-8')
print('Applied repeated strict-marker conflict handling')
