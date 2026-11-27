using AIConsole.Models;
using System.Globalization;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var choice = DecisionResult.Parse("""
    {"model":"jev-1","answers":{"decision":{"type":"choice","choice":"high","confidence":0.91,
      "probabilities":{"low":0.09,"high":0.91}}}}
    """);
Check(choice.Model == "jev-1" && choice.Answers.Count == 1, "choice/model");
Check(choice.Answers[0].Value == "high" && choice.Answers[0].Confidence == "91%", "choice values");
Check(choice.Answers[0].Probabilities[1] is { Label: "high", DisplayValue: "91%", BarValue: 91, HasBar: true }, "probability");

var wrapped = DecisionResult.Parse("""
    {"data":{"result":{"model":"jev-2","answers":{"risk":{"type":"score","score":2.5,
    "probabilities":{"0":0.2,"1":1.2}},"safe":{"type":"noul","noul":0.01}}}}}
    """);
Check(wrapped.Model == "jev-2" && wrapped.Answers.Count == 2, "wrapped result");
Check(wrapped.Answers[0].Value == "2.5", "score is not a percentage");
Check(!wrapped.Answers[0].Probabilities[1].HasBar && wrapped.Answers[0].Probabilities[1].DisplayValue == "1.2", "invalid probability");
Check(wrapped.Answers[1].Value == "1%", "noul");
Check(DecisionResult.Parse("not json").Notice.Contains("not valid JSON"), "invalid JSON fallback");
Check(DecisionResult.Parse("{\"other\":42}").Notice.Contains("No recognizable answers"), "unknown response fallback");
Check(DecisionResult.Parse("{\"answers\":{\"x\":{\"type\":\"new\",\"data\":42}}}").Answers[0].Value == "Not provided", "unknown answer type");
Console.WriteLine("Decision result parser tests passed.");
