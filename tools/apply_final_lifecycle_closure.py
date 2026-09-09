from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected 1 occurrence, found {count}")
    return text.replace(old, new, 1)

# ---------------------------------------------------------------------------
# ResultFilePaths: stable evidence snapshot directory.
# ---------------------------------------------------------------------------
path = Path('OcrLineTool.App/ResultFilePaths.cs')
text = path.read_text(encoding='utf-8')
old = '''    public static string RecognitionStateDirectory(string appDirectory) =>
        Path.Combine(ImportantResultsDirectory(appDirectory), "识别状态");

    public static string ForRecognitionState(string appDirectory, string selectedDirectory, int issue) =>'''
new = '''    public static string RecognitionStateDirectory(string appDirectory) =>
        Path.Combine(ImportantResultsDirectory(appDirectory), "识别状态");

    public static string RecognitionEvidenceDirectory(
        string appDirectory, string groupName, int issue) =>
        Path.Combine(RecognitionStateDirectory(appDirectory), "证据", $"{groupName}_{issue}期");

    public static string ForRecognitionState(string appDirectory, string selectedDirectory, int issue) =>'''
text = replace_once(text, old, new, 'recognition evidence path')
path.write_text(text, encoding='utf-8')

# ---------------------------------------------------------------------------
# RecognitionStateStore: persist temporary input views and tolerant publishing.
# ---------------------------------------------------------------------------
path = Path('OcrLineTool.App/RecognitionStateStore.cs')
text = path.read_text(encoding='utf-8')

old = '''            if (!values.TryGetValue(rule.Id, out string? value) ||
                record.Status != "success" || record.Value != value ||
                !RuleEngine.IsCanonicalValueValid(rule, value))
                continue;
            results.Add(record);'''
new = '''            if (!values.TryGetValue(rule.Id, out string? value) ||
                record.Status != "success" || record.Value != value ||
                !RuleEngine.IsCanonicalValueValid(rule, value))
                continue;

            // A successful OCR view may live in a temporary crop directory that
            // MainForm deletes when the run ends. Persist the exact input bytes
            // before writing trusted state, while retaining the original source
            // path/hash as the primary provenance check.
            record = PersistInputSnapshot(appDirectory, group, issue, record);
            evidence.Seed(record);
            results.Add(record);'''
text = replace_once(text, old, new, 'persist successful input')

anchor = '''    internal static IDisposable LockCurrentEvidenceForPublish(
        IReadOnlyList<OcrRule> rules,'''
if text.count(anchor) != 1:
    raise RuntimeError('lock method anchor mismatch')
