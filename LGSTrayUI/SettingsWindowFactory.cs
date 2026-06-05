using System.Windows;

namespace LGSTrayUI;

public sealed class SettingsWindowFactory
{
    private readonly SettingsViewModel _viewModel;
    private SettingsWindow? _window;

    public SettingsWindowFactory(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public void Show()
    {
        if (_window == null)
        {
            _window = new SettingsWindow
            {
                DataContext = _viewModel,
            };
            _window.Closed += (_, _) => _window = null;
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }
        _window.Activate();
    }
}
