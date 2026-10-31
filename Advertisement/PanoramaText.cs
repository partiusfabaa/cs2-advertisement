using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Advertisement;

internal readonly record struct PanoramaTextRun(string Text, string Color);

internal static class PanoramaText
{
    public const int MaxRuns = 16;

    private static readonly Regex ColorTags = new(
        @"\{(DEFAULT|WHITE|DARKRED|GREEN|LIGHTYELLOW|LIGHTBLUE|OLIVE|LIME|RED|LIGHTPURPLE|PURPLE|GREY|YELLOW|GOLD|SILVER|BLUE|DARKBLUE|BLUEGREY|MAGENTA|LIGHTRED|ORANGE)\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static List<PanoramaTextRun> Parse(string message)
    {
        var runs = new List<PanoramaTextRun>();
        var color = "default";
        var offset = 0;
        foreach (Match match in ColorTags.Matches(message))
        {
            AddRun(runs, message.Substring(offset, match.Index - offset), color);
            color = match.Groups[1].Value.ToLowerInvariant();
            offset = match.Index + match.Length;
        }

        AddRun(runs, message.Substring(offset), color);
        return runs;
    }

    private static void AddRun(List<PanoramaTextRun> runs, string text, string color)
    {
        if (text.Length == 0)
            return;

        if (runs.Count > 0 && runs[runs.Count - 1].Color == color)
        {
            var previous = runs[runs.Count - 1];
            runs[runs.Count - 1] = previous with { Text = previous.Text + text };
        }
        else
        {
            runs.Add(new PanoramaTextRun(text, color));
        }
    }
}