helpers = r'''    private static ResultEvidenceRecord PersistInputSnapshot(
        string appDirectory,
        string group,
        int issue,
        ResultEvidenceRecord record)
    {
        try
        {
            string source = Path.GetFullPath(record.SourcePath);
            string input = Path.GetFullPath(record.InputPath);
            if (!File.Exists(source)
                || !LocalOcrIdentity.Image(source).Equals(record.SourceHash, StringComparison.OrdinalIgnoreCase))
                throw new OcrException(
                    "识别来源在状态保存前发生变化，请重新识别。",
                    "OCR_IMAGE_CHANGED");

            if (source.Equals(input, StringComparison.OrdinalIgnoreCase))
                return record with { SourcePath = source, InputPath = input };
            if (!File.Exists(input)
                || !LocalOcrIdentity.Image(input).Equals(record.InputHash, StringComparison.OrdinalIgnoreCase))
                throw new OcrException(
                    "识别裁剪在状态保存前发生变化，请重新识别。",
                    "OCR_IMAGE_CHANGED");

            string directory = ResultFilePaths.RecognitionEvidenceDirectory(
                appDirectory, group, issue);
            Directory.CreateDirectory(directory);
            string extension = Path.GetExtension(input);
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
                extension = ".img";
            string destination = Path.Combine(directory,
                record.InputHash.ToLowerInvariant() + extension.ToLowerInvariant());

            if (!File.Exists(destination)
                || !LocalOcrIdentity.Image(destination).Equals(record.InputHash, StringComparison.OrdinalIgnoreCase))
            {
                string staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(input, staging, overwrite: true);
                    if (!LocalOcrIdentity.Image(staging).Equals(record.InputHash, StringComparison.OrdinalIgnoreCase))
                        throw new OcrException(
                            "识别裁剪在持久化时发生变化，请重新识别。",
                            "OCR_IMAGE_CHANGED");
                    File.Move(staging, destination, overwrite: true);
                }
                finally
                {
                    try { if (File.Exists(staging)) File.Delete(staging); } catch { }
                }
            }
            return record with { SourcePath = source, InputPath = destination };
        }
        catch (OcrException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new OcrException(
                "无法持久化识别裁剪，本次结果不会作为可恢复成功值。",
                "OCR_STATE_WRITE_ERROR");
        }
    }

    /// <summary>
    /// Automatic publishing is per-rule fail-closed: an invalid success is
    /// downgraded to missing, but a different rule's already-known conflict is
    /// preserved so it can be saved and revoke an owned stale output.
    /// </summary>
    internal static IDisposable LockValidEvidenceForPublish(
        IReadOnlyList<OcrRule> rules,
        ResultValues values,
        ResultEvidenceLedger evidence,
        IDictionary<string, string> missingReasons)
    {
        var handles = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        var expectedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (OcrRule rule in rules)
        {
            if (ResultValues.IsConflict(values, rule.Id) || !values.ContainsKey(rule.Id))
                continue;

            if (!evidence.Records.TryGetValue(rule.Id, out ResultEvidenceRecord? record)
                || record.Status != "success"
                || record.RuleSignature != RuleSignature(rule)
                || string.IsNullOrWhiteSpace(record.SourcePath)
                || string.IsNullOrWhiteSpace(record.InputPath)
                || string.IsNullOrWhiteSpace(record.SourceHash)
                || string.IsNullOrWhiteSpace(record.InputHash)
                || !TryLock(record.SourcePath, record.SourceHash)
                || !TryLock(record.InputPath, record.InputHash))
            {
                values.Remove(rule.Id);
                evidence.Remove(rule.Id);
                missingReasons[rule.Id] = "图片或识别视图在发布前发生变化";
            }
        }
        return new EvidencePublishLock(handles.Values.ToArray());

        bool TryLock(string path, string expectedHash)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (expectedHashes.TryGetValue(fullPath, out string? existingHash))
                    return existingHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);

                FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                string currentHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(stream));
                stream.Position = 0;
                if (!currentHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    stream.Dispose();
                    return false;
                }
                handles.Add(fullPath, stream);
                expectedHashes.Add(fullPath, expectedHash);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        }
    }

'''
text = text.replace(anchor, helpers + anchor, 1)
path.write_text(text, encoding='utf-8')

# ---------------------------------------------------------------------------
# MainForm: pin candidate identity at creation; use it at all OCR requests;
# automatic publish prunes invalid successes without swallowing conflicts.
# ---------------------------------------------------------------------------
path = Path('OcrLineTool.App/MainForm.cs')
text = path.read_text(encoding='utf-8')

old_record = '''    private sealed record RecognitionCandidate(
        string SourcePath,
        string OcrPath,
        IReadOnlyList<OcrRule> Rules,
        bool IsPrimary,
        string SelectionMode,
        int? TemplateDistance,
        IReadOnlyList<string>? LocalLines = null,
        OcrEvidence? LocalEvidence = null);'''
new_record = '''    private sealed record RecognitionCandidate(
        string SourcePath,
        string OcrPath,
        IReadOnlyList<OcrRule> Rules,
        bool IsPrimary,
        string SelectionMode,
        int? TemplateDistance,
        IReadOnlyList<string>? LocalLines = null,
        OcrEvidence? LocalEvidence = null)
    {
        // Capture provenance immediately after a crop/compact view is created.
        // Later OCR requests may only reuse these exact source/input versions.
        internal OcrEvidenceIdentity PinnedIdentity { get; } =
            OcrEvidenceIdentity.Capture(SourcePath, OcrPath, "candidate");
    }'''
text = replace_once(text, old_record, new_record, 'candidate pinned identity')

# Add testable helper before IsEvidenceCurrent.
anchor = '''    internal static bool IsEvidenceCurrent(OcrEvidence evidence)
    {'''
