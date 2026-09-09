using System.Numerics;
using System.Text.Json;
using OcrLineTool;

// Read-only migration evidence: nearest titles are candidates, not verified references.
if (args.Length != 3)
    throw new ArgumentException("Expected template JSON, image folder, output JSON.");
var catalog = VisualTemplateMatcher.Load(args[0]);
var regions = catalog.Templates.Select(t => (
    Top: t.FingerprintTopWidthRatio ?? catalog.FingerprintTopWidthRatio ?? 0.1125,
    Bottom: t.FingerprintBottomWidthRatio ?? catalog.FingerprintBottomWidthRatio ?? 0.2125)).Distinct().ToArray();
var paths = Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories)
    .Where(p => new[] { ".jpg", ".jpeg", ".png", ".bmp" }.Contains(Path.GetExtension(p).ToLowerInvariant()))
    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
var hashes = new string[paths.Length][];
var errors = new string[paths.Length];
var timer = System.Diagnostics.Stopwatch.StartNew();
Parallel.For(0, paths.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
{
    try
    {
        hashes[i] = regions.Select(r => VisualTemplateMatcher.CreateFingerprint(paths[i], r.Top, r.Bottom)).ToArray();
    }
    catch (Exception e) when (e is ArgumentException or IOException or System.Runtime.InteropServices.ExternalException)
    {
        errors[i] = e.GetType().Name;
    }
});
int Distance(string a, string b) => Enumerable.Range(0, 16).Sum(i =>
    BitOperations.PopCount(Convert.ToUInt64(a.Substring(i * 16, 16), 16) ^ Convert.ToUInt64(b.Substring(i * 16, 16), 16)));
var entries = catalog.Templates.Select(t =>
{
    int region = Array.IndexOf(regions, (
        t.FingerprintTopWidthRatio ?? catalog.FingerprintTopWidthRatio ?? 0.1125,
        t.FingerprintBottomWidthRatio ?? catalog.FingerprintBottomWidthRatio ?? 0.2125));
    var candidates = paths.Select((p, i) => new { Path = p, Index = i })
        .Where(p => hashes[p.Index] != null)
        .Select(p => new { p.Path, Distance = Distance(t.Fingerprint, hashes[p.Index][region]) })
        .OrderBy(p => p.Distance).ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
    return new { t.Id, Candidates = candidates, Exact = candidates.Count(c => c.Distance == 0) };
}).ToArray();
var report = new { catalog.Folder, Images = paths.Length, Seconds = timer.Elapsed.TotalSeconds,
    Search = "zero shift only; nearest candidates require verification", Entries = entries,
    Errors = paths.Select((p, i) => new { Path = p, Error = errors[i] }).Where(e => e.Error != null) };
File.WriteAllText(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{catalog.Folder}: images={paths.Length}, templates={entries.Length}, exact={entries.Count(e => e.Exact > 0)}, seconds={timer.Elapsed.TotalSeconds:F1}");
