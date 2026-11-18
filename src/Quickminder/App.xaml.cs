using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using Quickminder.Core;
using Forms = System.Windows.Forms;

namespace Quickminder;

public partial class App : Application
{
    private Mutex? mutex;
    private EventWaitHandle? activation;
    private RegisteredWaitHandle? activationWait;
    private Forms.NotifyIcon? tray;
    private Icon? trayIcon;
    private MainWindow? popup;
    private bool isDemo;
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quickminder", "settings.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        isDemo = e.Args.Length > 0 && e.Args[0] == "--demo";
        string? screenshotDirectory = null;
        if (e.Args.Length != 0 && !(isDemo && (e.Args.Length == 1 ||
            (e.Args.Length == 3 && e.Args[1] == "--screenshots" && Path.IsPathFullyQualified(e.Args[2])))))
        {
            MessageBox.Show("Usage: Quickminder.exe [--demo [--screenshots <absolute output folder>]]", "Quickminder", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        if (e.Args.Length == 3) screenshotDirectory = e.Args[2];
        var instanceName = isDemo ? "Quickminder.Demo" : "Quickminder";
        mutex = new Mutex(true, $@"Local\{instanceName}.App", out var first);
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{instanceName}.Open");
        if (!first)
        {
            activation.Set();
            Shutdown();
            return;
        }

        try
        {
            string? directory = null;
            var playCopySound = true;
            if (!isDemo && File.Exists(settingsPath))
            {
                var preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(settingsPath));
                directory = preferences?.NotesDirectory;
                playCopySound = preferences?.PlayCopySound ?? true;
            }
            directory ??= isDemo
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quickminder", "Demo", "Notes")
                : NoteStore.FindDefaultDirectory();
            if (directory is null)
            {
                MessageBox.Show("No local OneDrive folder was found. Choose your synced Quickminder folder. Notes stay on this computer unless the selected folder is synced.", "Quickminder", MessageBoxButton.OK, MessageBoxImage.Information);
                directory = PickDirectory();
            }
            if (directory is null)
            {
                Shutdown();
                return;
            }
            var store = new NoteStore(directory);
            if (isDemo) SampleNotes.SeedIfEmpty(store);
            popup = new MainWindow(store, isDemo, screenshotDirectory) { PlayCopySound = playCopySound };
            MainWindow = popup;
            SavePreferences(directory);
            CreateTray();
            activationWait = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) =>
                Dispatcher.BeginInvoke(() => popup.ShowPopup()), null, Timeout.Infinite, false);
            popup.ShowPopup();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            MessageBox.Show($"Quickminder could not start:\n\n{ex.Message}\n\nSettings: {settingsPath}", "Quickminder", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void CreateTray()
    {
        trayIcon = CreateIcon();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = isDemo ? "Quickminder - sample notes" : "Quickminder - click for notes", Visible = true };
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                if (popup!.IsVisible) popup.HideSafely();
                else popup.ShowPopup();
            }
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Quickminder", null, (_, _) => popup!.ShowPopup());
        menu.Items.Add("Open notes folder", null, (_, _) => OpenFolder());
        menu.Items.Add("Choose notes folder...", null, (_, _) => ChangeDirectory()).Enabled = !isDemo;
        var sound = new Forms.ToolStripMenuItem("Sound on copy and hide") { Checked = popup!.PlayCopySound, CheckOnClick = true };
        sound.Click += (_, _) =>
        {
            var previous = popup.PlayCopySound;
            popup.PlayCopySound = sound.Checked;
            try { SavePreferences(popup.NotesDirectory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                popup.PlayCopySound = previous;
                sound.Checked = previous;
                MessageBox.Show(popup, $"Could not save the sound preference:\n\n{ex.Message}", "Quickminder", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        menu.Items.Add(sound);
        menu.Items.Add("Tray visibility settings", null, (_, _) => OpenShell("ms-settings:taskbar"));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => { if (popup!.CanLeave()) Shutdown(); });
        tray.ContextMenuStrip = menu;
    }

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var background = new SolidBrush(Color.FromArgb(19, 124, 112));
        graphics.FillEllipse(background, 1, 1, 30, 30);
        using var pen = new Pen(Color.White, 2.5f);
        graphics.DrawRectangle(pen, 9, 7, 14, 18);
        graphics.DrawLine(pen, 12, 12, 20, 12);
        graphics.DrawLine(pen, 12, 17, 20, 17);
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private void OpenFolder() => OpenShell(popup!.NotesDirectory);

    private static void OpenShell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(ex.Message, "Quickminder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? PickDirectory()
    {
        using var picker = new Forms.FolderBrowserDialog { Description = "Choose the Quickminder notes folder", UseDescriptionForTitle = true };
        return picker.ShowDialog() == Forms.DialogResult.OK ? picker.SelectedPath : null;
    }

    private void ChangeDirectory()
    {
        popup!.ShowPopup();
        if (!popup.CanLeave()) return;
        var directory = PickDirectory();
        if (directory is null) return;
        try
        {
            var store = new NoteStore(directory);
            _ = store.List();
            SavePreferences(directory);
            popup.ChangeStore(store);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(ex.Message, "Cannot change folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SavePreferences(string directory)
    {
        if (isDemo) return;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporary = settingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Preferences(directory, popup?.PlayCopySound ?? true)));
        File.Move(temporary, settingsPath, overwrite: true);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        activationWait?.Unregister(null);
        popup?.DisposeWatcher();
        tray?.ContextMenuStrip?.Dispose();
        tray?.Dispose();
        trayIcon?.Dispose();
        activation?.Dispose();
        mutex?.Dispose();
        base.OnExit(e);
    }

    private sealed record Preferences(string NotesDirectory, bool PlayCopySound = true);
}
