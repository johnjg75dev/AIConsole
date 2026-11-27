using System.Globalization;
using AIConsole.Models;

namespace AIConsole.Tests;

public class DecisionResultTests
{
    [Fact]
    public void Parse_Choice_ReturnsValueConfidenceAndBars()
    {
        var result = DecisionResult.Parse("""
            {"model":"jev-1","answers":{"decision":{"type":"choice","choice":"high","confidence":0.91,
              "probabilities":{"low":0.09,"high":0.91}}}}
            """);

        Assert.Equal("jev-1", result.Model);
        var answer = Assert.Single(result.Answers);
        Assert.Equal("decision", answer.Name);
        Assert.Equal("choice", answer.Type);
        Assert.Equal("high", answer.Value);
        Assert.Equal("91%", answer.Confidence);
        Assert.Equal(2, answer.Probabilities.Count);

        var high = answer.Probabilities.Single(p => p.Label == "high");
        Assert.Equal("91%", high.DisplayValue);
        Assert.Equal(91, high.BarValue, 3);
        Assert.True(high.HasBar);

        var low = answer.Probabilities.Single(p => p.Label == "low");
        Assert.Equal(9, low.BarValue, 3);
        Assert.True(low.HasBar);
        Assert.Empty(result.Notice);
    }

    [Fact]
    public void Parse_UnwrapsDataResult_AndHandlesScoreAndNoul()
    {
        var result = DecisionResult.Parse("""
            {"data":{"result":{"model":"jev-2","answers":{"risk":{"type":"score","score":2.5,
            "probabilities":{"0":0.2,"1":1.2}},"safe":{"type":"noul","noul":0.01}}}}}
            """);

        Assert.Equal("jev-2", result.Model);
        Assert.Equal(2, result.Answers.Count);

        var risk = result.Answers[0];
        Assert.Equal("risk", risk.Name);
        Assert.Equal("score", risk.Type);
        Assert.Equal("2.5", risk.Value);
        Assert.Equal(2, risk.Probabilities.Count);
        Assert.True(risk.Probabilities[0].HasBar);
        Assert.False(risk.Probabilities[1].HasBar); // 1.2 is outside the 0..1 probability range
        Assert.Equal("1.2", risk.Probabilities[1].DisplayValue);

        var safe = result.Answers[1];
        Assert.Equal("noul", safe.Type);
        Assert.Equal("1%", safe.Value);
        Assert.Equal("", safe.Confidence);
    }

    [Fact]
    public void Parse_UnknownType_IsReportedAsNotProvided()
    {
        var result = DecisionResult.Parse("""{"answers":{"x":{"type":"other","confidence":"high"}}}""");

        var answer = Assert.Single(result.Answers);
        Assert.Equal("other", answer.Type);
        Assert.Equal("Not provided", answer.Value);
        Assert.Equal("high", answer.Confidence);
    }

    [Fact]
    public void Parse_InvalidJson_RetainsRawBodyAndNotice()
    {
        var result = DecisionResult.Parse("this is not json");

        Assert.Equal("this is not json", result.Raw);
        Assert.Empty(result.Answers);
        Assert.Contains("not valid JSON", result.Notice);
    }

    [Fact]
    public void Parse_NoAnswers_ReturnsNotice()
    {
        var result = DecisionResult.Parse("""{"model":"m","answers":{}}""");

        Assert.Equal("m", result.Model);
        Assert.Empty(result.Answers);
        Assert.Contains("No recognizable answers", result.Notice);
    }

    [Fact]
    public void Parse_FormatsPercentWithInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var result = DecisionResult.Parse("""{"answers":{"d":{"type":"noul","noul":0.915,"confidence":0.915}}}""");

            Assert.Equal("91.5%", result.Answers[0].Value);
            Assert.Equal("91.5%", result.Answers[0].Confidence);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
