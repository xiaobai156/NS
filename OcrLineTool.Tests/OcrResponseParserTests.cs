using OcrLineTool;

namespace OcrLineTool.Tests;

public sealed class OcrResponseParserTests
{
    [Fact]
    public void RejectsMissingCloudCredentialsBeforeMakingARequest()
    {
        Assert.Throws<OcrException>(() =>
            OcrClientFactory.Create(new OcrCredential(OcrProvider.Baidu, "D", "", "")));
    }

    [Fact]
    public void ParsesTencentDetectedTextLines()
    {
        const string json = """
        {"Response":{"TextDetections":[{"DetectedText":"240期：内容"},{"DetectedText":"241期：目标"}],"RequestId":"id"}}
        """;

        Assert.Equal(["240期：内容", "241期：目标"], TencentOcrClient.ParseLines(json));
    }

    [Fact]
    public void ReassemblesTencentFragmentsOnTheSameVisualRow()
    {
        const string json = """
        {"Response":{"TextDetections":[
          {"DetectedText":"猴","ItemPolygon":{"X":500,"Y":210,"Width":20,"Height":24}},
          {"DetectedText":"241期:新澳门[天机阁论坛","ItemPolygon":{"X":20,"Y":208,"Width":270,"Height":25}},
          {"DetectedText":"●南国挽心㊣㊣㊣杀一肖]","ItemPolygon":{"X":290,"Y":209,"Width":210,"Height":25}},
          {"DetectedText":"240期:上一行","ItemPolygon":{"X":20,"Y":178,"Width":180,"Height":24}}
        ],"RequestId":"id"}}
        """;

        Assert.Equal(
            ["240期:上一行", "241期:新澳门[天机阁论坛●南国挽心㊣㊣㊣杀一肖]猴"],
            TencentOcrClient.ParseLines(json));
    }

    [Fact]
    public void TurnsTencentApiErrorIntoSafeMessage()
    {
        const string json = """
        {"Response":{"Error":{"Code":"AuthFailure.SecretIdNotFound","Message":"SecretId 不存在"},"RequestId":"id"}}
        """;

        var error = Assert.Throws<OcrException>(() => TencentOcrClient.ParseLines(json));
        Assert.Contains("AuthFailure.SecretIdNotFound", error.Message);
        Assert.DoesNotContain("AKID", error.Message);
    }

    [Fact]
    public void RejectsMalformedTencentResponseJson()
    {
        var error = Assert.Throws<OcrException>(() => TencentOcrClient.ParseLines("{"));

        Assert.Equal("腾讯云 OCR 返回格式异常。", error.Message);
    }

    [Fact]
    public void RejectsTencentResponseWithoutTextDetections()
    {
        var error = Assert.Throws<OcrException>(() =>
            TencentOcrClient.ParseLines("{\"Response\":{}}"));

        Assert.Equal("腾讯云 OCR 未返回文字结果。", error.Message);
    }

    [Fact]
    public void ParsesBaiduWordLines()
    {
        const string json = """
        {"words_result_num":2,"words_result":[{"words":"240期：内容"},{"words":"241期：目标"}],"log_id":1}
        """;

        Assert.Equal(["240期：内容", "241期：目标"], BaiduOcrClient.ParseLines(json));
    }

    [Fact]
    public void TurnsBaiduApiErrorIntoSafeMessage()
    {
        const string json = """
        {"error_code":17,"error_msg":"Open api daily request limit reached"}
        """;

        var error = Assert.Throws<OcrException>(() => BaiduOcrClient.ParseLines(json));
        Assert.Contains("17", error.Message);
        Assert.Equal("17", error.Code);
    }

    [Fact]
    public void RejectsMalformedBaiduResponseJson()
    {
        var error = Assert.Throws<OcrException>(() => BaiduOcrClient.ParseLines("{"));

        Assert.Equal("百度 OCR 返回格式异常。", error.Message);
    }

    [Fact]
    public void RejectsBaiduResponseWithoutWordsResult()
    {
        var error = Assert.Throws<OcrException>(() => BaiduOcrClient.ParseLines("{}"));

        Assert.Equal("百度 OCR 未返回文字结果。", error.Message);
    }

    [Fact]
    public void KeepsBaiduQpsErrorCodeForRetryPolicy()
    {
        const string json = """
        {"error_code":18,"error_msg":"Open api qps request limit reached"}
        """;

        var error = Assert.Throws<OcrException>(() => BaiduOcrClient.ParseLines(json));
        Assert.Equal("18", error.Code);
    }
}
