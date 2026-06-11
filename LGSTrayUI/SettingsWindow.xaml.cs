using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace LGSTrayUI;

public partial class SettingsWindow : Window
{
    private const int WindowResizeAnimationMilliseconds = 120;
    private SettingsViewModel? _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeWindowInterop.Apply(this);
        Loaded += (_, _) => ApplyWindowBounds(animate: false);
        DataContextChanged += OnDataContextChanged;
        CheckTheme.StaticPropertyChanged += OnThemeChanged;
        Closed += (_, _) =>
        {
            CheckTheme.StaticPropertyChanged -= OnThemeChanged;
            SetViewModel(null);
        };
        UiScaleSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnUiScaleDragCompleted));
    }

    private void OnThemeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CheckTheme.LightTheme) or nameof(CheckTheme.ThemeMode))
        {
            Dispatcher.BeginInvoke(() => ThemeWindowInterop.Apply(this));
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SetViewModel(e.NewValue as SettingsViewModel);
    }

    private void SetViewModel(SettingsViewModel? viewModel)
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName is nameof(SettingsViewModel.WindowWidth)
                or nameof(SettingsViewModel.WindowHeight)
                or nameof(SettingsViewModel.WindowMinWidth)
                or nameof(SettingsViewModel.WindowMinHeight))
        {
            Dispatcher.BeginInvoke(() => ApplyWindowBounds(animate: IsLoaded));
        }
    }

    private void OnUiScaleDragCompleted(object sender, DragCompletedEventArgs e)
    {
        CommitUiScale();
    }

    private void OnUiScaleMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CommitUiScale();
    }

    private void OnUiScaleTouchUp(object sender, TouchEventArgs e)
    {
        CommitUiScale();
    }

    private void OnUiScaleKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            CommitUiScale();
        }
    }

    private void OnUiScaleLostFocus(object sender, RoutedEventArgs e)
    {
        CommitUiScale();
    }

    private void CommitUiScale()
    {
        _viewModel?.CommitUiScaleValue();
    }

    private void OnThresholdSliderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnThresholdDragCompleted));
        }
    }

    private void OnThresholdSliderUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider)
        {
            slider.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnThresholdDragCompleted));
        }
    }

    private void OnThresholdDragCompleted(object sender, DragCompletedEventArgs e)
    {
        CommitThreshold(sender);
    }

    private void OnThresholdMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CommitThreshold(sender);
    }

    private void OnThresholdTouchUp(object sender, TouchEventArgs e)
    {
        CommitThreshold(sender);
    }

    private void OnThresholdKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            CommitThreshold(sender);
        }
    }

    private void OnThresholdLostFocus(object sender, RoutedEventArgs e)
    {
        CommitThreshold(sender);
    }

    private static void CommitThreshold(object sender)
    {
        switch ((sender as FrameworkElement)?.DataContext)
        {
            case SettingsViewModel settings:
                settings.CommitDefaultThresholdPercent();
                break;
            case DeviceSettingsItemViewModel device:
                device.CommitThresholdPercent();
                break;
        }
    }

    private void OnAliasPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox textBox && WouldCreateLeadingWhitespace(textBox, e.Text))
        {
            e.Handled = true;
        }
    }

    private void OnAliasPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox ||
            !e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true) ||
            e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string pasteText ||
            !WouldCreateLeadingWhitespace(textBox, pasteText))
        {
            return;
        }

        string sanitizedText = pasteText.TrimStart();
        e.CancelCommand();
        if (sanitizedText.Length == 0)
        {
            return;
        }

        int selectionStart = textBox.SelectionStart;
        string currentText = textBox.Text ?? string.Empty;
        textBox.Text = currentText
            .Remove(selectionStart, textBox.SelectionLength)
            .Insert(selectionStart, sanitizedText);
        textBox.SelectionStart = selectionStart + sanitizedText.Length;
        textBox.SelectionLength = 0;
    }

    private static bool WouldCreateLeadingWhitespace(TextBox textBox, string insertedText)
    {
        string currentText = textBox.Text ?? string.Empty;
        int selectionStart = Math.Clamp(textBox.SelectionStart, 0, currentText.Length);
        int selectionLength = Math.Clamp(textBox.SelectionLength, 0, currentText.Length - selectionStart);
        string proposedText = currentText
            .Remove(selectionStart, selectionLength)
            .Insert(selectionStart, insertedText ?? string.Empty);

        return proposedText.Length > 0 && char.IsWhiteSpace(proposedText[0]);
    }

    private void ApplyWindowBounds(bool animate)
    {
        if (_viewModel == null)
        {
            return;
        }

        MinWidth = _viewModel.WindowMinWidth;
        MinHeight = _viewModel.WindowMinHeight;

        double width = Math.Max(_viewModel.WindowWidth, MinWidth);
        double height = Math.Max(_viewModel.WindowHeight, MinHeight);

        if (!animate)
        {
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            Width = width;
            Height = height;
            return;
        }

        AnimateDimension(WidthProperty, Width, width);
        AnimateDimension(HeightProperty, Height, height);
    }

    private void AnimateDimension(DependencyProperty property, double from, double to)
    {
        if (Math.Abs(from - to) < 0.5)
        {
            SetValue(property, to);
            return;
        }

        DoubleAnimation animation = new(from, to, TimeSpan.FromMilliseconds(WindowResizeAnimationMilliseconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) => SetValue(property, to);
        BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
