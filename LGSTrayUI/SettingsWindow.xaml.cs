using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;

namespace LGSTrayUI;

public partial class SettingsWindow : Window
{
    private SettingsViewModel? _viewModel;
    private HwndSource? _source;
    private Size? _lastPreferredSize;

    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            ThemeWindowInterop.Apply(this);
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(OnWindowMessage);
        };
        Loaded += (_, _) => ApplyWindowBounds(animate: false);
        StateChanged += (_, _) => Dispatcher.BeginInvoke(() => SettingsWindowPlacement.Apply(this));
        DataContextChanged += OnDataContextChanged;
        CheckTheme.StaticPropertyChanged += OnThemeChanged;
        Closed += (_, _) =>
        {
            CheckTheme.StaticPropertyChanged -= OnThemeChanged;
            _source?.RemoveHook(OnWindowMessage);
            SetViewModel(null);
        };
        UiScaleSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnUiScaleDragCompleted));
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Let WPF apply its DPI change before fitting the native window to the current monitor.
        if (message is 0x02E0 or 0x007E or 0x001A or 0x0232)
            Dispatcher.BeginInvoke(() => SettingsWindowPlacement.Apply(this));
        return IntPtr.Zero;
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

    private void OnTrayToolTipModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded ||
            sender is not ComboBox { SelectedValue: TrayToolTipMode selectedMode } ||
            _viewModel == null)
        {
            return;
        }

        _viewModel.ApplyTrayToolTipModeFromUser(selectedMode, this);
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

        Size preferred = new(Math.Max(_viewModel.WindowWidth, _viewModel.WindowMinWidth),
            Math.Max(_viewModel.WindowHeight, _viewModel.WindowMinHeight));
        if (animate && _lastPreferredSize == preferred) return;
        _lastPreferredSize = preferred;
        SettingsWindowPlacement.Apply(this, preferred,
            new Size(_viewModel.WindowMinWidth, _viewModel.WindowMinHeight));
    }
}
