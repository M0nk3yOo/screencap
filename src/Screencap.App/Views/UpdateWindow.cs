using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Screencap.App.Services;
using Screencap.Core.Updates;

namespace Screencap.App.Views;

/// <summary>Announces a new version with its release notes and installs it on request.</summary>
internal sealed class UpdateWindow : Window
{
    private readonly AppController _app;
    private readonly ReleaseInfo _release;
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, IsVisible = false };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button _install = new() { Content = "Jetzt aktualisieren" };
    private readonly Button _skip = new() { Content = "Version überspringen" };
    private readonly Button _later = new() { Content = "Später" };
    private CancellationTokenSource? _cancel;

    public UpdateWindow(AppController app, ReleaseInfo release)
    {
        _app = app;
        _release = release;
        Title = "Screencap – Update";
        Icon = AppIcon.Window;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _install.Classes.Add("accent");
        _install.IsDefault = true;
        _install.Click += async (_, _) => await InstallAsync();
        _skip.Click += (_, _) =>
        {
            app.Settings.SkippedUpdateVersion = release.Version.ToString(3);
            app.SaveSettings();
            Close();
        };
        _later.IsCancel = true;
        _later.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { _skip, _later, _install },
        };

        var notes = ReleaseNotes(release.Notes);
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Screencap {release.Version.ToString(3)} ist verfügbar",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Installiert ist Version {UpdateChecker.CurrentVersion.ToString(3)}.",
            Opacity = 0.7,
        });
        if (notes.Length > 0)
        {
            panel.Children.Add(new ScrollViewer
            {
                MaxHeight = 160,
                Content = new TextBlock { Text = notes, TextWrapping = TextWrapping.Wrap },
            });
        }
        panel.Children.Add(new TextBlock
        {
            Text = "Offene Screenshots bleiben beim Neustart erhalten.",
            Opacity = 0.6,
            FontSize = 12,
        });
        panel.Children.Add(_progress);
        panel.Children.Add(_status);
        panel.Children.Add(buttons);
        Content = panel;
        Closing += (_, _) => _cancel?.Cancel();
    }

    /// <summary>Release text without the link line to the changelog.</summary>
    private static string ReleaseNotes(string body) =>
        string.Join('\n', body.Replace("\r", "").Split('\n')
            .Where(line => !line.StartsWith("Details:", StringComparison.Ordinal))).Trim();

    private async Task InstallAsync()
    {
        _install.IsEnabled = _skip.IsEnabled = false;
        _later.Content = "Abbrechen";
        _progress.IsVisible = _status.IsVisible = true;
        _progress.Value = 0;
        _status.Text = "Update wird geladen …";
        _cancel = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p => _progress.Value = p);
            await _app.InstallUpdateAsync(progress, () => _status.Text = "Screencap startet neu …", _cancel.Token);
        }
        catch (OperationCanceledException)
        {
            Close();
        }
        catch (Exception e)
        {
            CrashLog.Write("Update install", e);
            _status.Text = $"Das Update ist fehlgeschlagen: {e.Message}\nDu kannst die neue Version auch von GitHub laden: {_release.PageUrl}";
            _progress.IsVisible = false;
            _install.IsEnabled = _skip.IsEnabled = true;
            _install.Content = "Erneut versuchen";
            _later.Content = "Schließen";
        }
    }
}