helper = r'''    internal static OcrEvidenceIdentity RequirePinnedCandidateIdentity(
        OcrEvidenceIdentity pinned,
        string sourcePath,
        string inputPath,
        string viewId)
    {
        string source = Path.GetFullPath(sourcePath);
        string input = Path.GetFullPath(inputPath);
        if (!source.Equals(Path.GetFullPath(pinned.SourcePath), StringComparison.OrdinalIgnoreCase))
            throw new OcrException("候选来源与创建时不一致，请重新识别。", "OCR_IMAGE_CHANGED");

        string inputHash;
        if (input.Equals(Path.GetFullPath(pinned.InputPath), StringComparison.OrdinalIgnoreCase))
            inputHash = pinned.InputHash;
        else if (input.Equals(Path.GetFullPath(pinned.SourcePath), StringComparison.OrdinalIgnoreCase))
            inputHash = pinned.SourceHash;
        else
            throw new OcrException("候选识别视图与创建时不一致，请重新识别。", "OCR_IMAGE_CHANGED");

        var identity = new OcrEvidenceIdentity(
            source,
            input,
            pinned.SourceHash,
            inputHash,
            string.IsNullOrWhiteSpace(viewId) ? pinned.ViewId : viewId);
        identity.EnsureCurrent();
        return identity;
    }

'''
if text.count(anchor) != 1:
    raise RuntimeError('pinned helper anchor mismatch')
text = text.replace(anchor, helper + anchor, 1)

# Local-primary fallback must clone the original candidate so PinnedIdentity is
# retained rather than recaptured after local OCR has already happened.
old = '''                    return new RecognitionCandidate(
                        sourcePath,
                        ocrPath,
                        candidateRules,
                        true,
                        "本地主识别云兜底",
                        null,
                        lines ?? []);'''
new = '''                    RecognitionCandidate original = candidates.First(candidate =>
                        candidate.SourcePath.Equals(sourcePath, StringComparison.OrdinalIgnoreCase)
                        && candidate.OcrPath.Equals(ocrPath, StringComparison.OrdinalIgnoreCase));
                    return original with
                    {
                        Rules = candidateRules,
                        IsPrimary = true,
                        SelectionMode = "本地主识别云兜底",
                        TemplateDistance = null,
                        LocalLines = lines ?? []
                    };'''
text = replace_once(text, old, new, 'local cloud candidate clone')

# Pinned identity in local-primary cloud fallback.
old = '''                    OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(
                        candidate.SourcePath, candidate.OcrPath,
                        $"local-primary/cloud/{credential.Provider}/{candidate.SelectionMode}");'''
new = '''                    OcrEvidenceIdentity identity = RequirePinnedCandidateIdentity(
                        candidate.PinnedIdentity,
                        candidate.SourcePath,
                        candidate.OcrPath,
                        $"local-primary/cloud/{credential.Provider}/{candidate.SelectionMode}");'''
text = replace_once(text, old, new, 'local primary pinned request')

# Ordinary primary/fallback/current-precheck identities.
old = '''                OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(
                    candidate.SourcePath, candidate.OcrPath, $"cloud-primary/{candidate.SelectionMode}");'''
new = '''                OcrEvidenceIdentity identity = RequirePinnedCandidateIdentity(
                    candidate.PinnedIdentity,
                    candidate.SourcePath,
                    candidate.OcrPath,
                    $"cloud-primary/{candidate.SelectionMode}");'''
text = replace_once(text, old, new, 'ordinary primary pinned request')

old = '''                OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(
                    candidate.SourcePath, candidate.SourcePath, "cloud-fallback/original");'''
new = '''                OcrEvidenceIdentity identity = RequirePinnedCandidateIdentity(
                    candidate.PinnedIdentity,
                    candidate.SourcePath,
                    candidate.SourcePath,
                    "cloud-fallback/original");'''
text = replace_once(text, old, new, 'fallback pinned request')

old = '''                OcrEvidenceIdentity currentPrimaryIdentity = OcrEvidenceIdentity.Capture(
                    candidate.SourcePath, candidate.OcrPath, $"cloud-primary/{candidate.SelectionMode}");'''
new = '''                OcrEvidenceIdentity currentPrimaryIdentity = RequirePinnedCandidateIdentity(
                    candidate.PinnedIdentity,
                    candidate.SourcePath,
                    candidate.OcrPath,
                    $"cloud-primary/{candidate.SelectionMode}");'''
text = replace_once(text, old, new, 'precheck pinned identity')

