namespace Quickminder.Core;

public sealed record NoteLine(int Number, int Start, string Text)
{
    public static IReadOnlyList<NoteLine> Parse(string text)
    {
        var lines = new List<NoteLine>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\r' or '\n')) continue;
            lines.Add(new NoteLine(lines.Count + 1, start, text[start..index]));
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
            start = index + 1;
        }
        lines.Add(new NoteLine(lines.Count + 1, start, text[start..]));
        return lines;
    }
}
