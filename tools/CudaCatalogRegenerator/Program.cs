using System.Text.Json;
using OcrLineTool;

if (args.Length != 4)
    throw new ArgumentException("catalog refs baseline output");
var catalogPath = args[0];
var refs = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(args[1]));
var baseline = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(args[2]));
var decoded = baseline.EnumerateArray().ToDictionary(
    item => item.GetProperty("Path").GetString()!,
    item => item.GetProperty("Decoded").GetString()!, StringComparer.OrdinalIgnoreCase);
var referencePaths = refs.GetProperty("Entries").EnumerateArray().ToDictionary(
    item => item.GetProperty("Id").GetString()!,
    item => item.GetProperty("Candidates")[0].GetProperty("Path").GetString()!, StringComparer.Ordinal);
var catalog = JsonSerializer.Deserialize<VisualTemplateSet>(
    File.ReadAllText(catalogPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
var output = catalog with
{
    Templates = catalog.Templates.Select(template =>
    {
        string path = decoded[referencePaths[template.Id]];
        double top = template.FingerprintTopWidthRatio ?? catalog.FingerprintTopWidthRatio ?? .1125;
        double bottom = template.FingerprintBottomWidthRatio ?? catalog.FingerprintBottomWidthRatio ?? .2125;
        return template with { Fingerprint = VisualTemplateMatcher.CreateFingerprint(path, top, bottom) };
    }).ToList()
};
File.WriteAllText(args[3], JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
