using OcrLineTool;
using Xunit;

namespace OcrLineTool.Tests;

[Trait("Category", "ReauditAccuracy")]
public sealed class FinalCropCreationIdentityTests
{
    [Fact]
    public void SourceReplacementBetweenCropCreationAndCandidateFinalizationIsRejected()
    {
        string root = Path.Combine(Path.GetTempPath(), "ns-crop-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "source.png");
            string crop = Path.Combine(root, "crop.png");
            File.WriteAllBytes(source, [1, 2, 3, 4]);
            OcrEvidenceIdentity sourceAtStart = OcrEvidenceIdentity.Capture(
                source, source, "candidate-source");

            // Simulate a crop produced from source version A, followed by the
            // crawler replacing the source path with version B before Candidate
            // construction/finalization.
            File.WriteAllBytes(crop, [9, 8, 7, 6]);
            File.WriteAllBytes(source, [4, 3, 2, 1]);

            OcrException error = Assert.Throws<OcrException>(() =>
                MainForm.PinCreatedCandidateView(sourceAtStart, crop));
            Assert.Equal("OCR_IMAGE_CHANGED", error.Code);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void StableSourceAndCropProducePinnedCreationIdentity()
    {
        string root = Path.Combine(Path.GetTempPath(), "ns-crop-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "source.png");
            string crop = Path.Combine(root, "crop.png");
            File.WriteAllBytes(source, [1, 2, 3, 4]);
            OcrEvidenceIdentity sourceAtStart = OcrEvidenceIdentity.Capture(
                source, source, "candidate-source");
            File.WriteAllBytes(crop, [9, 8, 7, 6]);

            OcrEvidenceIdentity pinned = MainForm.PinCreatedCandidateView(sourceAtStart, crop);
            Assert.Equal(sourceAtStart.SourceHash, pinned.SourceHash);
            Assert.Equal(LocalOcrIdentity.Image(crop), pinned.InputHash);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
