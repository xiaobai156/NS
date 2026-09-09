from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected 1 occurrence, found {count}")
    return text.replace(old, new, 1)

path = Path('OcrLineTool.App/MainForm.cs')
text = path.read_text(encoding='utf-8')

# Strengthen the patched record so explicit creation-time identity can be passed
# from crop creation. Other source==input candidates retain the safe default.
old = '''    private sealed record RecognitionCandidate(
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
new = '''    private sealed record RecognitionCandidate(
        string SourcePath,
        string OcrPath,
        IReadOnlyList<OcrRule> Rules,
        bool IsPrimary,
        string SelectionMode,
        int? TemplateDistance,
        IReadOnlyList<string>? LocalLines = null,
        OcrEvidence? LocalEvidence = null,
        OcrEvidenceIdentity? CreationIdentity = null)
    {
        internal OcrEvidenceIdentity PinnedIdentity { get; } =
            CreationIdentity ?? OcrEvidenceIdentity.Capture(SourcePath, OcrPath, "candidate");
    }'''
text = replace_once(text, old, new, 'candidate record creation identity')

# Add the creation-finalization helper immediately before RequirePinnedCandidateIdentity.
anchor = '''    internal static OcrEvidenceIdentity RequirePinnedCandidateIdentity(
        OcrEvidenceIdentity pinned,'''
helper = r'''    internal static OcrEvidenceIdentity PinCreatedCandidateView(
        OcrEvidenceIdentity sourceAtStart,
        string inputPath)
    {
        // sourceAtStart must itself be a source==input snapshot taken before the
        // crop/compact rendering begins.
        if (!Path.GetFullPath(sourceAtStart.SourcePath).Equals(
                Path.GetFullPath(sourceAtStart.InputPath), StringComparison.OrdinalIgnoreCase)
            || !sourceAtStart.SourceHash.Equals(sourceAtStart.InputHash, StringComparison.OrdinalIgnoreCase))
            throw new OcrException("候选来源起始身份无效，请重新识别。", "OCR_IMAGE_CHANGED");

        sourceAtStart.EnsureCurrent();
        string input = Path.GetFullPath(inputPath);
        string source = Path.GetFullPath(sourceAtStart.SourcePath);
        string inputHash;
        try
        {
            inputHash = input.Equals(source, StringComparison.OrdinalIgnoreCase)
                ? sourceAtStart.SourceHash
                : LocalOcrIdentity.Image(input);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new OcrException("无法固定候选裁剪身份，请重新识别。", "OCR_IMAGE_CHANGED");
        }

        // Check the source again after hashing the crop. This proves that the
        // source stayed on the same version throughout crop creation/finalization.
        sourceAtStart.EnsureCurrent();
        return new OcrEvidenceIdentity(
            source,
            input,
            sourceAtStart.SourceHash,
            inputHash,
            "candidate");
    }

'''
if text.count(anchor) != 1:
    raise RuntimeError('PinCreatedCandidateView insertion anchor mismatch')
text = text.replace(anchor, helper + anchor, 1)

# Template crops: capture source before rendering, finalize after rendering, and
# pass the resulting identity into the Candidate.
old = '''                        foreach (VisualTemplateMatch match in matches)
                        {
                            string cropPath = Path.Combine(templateCropFolder, $"{index++:D2}.png");
                            string ocrPath = cropPath;
                            try
                            {
                                VisualTemplateMatcher.CreateCrop(match, cropPath,
                                    includeRemainingRows: match.Template.RuleIds.Any(id =>
                                        ruleMap[id].StrictIssueBlock && !ruleMap[id].AllowNearbyValue),
                                    scale: match.Template.RuleIds.Contains("翩翩公子尾") ? 2 : 1);
                            }
                            catch (Exception exception) when (exception is ArgumentException or IOException or ExternalException)
                            {
                                ocrPath = match.SourcePath;
                            }
                            templateCandidates.Add(new RecognitionCandidate(
                                match.SourcePath,
                                ocrPath,
                                match.Template.RuleIds.Select(id => ruleMap[id]).ToArray(),
                                true,
                                "标题模板",
                                match.Distance));
                        }'''
new = '''                        foreach (VisualTemplateMatch match in matches)
                        {
                            OcrEvidenceIdentity sourceAtStart = OcrEvidenceIdentity.Capture(
                                match.SourcePath, match.SourcePath, "candidate-source");
                            string cropPath = Path.Combine(templateCropFolder, $"{index++:D2}.png");
                            string ocrPath = cropPath;
                            try
                            {
                                VisualTemplateMatcher.CreateCrop(match, cropPath,
                                    includeRemainingRows: match.Template.RuleIds.Any(id =>
                                        ruleMap[id].StrictIssueBlock && !ruleMap[id].AllowNearbyValue),
                                    scale: match.Template.RuleIds.Contains("翩翩公子尾") ? 2 : 1);
                            }
                            catch (Exception exception) when (exception is ArgumentException or IOException or ExternalException)
                            {
                                ocrPath = match.SourcePath;
                            }
                            OcrEvidenceIdentity creationIdentity = PinCreatedCandidateView(
                                sourceAtStart, ocrPath);
                            templateCandidates.Add(new RecognitionCandidate(
                                match.SourcePath,
                                ocrPath,
                                match.Template.RuleIds.Select(id => ruleMap[id]).ToArray(),
                                true,
                                "标题模板",
                                match.Distance,
                                CreationIdentity: creationIdentity));
                        }'''
text = replace_once(text, old, new, 'template crop creation pin')

# Yanran compact cloud images: pin source before PrepareLocalCloudImage and
# finalize immediately afterwards.
old = '''            foreach (LocalCandidatePlan plan in LocalCandidatePlanner.Build(
                imagePaths, localResults, rules, issue, completeIdentityRules))
            {
                string ocrPath = PrepareLocalCloudImage(selectedImageDirectory!, plan.Path, plan.Rules, ref templateCropFolder);
                localCandidates.Add(new RecognitionCandidate(
                    plan.Path,
                    ocrPath,
                    plan.Rules,
                    plan.IsPrimary,
                    (plan.IsPrimary ? "本地OCR首选" : "本地OCR备选") + (ocrPath == plan.Path ? "" : "（杰少密集表横向压缩整图）"),
                    null,
                    localResults.TryGetValue(plan.Path, out IReadOnlyList<string>? planLines) ? planLines : [],
                    BindPaddleEvidence(localClient, plan.Path, plan.Path, "candidate-small")));
            }'''
new = '''            foreach (LocalCandidatePlan plan in LocalCandidatePlanner.Build(
                imagePaths, localResults, rules, issue, completeIdentityRules))
            {
                OcrEvidenceIdentity sourceAtStart = OcrEvidenceIdentity.Capture(
                    plan.Path, plan.Path, "candidate-source");
                string ocrPath = PrepareLocalCloudImage(
                    selectedImageDirectory!, plan.Path, plan.Rules, ref templateCropFolder);
                OcrEvidenceIdentity creationIdentity = PinCreatedCandidateView(
                    sourceAtStart, ocrPath);
                localCandidates.Add(new RecognitionCandidate(
                    plan.Path,
                    ocrPath,
                    plan.Rules,
                    plan.IsPrimary,
                    (plan.IsPrimary ? "本地OCR首选" : "本地OCR备选") + (ocrPath == plan.Path ? "" : "（杰少密集表横向压缩整图）"),
                    null,
                    localResults.TryGetValue(plan.Path, out IReadOnlyList<string>? planLines) ? planLines : [],
                    BindPaddleEvidence(localClient, plan.Path, plan.Path, "candidate-small"),
                    creationIdentity));
            }'''
text = replace_once(text, old, new, 'yanran compact creation pin')

path.write_text(text, encoding='utf-8')
print('Applied crop creation identity pinning')
