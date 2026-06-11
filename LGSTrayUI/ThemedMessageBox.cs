using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
            AddButton(ButtonText("Yes", "Yes"), MessageBoxResult.Yes, defaultResult == MessageBoxResult.Yes);
            AddButton(ButtonText("No", "No"), MessageBoxResult.No, defaultResult == MessageBoxResult.No);
        }
        else
        {
            AddButton(ButtonText("OK", "OK"), MessageBoxResult.OK, true);
        }
    }

    private ThemedMessageBox(string message, string title, IReadOnlyList<ThemedDialogOption> options, string? detail)
        : this(message, title, detail)
    {
        ThemedDialogOption? cancelOption = options.FirstOrDefault(x => x.IsCancel);
        if (cancelOption != null)
        {
            _optionResult = cancelOption.Result;
        }

        foreach (ThemedDialogOption option in options)
        {
            AddOptionButton(option);
        }
    }

    private ThemedMessageBox(string message, string title, string? detail = null)
    {
        Title = title;
        Width = GetDialogWidth();
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        SetResourceReference(FontFamilyProperty, "UIFontFamily");
        SetResourceReference(FontSizeProperty, "UIFontSize");
        SetResourceReference(ForegroundProperty, "TextBrush");

        Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive && x != this);
        if (Owner == null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Border shell = new()
        {
            Margin = new Thickness(14),
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
        };
        shell.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        shell.SetResourceReference(Border.BorderBrushProperty, "BorderBrushSoft");

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock titleBlock = new()
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        titleBlock.SetResourceReference(TextBlock.FontSizeProperty, "UIDialogTitleFontSize");
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        root.Children.Add(titleBlock);

        StackPanel content = new()
        {
            Margin = new Thickness(0, 0, 0, 18),
        };
        Grid.SetRow(content, 1);

        TextBlock body = new()
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
        };
        body.SetResourceReference(TextBlock.FontSizeProperty, "UIFontSize");
        body.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        content.Children.Add(body);

        if (!string.IsNullOrWhiteSpace(detail))
        {
            TextBlock detailBlock = new()
            {
                Text = detail,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
            };
            detailBlock.SetResourceReference(TextBlock.FontSizeProperty, "UIDialogDetailFontSize");
            detailBlock.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            content.Children.Add(detailBlock);
        }

        root.Children.Add(content);

        WrapPanel actions = new()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        shell.Child = root;
        Content = shell;

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

    public static string ShowOptions(string message, string title, IReadOnlyList<ThemedDialogOption> options, string? detail = null)
    {
        ThemeService.ApplyCurrentResources();
        ThemedMessageBox dialog = new(message, title, options, detail);
        _ = dialog.ShowDialog();
        return dialog._optionResult;
    }

    private static double GetDialogWidth()
    {
        double desired = 460.0 * ThemeService.CurrentScale;
        double workAreaWidth = SystemParameters.WorkArea.Width;
        if (workAreaWidth <= 0)
        {
            return desired;
        }

        return Math.Max(360.0, Math.Min(desired, workAreaWidth - 48.0));
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
        if (FindActionsPanel() is not { } actions)
        {
            return;
        }

        Button button = CreateButton(text, isDefault, result is MessageBoxResult.Cancel or MessageBoxResult.No, false);
        button.Click += (_, _) =>
        {
            _result = result;
            DialogResult = true;
        };
        actions.Children.Add(button);
    }

    private void AddOptionButton(ThemedDialogOption option)
    {
        if (FindActionsPanel() is not { } actions)
        {
            return;
        }

        Button button = CreateButton(option.Text, option.IsDefault, option.IsCancel, option.IsDestructive);
        button.Click += (_, _) =>
        {
            _optionResult = option.Result;
            DialogResult = true;
        };
        actions.Children.Add(button);
    }

    private WrapPanel? FindActionsPanel()
    {
        return Content is Border { Child: Grid root }
            ? root.Children.OfType<WrapPanel>().FirstOrDefault()
            : null;
    }

    private static Button CreateButton(string text, bool isDefault, bool isCancel, bool isDestructive)
    {
        double scale = ThemeService.CurrentScale;
        TextBlock content = new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 180 * scale,
        };
        Button button = new()
        {
            Content = content,
            MinWidth = 92 * scale,
            Padding = new Thickness(14 * scale, 7 * scale, 14 * scale, 7 * scale),
            Margin = new Thickness(8 * scale, 0, 0, 0),
            FontWeight = FontWeights.SemiBold,
            IsDefault = isDefault,
            IsCancel = isCancel,
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = CreateButtonTemplate(),
        };

        button.SetResourceReference(FrameworkElement.MinHeightProperty, "UIInputMinHeight");
        button.SetResourceReference(Control.FontFamilyProperty, "UIFontFamily");
        button.SetResourceReference(Control.FontSizeProperty, "UIFontSize");
        button.SetResourceReference(Control.BorderBrushProperty, "ButtonBorderBrush");
        button.SetResourceReference(Control.ForegroundProperty, "ButtonTextBrush");

        if (isDestructive)
        {
            button.Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
            button.Foreground = Brushes.White;
        }
        else if (isDefault)
        {
            button.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
            button.SetResourceReference(Control.ForegroundProperty, "NavigationSelectedTextBrush");
        }
        else
        {
            button.SetResourceReference(Control.BackgroundProperty, "ButtonBackgroundBrush");
        }

        return button;
    }

    private static ControlTemplate CreateButtonTemplate()
    {
        FrameworkElementFactory border = new(typeof(Border))
        {
            Name = "ButtonBorder",
        };
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        FrameworkElementFactory presenter = new(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        border.AppendChild(presenter);

        ControlTemplate template = new(typeof(Button))
        {
            VisualTree = border,
        };
        Trigger disabled = new()
        {
            Property = UIElement.IsEnabledProperty,
            Value = false,
        };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "ButtonBorder"));
        template.Triggers.Add(disabled);
        return template;
    }

    private void OnThemeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CheckTheme.LightTheme) or nameof(CheckTheme.ThemeMode))
        {
            Dispatcher.BeginInvoke(() => ThemeWindowInterop.Apply(this));
        }
    }
}

public sealed record ThemedDialogOption(
    string Text,
    string Result,
    bool IsDefault = false,
    bool IsCancel = false,
    bool IsDestructive = false
);