# Candidate-based Paddle binding must also use the creation-time provenance.
old = '''    private static OcrEvidence? BindPaddleEvidence(
        PaddleLocalOcrClient client,
        RecognitionCandidate candidate,
        string stage) =>
        BindPaddleEvidence(client, candidate.SourcePath, candidate.OcrPath, stage + "/" + candidate.SelectionMode);'''
new = '''    private static OcrEvidence? BindPaddleEvidence(
        PaddleLocalOcrClient client,
        RecognitionCandidate candidate,
        string stage)
    {
        if (!client.LastEvidence.TryGetValue(candidate.OcrPath, out OcrEvidence? evidence))
            return null;
        OcrEvidenceIdentity identity = RequirePinnedCandidateIdentity(
            candidate.PinnedIdentity,
            candidate.SourcePath,
            candidate.OcrPath,
            stage + "/" + candidate.SelectionMode);
        return evidence.Bind(identity);
    }'''
text = replace_once(text, old, new, 'paddle candidate pin')

# Retry calls: derive exact request identity from the candidate pin.
old = '''                                return await RecognizeRetryEvidenceAsync(
                                    cloudClient, credential, candidate.SourcePath, primaryInputPath,
                                    completed + 1, selection.Candidates.Count);'''
new = '''                                OcrEvidenceIdentity retryIdentity = RequirePinnedCandidateIdentity(
                                    candidate.PinnedIdentity,
                                    candidate.SourcePath,
                                    primaryInputPath,
                                    $"retry/{credential.Provider}");
                                return await RecognizeRetryEvidenceAsync(
                                    cloudClient, credential, retryIdentity,
                                    completed + 1, selection.Candidates.Count);'''
text = replace_once(text, old, new, 'retry primary pinned request')

old = '''                                return await RecognizeRetryEvidenceAsync(
                                    fallbackClient, fallbackCredential, candidate.SourcePath, candidate.SourcePath,
                                    completed + 1, selection.Candidates.Count);'''
new = '''                                OcrEvidenceIdentity retryIdentity = RequirePinnedCandidateIdentity(
                                    candidate.PinnedIdentity,
                                    candidate.SourcePath,
                                    candidate.SourcePath,
                                    $"retry/{fallbackCredential.Provider}");
                                return await RecognizeRetryEvidenceAsync(
                                    fallbackClient, fallbackCredential, retryIdentity,
                                    completed + 1, selection.Candidates.Count);'''
text = replace_once(text, old, new, 'retry fallback pinned request')

old = '''    private async Task<OcrEvidence> RecognizeRetryEvidenceAsync(
        IOcrClient client,
        OcrCredential credential,
        string sourcePath,
        string inputPath,
        int current,
        int total)
    {
        OcrEvidenceIdentity identity = OcrEvidenceIdentity.Capture(
            sourcePath, inputPath, $"retry/{credential.Provider}");
        int retry = 0;'''
new = '''    private async Task<OcrEvidence> RecognizeRetryEvidenceAsync(
        IOcrClient client,
        OcrCredential credential,
        OcrEvidenceIdentity identity,
        int current,
        int total)
    {
        identity.EnsureCurrent();
        string sourcePath = identity.SourcePath;
        string inputPath = identity.InputPath;
        int retry = 0;'''
text = replace_once(text, old, new, 'retry method signature')

# Automatic publish paths become per-rule fail-closed. Manual distribute remains
# on LockCurrentEvidenceForPublish after trusted-state Load.
old = '''            using IDisposable publishGuard = RecognitionStateStore.LockCurrentEvidenceForPublish(
                rules, values, evidenceLedger);'''
new = '''            using IDisposable publishGuard = RecognitionStateStore.LockValidEvidenceForPublish(
                rules, values, evidenceLedger, missingReasons);'''
if text.count(old) != 2:
    raise RuntimeError(f'automatic publish guard expected 2, found {text.count(old)}')
text = text.replace(old, new)

old = '''            using IDisposable publishGuard = RecognitionStateStore.LockCurrentEvidenceForPublish(
                lastRules, lastValues, lastEvidenceLedger);'''
new = '''            using IDisposable publishGuard = RecognitionStateStore.LockValidEvidenceForPublish(
                lastRules, lastValues, lastEvidenceLedger, lastMissingReasons);'''
text = replace_once(text, old, new, 'retry tolerant publish')

path.write_text(text, encoding='utf-8')
print('Applied final evidence lifecycle closure')
