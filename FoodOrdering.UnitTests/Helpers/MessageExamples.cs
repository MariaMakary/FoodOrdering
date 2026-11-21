using System.Text.RegularExpressions;

namespace FoodOrdering.UnitTests.Helpers;

public static partial class MessageExamples
{
    private static readonly Lazy<IReadOnlyList<(string MessageType, string Json)>> Examples = new(Load);

    public static IReadOnlyList<(string MessageType, string Json)> All => Examples.Value;

    public static string First(string messageType) => All.First(e => e.MessageType == messageType).Json;

    private static IReadOnlyList<(string, string)> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "messages.md");
        string markdown = File.ReadAllText(path);
        var examples = new List<(string, string)>();

        foreach (Match section in SectionRegex().Matches(markdown))
        {
            string messageType = section.Groups["type"].Value;
            foreach (Match block in JsonBlockRegex().Matches(section.Groups["body"].Value))
            {
                examples.Add((messageType, block.Groups["json"].Value));
            }
        }

        return examples;
    }

    [GeneratedRegex(@"^## \d+\. `(?<type>\w+)`(?<body>.*?)(?=^## |\z)", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex SectionRegex();

    [GeneratedRegex(@"```json\r?\n(?<json>.*?)```", RegexOptions.Singleline)]
    private static partial Regex JsonBlockRegex();
}
