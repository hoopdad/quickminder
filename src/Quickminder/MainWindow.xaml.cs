using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Quickminder.Core;

namespace Quickminder;

public partial class MainWindow : Window
{
    private NoteStore store;
    private Note? selected;
    private bool loading;
    private bool unsaved;
    private readonly bool isDemo;
    private readonly string? screenshotDirectory;
    private IReadOnlyList<NoteLine> lines = NoteLine.Parse("");
    private FileSystemWatcher? watcher;
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    public string NotesDirectory => store.DirectoryPath;
    public bool PlayCopySound { get; set; } = true;

    public MainWindow(NoteStore store, bool isDemo = false, string? screenshotDirectory = null)
    {
        this.store = store;
        this.isDemo = isDemo;
        this.screenshotDirectory = isDemo ? screenshotDirectory : null;
        InitializeComponent();
        if (isDemo) Title = "Quickminder - sample notes";
        Editor.Padding = new Thickness(3, 4, 3, Math.Ceiling(Editor.FontFamily.LineSpacing * Editor.FontSize));
        refreshTimer.Tick += (_, _) => { refreshTimer.Stop(); RefreshFromDisk(); };
        ConfigureWatcher();
        RefreshFromDisk();
    }

    public void ShowPopup()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 12);
        Top = Math.Max(area.Top, area.Bottom - Height - 12);
        Show();
        Activate();
        RefreshFromDisk();
        if (selected is not null) PrepareEditorForOpening();
    }

    public bool CanLeave()
    {
        if (!unsaved) return true;
        MessageBox.Show(this, "This note has unsaved changes. Fix the save error or use Save a copy before leaving it.", "Your draft is still here", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    public void HideSafely()
    {
        if (CanLeave()) Hide();
    }

    public void ChangeStore(NoteStore replacement)
    {
        DisposeWatcher();
        store = replacement;
        selected = null;
        ShowStack();
        ConfigureWatcher();
        RefreshFromDisk();
    }

    private void ConfigureWatcher()
    {
        FolderLabel.Text = isDemo ? "Sample notes (local demo)" : store.DirectoryPath;
        FolderLabel.ToolTip = store.DirectoryPath;
        watcher = new FileSystemWatcher(store.DirectoryPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName,
            IncludeSubdirectories = false
        };
        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Deleted += OnFileChanged;
        watcher.Renamed += OnFileChanged;
        watcher.Error += (_, e) => Dispatcher.BeginInvoke(() => ShowError($"Folder monitoring stopped: {e.GetException().Message}. Reopen Quickminder to refresh, or choose the folder again."));
        watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Atomic saves also produce temporary-file events; only text notes affect the view.
        if (!e.FullPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
            !(e is RenamedEventArgs renamed && renamed.OldFullPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))) return;
        Dispatcher.BeginInvoke(() => { refreshTimer.Stop(); refreshTimer.Start(); });
    }

    public void DisposeWatcher()
    {
        refreshTimer.Stop();
        watcher?.Dispose();
        watcher = null;
    }

    private void RefreshFromDisk()
    {
        try
        {
            var notes = store.List();
            NotesList.ItemsSource = notes;
            EmptyState.Visibility = notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (selected is not null)
            {
                var latest = System.Linq.Enumerable.FirstOrDefault(notes, note => note.FileName == selected.FileName);
                if (latest?.Version != selected.Version)
                {
                    if (unsaved) ShowError("This file changed outside Quickminder. Your draft is kept here. Save a copy before reloading.");
                    else if (latest is null) { ShowStack(); SetStatus("Note was removed outside Quickminder."); }
                    else LoadNote(latest);
                }
            }
        }
        catch (Exception ex) when (IsStorageError(ex)) { ShowError(ex.Message); }
    }

    private void LoadNote(Note note)
    {
        selected = note;
        loading = true;
        try { Editor.Text = note.Text; }
        finally { loading = false; }
        unsaved = false;
        StackView.Visibility = Visibility.Collapsed;
        EditorView.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Visible;
        SectionTitle.Text = "YOUR NOTE";
        SetStatus("Saved to disk - copy a line, or copy and hide with its arrow button.");
        Editor.CaretIndex = Editor.Text.Length;
        PrepareEditorForOpening();
    }

    private void PrepareEditorForOpening()
    {
        var caretLine = System.Linq.Enumerable.Last(lines, line => line.Start <= Editor.CaretIndex);
        Editor.CaretIndex = caretLine.Start;
        Editor.Focus();
        Dispatcher.BeginInvoke(() =>
        {
            if (caretLine.Number == lines.Count) Editor.ScrollToVerticalOffset(Editor.ExtentHeight);
            Editor.ScrollToHorizontalOffset(0);
            DrawCopyButtons();
        }, DispatcherPriority.Loaded);
    }

    private void ShowStack()
    {
        selected = null;
        NotesList.SelectedItem = null;
        unsaved = false;
        EditorView.Visibility = Visibility.Collapsed;
        StackView.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Collapsed;
        SectionTitle.Text = "YOUR NOTES";
        SetStatus("Plain text. Saved on every keystroke.");
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        lines = NoteLine.Parse(Editor.Text);
        if (loading || selected is null) return;
        unsaved = true;
        try
        {
            selected = store.Save(selected, Editor.Text);
            unsaved = false;
            SetStatus("Saved to disk - OneDrive handles syncing.");
        }
        catch (Exception ex) when (IsStorageError(ex)) { ShowError($"Not saved: {ex.Message}"); }
        Dispatcher.BeginInvoke(PositionEditor, DispatcherPriority.Loaded);
    }

    private void PositionEditor()
    {
        if (Editor.CaretIndex == Editor.Text.Length) Editor.ScrollToVerticalOffset(Editor.ExtentHeight);
        DrawCopyButtons();
    }

    private void DrawCopyButtons()
    {
        CopyGutter.Children.Clear();
        if (!Editor.IsVisible) return;
        var first = Editor.GetFirstVisibleLineIndex();
        var last = Math.Min(Editor.GetLastVisibleLineIndex(), lines.Count - 1);
        if (first < 0 || last < 0) return;
        for (var line = first; line <= last; line++)
        {
            // WPF's character-index-from-line helper assumes top-aligned text.
            var noteLine = lines[line];
            var rect = Editor.GetRectFromCharacterIndex(noteLine.Start);
            if (rect.IsEmpty) continue;
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            void AddAction(string icon, string label, double width, Action action)
            {
                var button = new Button
                {
                    Content = icon, FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 12, Width = width, Height = Math.Max(16, rect.Height),
                    Padding = new Thickness(3, 0, 3, 0),
                    Background = Brushes.Transparent, ToolTip = label, Focusable = false
                };
                System.Windows.Automation.AutomationProperties.SetName(button, label);
                button.Click += (_, _) => action();
                actions.Children.Add(button);
            }
            AddAction("\uE8C8", $"Copy line {line + 1}", 27,
                () => CopyText(noteLine.Text));
            AddAction("\uE8C8\uE70D", $"Copy line {line + 1} and hide to tray", 35,
                () => CopyAndHide(noteLine.Text));
            if (NoteLinks.GetLaunchUri(noteLine.Text) is not null)
                AddAction("\uE8A7", $"Open line {line + 1} URL in default browser", 27,
                    () => LaunchLine(noteLine.Text));
            Canvas.SetTop(actions, rect.Top);
            CopyGutter.Children.Add(actions);
        }
    }

    private bool CopyText(string text)
    {
        try
        {
            if (text.Length == 0) Clipboard.Clear();
            else Clipboard.SetText(text);
            // Never replace a save/conflict warning with a success message.
            if (!unsaved) SetStatus("Copied to clipboard.");
            return true;
        }
        catch (ExternalException ex) { ShowError($"Clipboard unavailable: {ex.Message}"); return false; }
    }

    private void CopyAndHide(string text)
    {
        if (!CopyText(text)) return;
        if (unsaved)
        {
            ShowError("Copied, but kept open because this draft is not saved. Fix the save error or use Save a copy.");
            return;
        }
        Hide();
        if (PlayCopySound) System.Media.SystemSounds.Asterisk.Play();
    }

    private void LaunchLine(string text)
    {
        var uri = NoteLinks.GetLaunchUri(text);
        if (uri is null)
        {
            ShowError("This line no longer starts with a valid HTTP or HTTPS URL.");
            return;
        }
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowError($"Could not open the URL: {ex.Message}");
        }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!CanLeave()) return;
        try { LoadNote(store.Create()); }
        catch (Exception ex) when (IsStorageError(ex)) { ShowError(ex.Message); }
    }

    private void Note_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (NotesList.SelectedItem is Note note && CanLeave())
        {
            try { LoadNote(store.Read(note.FileName)); }
            catch (Exception ex) when (IsStorageError(ex)) { ShowError(ex.Message); }
        }
    }

    private void SaveCopy_Click(object sender, RoutedEventArgs e)
    {
        try { LoadNote(store.Create(Editor.Text)); }
        catch (Exception ex) when (IsStorageError(ex)) { ShowError(ex.Message); }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (selected is null || !CanLeave()) return;
        if (MessageBox.Show(this, $"Permanently delete \"{selected.Title}\"?\n\nThis removes its .txt file from the synced folder.", "Delete note", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { store.Delete(selected); ShowStack(); RefreshFromDisk(); }
        catch (Exception ex) when (IsStorageError(ex)) { ShowError(ex.Message); }
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (unsaved && MessageBox.Show(this, "Discard your unsaved draft and reload from disk? Choose No to keep it, then use Save a copy.", "Reload note", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        unsaved = false;
        if (selected is not null)
        {
            try
            {
                if (File.Exists(Path.Combine(store.DirectoryPath, selected.FileName))) LoadNote(store.Read(selected.FileName));
                else ShowStack();
            }
            catch (Exception ex) when (IsStorageError(ex)) { unsaved = true; ShowError(ex.Message); }
        }
        RefreshFromDisk();
    }

    private void Back_Click(object sender, RoutedEventArgs e) { if (CanLeave()) { ShowStack(); RefreshFromDisk(); } }
    private void CopyAll_Click(object sender, RoutedEventArgs e) => CopyText(Editor.Text);
    private void Hide_Click(object sender, RoutedEventArgs e) => HideSafely();
    private void Editor_ScrollChanged(object sender, ScrollChangedEventArgs e) => DrawCopyButtons();
    private void Editor_SizeChanged(object sender, SizeChangedEventArgs e) => Dispatcher.BeginInvoke(PositionEditor, DispatcherPriority.Loaded);
    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!loading) Dispatcher.BeginInvoke(PositionEditor, DispatcherPriority.Loaded);
    }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is TextBlock or Grid) DragMove(); }
    private void Window_Closing(object? sender, CancelEventArgs e) { e.Cancel = true; HideSafely(); }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { HideSafely(); e.Handled = true; }
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { New_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.F12 && screenshotDirectory is not null)
        {
            SaveScreenshot();
            e.Handled = true;
        }
    }

    private void SaveScreenshot()
    {
        try
        {
            Directory.CreateDirectory(screenshotDirectory!);
            var surface = (FrameworkElement)Content;
            surface.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(surface);
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(surface.ActualWidth * dpi.DpiScaleX),
                (int)Math.Ceiling(surface.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen())
                drawing.DrawRectangle(Background, null, new Rect(surface.RenderSize));
            bitmap.Render(background);
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = Path.Combine(screenshotDirectory!, selected is null ? "notes-stack.png" : "note-editor.png");
            using (var output = File.Create(path)) encoder.Save(output);
            SetStatus($"Screenshot saved: {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (IsStorageError(ex) || ex is NotSupportedException)
        {
            ShowError($"Could not save screenshot: {ex.Message}");
        }
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(store.DirectoryPath) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { ShowError(ex.Message); }
    }

    private void SetStatus(string message)
    {
        Status.Text = message;
        Status.Foreground = (Brush)FindResource("Muted");
        ReloadButton.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        Status.Text = message;
        Status.Foreground = new SolidColorBrush(Color.FromRgb(163, 63, 56));
        ReloadButton.Visibility = Visibility.Visible;
    }

    private static bool IsStorageError(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException;
}
