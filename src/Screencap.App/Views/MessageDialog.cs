using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Screencap.App.Views;

internal static class MessageDialog
{
    /// <summary>Shows a small modal-like window; returns the index of the clicked button, -1 if closed.</summary>
    public static Task<int> ShowAsync(string title, string message, params string[] buttons)
    {
        var result = new TaskCompletionSource<int>();
        var window = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], IsDefault = i == 0, IsCancel = i == buttons.Length - 1 };
            if (i == 0) button.Classes.Add("accent");
            button.Click += (_, _) =>
            {
                result.TrySetResult(index);
                window.Close();
            };
            buttonPanel.Children.Add(button);
        }

        window.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                buttonPanel,
            },
        };
        window.Closed += (_, _) => result.TrySetResult(-1);
        window.Show();
        window.Activate();
        return result.Task;
    }

    public static Task ShowErrorAsync(string title, Exception error)
    {
        Console.Error.WriteLine(error);
        return ShowAsync(title, error.Message, "OK");
    }
}
