using System.Xml.Linq;

namespace OcrLineTool.Tests;

public sealed class PublishConfigurationTests
{
    [Fact]
    public void PublishesAsAFrameworkDependentSingleFile()
    {
        string project = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "OcrLineTool.App", "OcrLineTool.App.csproj"));
        XDocument document = XDocument.Load(project);

        Assert.Equal("true", document.Descendants("PublishSingleFile").Single().Value);
        Assert.Equal("false", document.Descendants("SelfContained").Single().Value);
        Assert.Equal("win-x64", document.Descendants("RuntimeIdentifier").Single().Value);
        Assert.Contains(@"..\模型\**\*", document.Descendants("Content").Select(element => (string?)element.Attribute("Include")));
    }

    [Fact]
    [Trait("Category", "ModelAssets")]
    public void CopiesCudaModelsFromTheSiblingModelDirectory()
    {
        string project = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "OcrLineTool.App", "OcrLineTool.App.csproj"));
        XDocument document = XDocument.Load(project);
        XElement modelContent = document.Descendants("Content")
            .Single(element => (string?)element.Attribute("Include") == @"..\模型\**\*");

        Assert.Equal(@"模型\%(RecursiveDir)%(Filename)%(Extension)",
            (string?)modelContent.Attribute("TargetPath"));

        string model = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(project)!, "..", "模型", "PP-OCRv6_small_det", "inference.json"));
        Assert.True(File.Exists(model));

        string copiedModel = Path.Combine(
            AppContext.BaseDirectory, "模型", "PP-OCRv6_small_det", "inference.json");
        Assert.True(File.Exists(copiedModel));
    }

    [Fact]
    public void PublishesTheCudaCheckScriptWithTheRuntimeComponents()
    {
        string project = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "OcrLineTool.App", "OcrLineTool.App.csproj"));
        XDocument document = XDocument.Load(project);
        XElement checker = document.Descendants("Content")
            .Single(element => (string?)element.Attribute("Include") == @"..\检查NVIDIA-CUDA环境.py");

        Assert.Equal(@"运行组件\检查NVIDIA-CUDA环境.py",
            (string?)checker.Attribute("TargetPath"));
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(project)!, "..", "检查NVIDIA-CUDA环境.py"))));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,
            "运行组件", "检查NVIDIA-CUDA环境.py")));
    }
}
