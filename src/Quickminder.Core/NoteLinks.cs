namespace Quickminder.Core;

public static class NoteLinks
{
    public static Uri? GetLaunchUri(string line)
    {
        if (!line.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        var space = line.IndexOf(' ');
        var token = space < 0 ? line : line[..space];
        if (token.Any(char.IsWhiteSpace)) return null;
        return Uri.TryCreate(token, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri : null;
    }
}
