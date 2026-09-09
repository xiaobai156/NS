using OcrLineTool;

if (args.Length == 0)
    return;

DateOnly today = CredentialSchedule.TodayInBeijing();
int issue = CredentialSchedule.IssueForDate(today);
int previousIssue = Math.Max(1, issue - 1);
OcrCredential credential = CredentialSchedule.Today();
IOcrClient client = OcrClientFactory.Create(credential);
Console.WriteLine($"Provider: {credential.DisplayName}");
foreach (string path in args)
{
    IReadOnlyList<string> lines = await client.RecognizeAsync(path);
    Console.WriteLine($"Image: {Path.GetFileName(path)}");
    Console.WriteLine($"Result ({issue}期): {LineExtractor.Extract(lines, issue) ?? "NOT FOUND"}");
    Console.WriteLine("Relevant OCR lines:");
    foreach (string line in lines.Where(x =>
        x.Contains($"{issue}期", StringComparison.Ordinal) ||
        x.Contains($"{previousIssue}期", StringComparison.Ordinal)))
        Console.WriteLine(line);
    Console.WriteLine("All OCR lines with indexes:");
    for (int index = 0; index < lines.Count; index++)
        Console.WriteLine($"[{index}] {lines[index]}");
}
