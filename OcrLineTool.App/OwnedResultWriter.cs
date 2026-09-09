using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OcrLineTool;

/// <summary>Updates only rows this tool previously wrote. Unknown same-label rows are never deleted.</summary>
internal static class OwnedResultWriter
{
    private sealed record OwnedLine(string SourceGroup, string Label, string Line);

    internal static async Task<IReadOnlySet<string>> ApplyAsync(string targetPath, string sourceGroup,
        string[] configuredLines, string? marker, bool blankLineBeforeMarker)
    {
        string ownerPath = targetPath + ".ocr-owners.json";
        try
        {
            // Cross-process lock for cooperating instances; a busy target is reported, not silently retried.
            using FileStream gate = new(targetPath + ".ocr-write.lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            byte[] original = await File.ReadAllBytesAsync(targetPath);
            byte[] originalHash = SHA256.HashData(original);
            var rows = (await File.ReadAllLinesAsync(targetPath)).ToList();
            List<OwnedLine> owners = File.Exists(ownerPath)
                ? JsonSerializer.Deserialize<List<OwnedLine>>(await File.ReadAllTextAsync(ownerPath))
                    ?? throw new OcrException("分流归属记录无效，未修改目标文件。")
                : [];
            var pending = new List<string>();
            bool modified = false;
            foreach (IGrouping<string, string> group in configuredLines.GroupBy(Label, StringComparer.Ordinal))
            {
                string[] distinct = group.Distinct(StringComparer.Ordinal).ToArray();
                if (distinct.Length != 1)
                    throw new OcrException($"分流结果冲突：{group.Key}。未修改目标文件。");
                string line = distinct[0];
                string label = group.Key;
                OwnedLine[] matchingOwners = owners.Where(item => item.SourceGroup == sourceGroup && item.Label == label).ToArray();
                if (matchingOwners.Length > 1)
                    throw new OcrException("分流归属记录重复，未修改目标文件。");
                OwnedLine? owned = matchingOwners.SingleOrDefault();
                int[] sameLabel = Enumerable.Range(0, rows.Count).Where(index => Label(rows[index]) == label).ToArray();
                if (owned is not null && owned.Line != line && sameLabel.Length == 1 && rows[sameLabel[0]] == owned.Line
                    && !owners.Any(item => item.SourceGroup != sourceGroup && item.Label == label))
                {
                    rows[sameLabel[0]] = line;
                    owners.Remove(owned);
                    owners.Add(new(sourceGroup, label, line));
                    modified = true;
                    continue;
                }
                if (sameLabel.Any(index => rows[index] != line))
                    throw new OcrException($"分流目标存在不同值且归属不确定：{label}。保留原文件，请核对。");
                if (rows.Contains(line, StringComparer.Ordinal))
                    continue; // Idempotent, but do not claim ownership of someone else's existing row.
                pending.Add(line);
                if (owned is not null) owners.Remove(owned);
                owners.Add(new(sourceGroup, label, line));
                modified = true;
            }
            if (!modified) return configuredLines.ToHashSet(StringComparer.Ordinal);
            int markerIndex = marker is null ? -1 : rows.FindIndex(line => line == marker);
            if (marker is not null && markerIndex < 0)
                markerIndex = rows.FindIndex(line =>
                {
                    string compact = string.Concat(line.Where(c => !char.IsWhiteSpace(c)));
                    return compact.Contains("内容") && compact.Contains("次数") && compact.Contains("排名");
                });
            if (pending.Count > 0 && markerIndex >= 0)
            {
                while (markerIndex > 0 && string.IsNullOrWhiteSpace(rows[markerIndex - 1]))
                    rows.RemoveAt(--markerIndex);
                rows.InsertRange(markerIndex, pending);
                if (blankLineBeforeMarker) rows.Insert(markerIndex + pending.Count, string.Empty);
            }
            else rows.AddRange(pending);

            // Detect edits by non-cooperating tools before replacement. Such tools should share the lock protocol.
            if (!SHA256.HashData(await File.ReadAllBytesAsync(targetPath)).SequenceEqual(originalHash))
                throw new OcrException("分流目标在写入前被其他程序修改，请重试。");
            Encoding encoding = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF
                ? new UTF8Encoding(true)
                : original.Length >= 2 && original[0] == 0xFF && original[1] == 0xFE ? Encoding.Unicode
                : original.Length >= 2 && original[0] == 0xFE && original[1] == 0xFF ? Encoding.BigEndianUnicode
                : new UTF8Encoding(false);
            await AtomicFile.WriteAllLinesAsync(targetPath, rows, encoding);
            // Separate atomic files, not a multi-file transaction. Failure here is reported, never marked distributed.
            await AtomicFile.WriteAllTextAsync(ownerPath, JsonSerializer.Serialize(owners));
            return configuredLines.ToHashSet(StringComparer.Ordinal);
        }
        catch (OcrException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new OcrException($"分流目标忙、不可写或归属记录损坏：{Path.GetFileName(targetPath)}。请检查后重试。");
        }
    }

    private static string Label(string line)
    {
        if (line.StartsWith("缺失", StringComparison.Ordinal)) return string.Empty;
        int separator = line.LastIndexOf(' ');
        return separator >= 0 ? line[(separator + 1)..] : string.Empty;
    }
}
