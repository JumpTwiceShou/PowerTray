using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LGSTrayUI;

public sealed class ThemedMessageBox : Window
{
    private MessageBoxResult _result;
    private string _optionResult = string.Empty;

    public static Func<string, string>? Translate { get; set; }

    private ThemedMessageBox(string message, string title, MessageBoxButton buttons, MessageBoxResult defaultResult)
        : this(message, title)
    {
        _result = defaultResult;
        if (buttons == MessageBoxButton.YesNo)
        {
            AddButton(ButtonText("Yes", "Yes"), MessageBoxResult.Yes, true);
            AddButton(ButtonText("No", "No"), MessageBoxResult.No, false);
        }
        else
        {
            AddButton(ButtonText("OK", "OK"), MessageBoxResult.OK, true);
        }
    }

    private ThemedMessageBox(string message, string title, IReadOnlyList<ThemedDialogOption> options)
        : this(message, title)
    {
        foreach (ThemedDialogOption option in options)
        {
            AddOptionButton(option);
        }
    }

    private ThemedMessageBox(string message, string title)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)Application.Current.Resources["WindowBackgroundBrush"];
        Foreground = (Brush)Application.Current.Resources["TextBrush"];

        Grid root = new()
        {
            Margin = new Thickness(22),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock body = new()
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 18),
        };
        root.Children.Add(body);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        Content = root;
        SourceInitialized += (_, _) => ThemeWindowInterop.Apply(this);
        CheckTheme.StaticPropertyChanged += OnThemeChanged;
        Closed += (_, _) => CheckTheme.StaticPropertyChanged -= OnThemeChanged;
    }

    public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxResult defaultResult)
    {
        ThemeService.ApplyCurrentResources();
        ThemedMessageBox dialog = new(message, title, buttons, defaultResult);
        _ = dialog.ShowDialog();
        return dialog._result;
    }

    public static string ShowOptions(string message, string title, IReadOnlyList<ThemedDialogOption> options)
    {
        ThemeService.ApplyCurrentResources();
        ThemedMessageBox dialog = new(message, title, options);
        _ = dialog.ShowDialog();
        return dialog._optionResult;
    }

    private static string ButtonText(string key, string fallback)
    {
        try
        {
            string? translated = Translate?.Invoke(key);
            return string.IsNullOrWhiteSpace(translated) ? fallback : translated;
        }
        catch
        {
            return fallback;
        }
    }

    private void AddButton(string text, MessageBoxResult result, bool isDefault)
    {
        if (Content is not Grid root || root.Children.OfType<StackPanel>().FirstOrDefault() is not StackPanel actions)
        {
            return;
        }

        actions.Children.Add(CreateButton(text, result, isDefault));
    }

    private void AddOptionButton(ThemedDialogOption option)
    {
        if (Content is not Grid root || root.Children.OfType<StackPanel>().FirstOrDefault() is not StackPanel actions)
        {
            return;
        }

        Button button = new()
        {
            Content = option.Text,
            MinWidth = 90,
            MinHeight = 32,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = option.IsDefault,
            IsCancel = option.IsCancel,
        };
        button.Click += (_, _) =>
        {
            _optionResult = option.Result;
            DialogResult = true;
        };
        actions.Children.Add(button);
    }

    private Button CreateButton(string text, MessageBoxResult result, bool isDefault)
    {
        Button button = new()
        {
            Content = text,
            MinWidth = 78,
            MinHeight = 32,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = isDefault,
            IsCancel = result is MessageBoxResult.Cancel or MessageBoxResult.No,
        };
        button.Click += (_, _) =>
        {
            _result = result;
            DialogResult = true;
        };
        return button;
    }

    private void OnThemeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            Background = (Brush)Application.Current.Resources["WindowBackgroundBrush"];
            Foreground = (Brush)Application.Current.Resources["TextBrush"];
            ThemeWindowInterop.Apply(this);
        });
    }
}

public sealed record ThemedDialogOption(string Text, string Result, bool IsDefault = false, bool IsCancel = false);
