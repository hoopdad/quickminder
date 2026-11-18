using System.Security.Cryptography;
using System.Text;

namespace Quickminder.Core;

public sealed record Note(string FileName, string Text, string Version, DateTime ModifiedUtc)
{
    public string Title => Text.Split('\n').FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? "Untitled note";
    public string Preview => string.Join("  ", Text.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).Skip(1).Select(line => line.Trim()));
    public string UpdatedLabel => ModifiedUtc.ToLocalTime().ToString("MMM d, h:mm tt");
    public override string ToString() => Title;
}

public sealed class NoteConflictException(string message) : IOException(message);

public sealed class NoteStore
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public string DirectoryPath { get; }

    public NoteStore(string directoryPath)
    {
        DirectoryPath = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(DirectoryPath);
    }

    public static string? FindDefaultDirectory()
    {
        foreach (var name in new[] { "OneDriveCommercial", "OneDriveConsumer", "OneDrive" })
        {
            var root = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                return Path.Combine(root, "Quickminder");
        }
        return null;
    }

    public IReadOnlyList<Note> List() => Directory.EnumerateFiles(DirectoryPath, "*.txt")
        .Select(path => Read(Path.GetFileName(path)))
        .OrderByDescending(note => note.ModifiedUtc)
        .ThenBy(note => note.FileName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public Note Read(string fileName)
    {
        var path = GetPath(fileName);
        var bytes = File.ReadAllBytes(path);
        // StreamReader recognizes BOMs from notes created by Notepad and other editors.
        using var reader = new StreamReader(new MemoryStream(bytes), Utf8, detectEncodingFromByteOrderMarks: true);
        return new Note(fileName, reader.ReadToEnd(), Hash(bytes), File.GetLastWriteTimeUtc(path));
    }

    public Note Create(string text = "")
    {
        var name = $"Note {DateTime.Now:yyyy-MM-dd HHmmss}-{Guid.NewGuid():N}.txt";
        var path = GetPath(name);
        var bytes = Utf8.GetBytes(text);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        return new Note(name, text, Hash(bytes), File.GetLastWriteTimeUtc(path));
    }

    public Note Save(Note previous, string text)
    {
        var path = GetPath(previous.FileName);
        CheckVersion(previous);
        var bytes = Utf8.GetBytes(text);
        var temporary = Path.Combine(DirectoryPath, $".quickminder-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            CheckVersion(previous);
            // Replace requires an existing destination, so an externally deleted note is not recreated.
            File.Replace(temporary, path, null);
        }
        finally
        {
            File.Delete(temporary);
        }
        // Keep the version of our write, not a later edit that may already be on disk.
        return new Note(previous.FileName, text, Hash(bytes), File.GetLastWriteTimeUtc(path));
    }

    public void Delete(Note note)
    {
        CheckVersion(note);
        File.Delete(GetPath(note.FileName));
    }

    private void CheckVersion(Note note)
    {
        var path = GetPath(note.FileName);
        if (!File.Exists(path) || Hash(File.ReadAllBytes(path)) != note.Version)
            throw new NoteConflictException("This note changed or was deleted outside Quickminder. Your draft is still here. Save a copy before reloading.");
    }

    private string GetPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A note must be a .txt file directly inside the notes folder.", nameof(fileName));
        return Path.Combine(DirectoryPath, fileName);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
