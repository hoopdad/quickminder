using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Quickminder.Core;

var directory = Path.Combine(Path.GetTempPath(), "Quickminder.Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var store = new NoteStore(directory);
    var note = store.Create();
    Assert(File.Exists(Path.Combine(directory, note.FileName)), "New note persists immediately");
    foreach (var text in new[] { "a", "ab", "abc", "First line\r\nSecond line\r\n", "Unicode \u2713\n\nlast", "" })
    {
        note = store.Save(note, text);
        Assert(store.Read(note.FileName).Text == text, "Every edit round trips exactly");
        Assert(File.ReadAllText(Path.Combine(directory, note.FileName)) == text, "Plain text available to other apps");
    }
    var restarted = new NoteStore(directory);
    Assert(restarted.List().Single().Text == "", "Notes survive a new store instance");

    File.WriteAllText(Path.Combine(directory, note.FileName), "external edit");
    Expect<NoteConflictException>(() => store.Save(note, "local draft"));
    Expect<NoteConflictException>(() => store.Delete(note));
    Assert(store.Read(note.FileName).Text == "external edit", "Conflicts do not overwrite external edits");
    var recovery = store.Create("local draft");
    Assert(store.Read(recovery.FileName).Text == "local draft", "Draft can be preserved as another note");
    store.Delete(store.Read(note.FileName));
    Expect<NoteConflictException>(() => store.Save(note, "do not resurrect"));
    Assert(!File.Exists(Path.Combine(directory, note.FileName)), "Deleted files stay deleted");

    var unicodeName = "Imported note.txt";
    File.WriteAllText(Path.Combine(directory, unicodeName), "\r\nHeading\r\nDetail", Encoding.Unicode);
    var imported = store.Read(unicodeName);
    Assert(imported.Title == "Heading" && imported.Preview == "Detail", "Imported BOM and first nonempty heading are supported");
    imported = store.Save(imported, imported.Text + "\r\nUpdated");
    Assert(store.Read(unicodeName).Text == imported.Text, "Imported note can be edited as UTF-8");
    Assert(imported.Preview == "Detail  Updated", "CRLF previews stay on a single line");
    Expect<ArgumentException>(() => store.Read(Path.Combine("..", "escape.txt")));
    Expect<ArgumentException>(() => store.Read("not-a-note.json"));
    Assert(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "No temporary files left behind");
    var parsed = NoteLine.Parse("a\r\nb\nc\rd\r\n");
    Assert(parsed.Select(line => line.Text).SequenceEqual(new[] { "a", "b", "c", "d", "" }) &&
        parsed.Select(line => line.Start).SequenceEqual(new[] { 0, 3, 5, 7, 10 }),
        "Line actions preserve exact offsets across CRLF, LF, CR and trailing blank lines");
    Assert(NoteLinks.GetLaunchUri("https://example.com/path?x=1&y=2 a useful description")?.OriginalString ==
        "https://example.com/path?x=1&y=2", "Launch URL stops at the first space and preserves its query");
    Assert(NoteLinks.GetLaunchUri("http://example.com")?.Scheme == Uri.UriSchemeHttp, "HTTP URLs can be launched");
    Assert(NoteLinks.GetLaunchUri("HTTPS://example.com description")?.Scheme == Uri.UriSchemeHttps, "URL schemes are case insensitive");
    foreach (var invalid in new[] { "https://", "http-not-a-url description", "file:///C:/Windows/notepad.exe", "javascript:alert(1)", " https://example.com", "https://example.com\tlabel", "" })
        Assert(NoteLinks.GetLaunchUri(invalid) is null, "Only valid leading HTTP(S) tokens become launch actions");

    var demoStore = new NoteStore(Path.Combine(directory, "demo"));
    SampleNotes.SeedIfEmpty(demoStore);
    Assert(demoStore.List().Count == 3 && demoStore.List().All(sample => sample.Text.Contains("Fictional sample data.")),
        "Demo starts with three clearly fictional notes");
    var demoNote = demoStore.List()[0];
    demoStore.Save(demoNote, "My edited sample");
    SampleNotes.SeedIfEmpty(demoStore);
    Assert(demoStore.List().Count == 3 && demoStore.Read(demoNote.FileName).Text == "My edited sample",
        "Restarting demo does not duplicate notes or overwrite edits");
    var occupiedStore = new NoteStore(Path.Combine(directory, "occupied"));
    occupiedStore.Create("Existing note");
    SampleNotes.SeedIfEmpty(occupiedStore);
    Assert(occupiedStore.List().Single().Text == "Existing note", "Sample seeding leaves nonempty folders alone");

    var variables = new[] { "OneDriveCommercial", "OneDriveConsumer", "OneDrive" };
    var previous = variables.Select(Environment.GetEnvironmentVariable).ToArray();
    try
    {
        foreach (var variable in variables) Environment.SetEnvironmentVariable(variable, null);
        Assert(NoteStore.FindDefaultDirectory() is null, "Missing OneDrive does not silently fall back");
        Environment.SetEnvironmentVariable("OneDrive", directory);
        Assert(NoteStore.FindDefaultDirectory() == Path.Combine(directory, "Quickminder"), "Default uses OneDrive app folder");
    }
    finally
    {
        for (var i = 0; i < variables.Length; i++) Environment.SetEnvironmentVariable(variables[i], previous[i]);
    }

    Exception? uiFailure = null;
    var uiThread = new Thread(() =>
    {
        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
            Resources = new ResourceDictionary { Source = new Uri("/Quickminder;component/Styles.xaml", UriKind.Relative) }
        };
        Quickminder.MainWindow? window = null;
        try
        {
            var uiStore = new NoteStore(Path.Combine(directory, "ui"));
            var original = uiStore.Create("Heading\r\nCopy this line");
            window = new Quickminder.MainWindow(uiStore);
            var surface = (FrameworkElement)window.Content;
            window.Content = null;
            void Layout()
            {
                surface.Measure(new Size(430, 600));
                surface.Arrange(new Rect(0, 0, 430, 600));
                surface.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                surface.UpdateLayout();
            }
            var list = (ListBox)window.FindName("NotesList");
            var editor = (TextBox)window.FindName("Editor");
            list.SelectedItem = list.Items[0];
            Assert(editor.Text == original.Text, "Selecting a note opens the in-place editor");
            foreach (var character in " edited")
            {
                editor.AppendText(character.ToString());
                Assert(uiStore.Read(original.FileName).Text == editor.Text, "Editor persists each keystroke");
            }
            ((Button)window.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(((FrameworkElement)window.FindName("StackView")).Visibility == Visibility.Visible, "Back restores the note stack");
            Layout();
            var stackBottom = list.TranslatePoint(new Point(0, list.ActualHeight), surface).Y;
            Assert(Math.Abs(stackBottom - (surface.ActualHeight - 11)) <= 2, "Note stack ends at the popup's bottom inset");
            var status = (TextBlock)window.FindName("Status");
            var folder = (TextBlock)window.FindName("FolderLabel");
            var stackTop = list.TranslatePoint(new Point(), surface).Y;
            Assert(status.TranslatePoint(new Point(0, status.ActualHeight), surface).Y < stackTop &&
                folder.TranslatePoint(new Point(0, folder.ActualHeight), surface).Y < stackTop,
                "Instructions and folder information stay above the notes");
            list.SelectedItem = list.Items[0];
            Assert(editor.Text.EndsWith(" edited"), "Same note can be reopened after Back");
            editor.Text = "hello\r\ngoodbye";
            editor.CaretIndex = editor.Text.Length;
            Layout();
            var lastLine = editor.GetRectFromCharacterIndex(editor.Text.Length);
            var anchors = NoteLine.Parse(editor.Text);
            var firstLine = editor.GetRectFromCharacterIndex(anchors[0].Start);
            var secondLine = editor.GetRectFromCharacterIndex(anchors[1].Start);
            Assert(secondLine.Top >= firstLine.Bottom - 1,
                "Per-line action anchors occupy separate text rows");
            var bottomGap = editor.ActualHeight - lastLine.Bottom;
            Assert(!lastLine.IsEmpty && Math.Abs(bottomGap - editor.Padding.Bottom) <= 2 &&
                bottomGap >= lastLine.Height * 0.9 && bottomGap <= lastLine.Height * 1.3,
                "Short editor text sits one visual blank line above the bottom");
            Assert(uiStore.Read(original.FileName).Text == "hello\r\ngoodbye", "Bottom spacing adds no characters to the saved note");
            editor.Text = string.Join("\r\n", Enumerable.Range(1, 80).Select(number => $"Line {number}"));
            editor.CaretIndex = editor.Text.Length;
            Layout();
            lastLine = editor.GetRectFromCharacterIndex(editor.Text.Length);
            Assert(editor.VerticalOffset > 0 && lastLine.Top >= 0 &&
                Math.Abs(editor.ActualHeight - lastLine.Bottom - editor.Padding.Bottom) <= 2,
                "Overflowing notes scroll upward and keep the last line above the blank inset");
            editor.CaretIndex = 0;
            editor.ScrollToVerticalOffset(0);
            Layout();
            Assert(editor.VerticalOffset == 0, "Editing earlier text does not force the viewport to the end");

            editor.Text = "Long line " + new string('x', 160);
            Layout();
            editor.ScrollToHorizontalOffset(editor.ExtentWidth);
            Layout();
            Assert(editor.HorizontalOffset > 0, "Long notes can scroll horizontally");
            ((Button)window.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            list.SelectedItem = list.Items[0];
            Layout();
            Assert(editor.HorizontalOffset == 0 && editor.CaretIndex == 0,
                "Opening a long line keeps its start visible without a caret-triggered scroll to the right");
            editor.ScrollToHorizontalOffset(editor.ExtentWidth);
            Layout();
            editor.AppendText("y");
            Layout();
            Assert(editor.HorizontalOffset > 0, "Typing does not repeatedly reset horizontal scrolling");

            File.WriteAllText(Path.Combine(uiStore.DirectoryPath, original.FileName), "An external edit");
            editor.AppendText(" local draft");
            Assert(((TextBlock)window.FindName("Status")).Text.StartsWith("Not saved:"), "Editor exposes save conflicts");
            Assert(uiStore.Read(original.FileName).Text == "An external edit", "Editor never overwrites a detected conflict");
            var draft = editor.Text;
            ((Button)window.FindName("SaveCopyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(uiStore.List().Any(note => note.Text == draft), "Save a copy preserves the conflicted editor draft");
            Assert(uiStore.Read(original.FileName).Text == "An external edit", "Saving a copy leaves the external file untouched");
        }
        catch (Exception ex) { uiFailure = ex; }
        finally
        {
            window?.DisposeWatcher();
            application.Shutdown();
        }
    });
    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    uiThread.Join();
    if (uiFailure is not null) throw new Exception("WPF interaction regression failed.", uiFailure);
    Console.WriteLine("All Quickminder regression checks passed.");
}
finally
{
    Directory.Delete(directory, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}

static void Expect<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { Console.WriteLine("PASS Expected " + typeof(T).Name); return; }
    throw new Exception("Expected " + typeof(T).Name);
}
