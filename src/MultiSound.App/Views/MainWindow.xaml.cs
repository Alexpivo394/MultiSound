using System;
using System.ComponentModel;
using System.Windows;
using MultiSound.App.ViewModels;
using ToadDesign;

namespace MultiSound.App.Views;

/// <summary>The main window; closing it only hides it, the app keeps playing from the tray.</summary>
public partial class MainWindow : ToadWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        RestorePlacement();

        SizeChanged += (_, _) => StorePlacement();
        LocationChanged += (_, _) => StorePlacement();
    }

    /// <summary>Raised when the close button hid the window.</summary>
    public event EventHandler? HiddenToTray;

    /// <summary>Set before <see cref="Window.Close"/> to really close the window on exit.</summary>
    public bool AllowClose { get; set; }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
        }
        base.OnClosing(e);
    }

    private void RestorePlacement()
    {
        var settings = _viewModel.Settings;
        if (settings.WindowWidth is { } width && settings.WindowHeight is { } height)
        {
            Width = Math.Max(width, MinWidth);
            Height = Math.Max(height, MinHeight);
        }

        // Only restore a position that is still on a screen (a monitor may have been unplugged).
        if (settings.WindowLeft is { } left && settings.WindowTop is { } top &&
            left >= SystemParameters.VirtualScreenLeft &&
            top >= SystemParameters.VirtualScreenTop &&
            left + 100 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
            top + 50 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
    }

    private void StorePlacement()
    {
        if (WindowState != WindowState.Normal || !IsLoaded) return;
        var settings = _viewModel.Settings;
        settings.WindowLeft = Left;
        settings.WindowTop = Top;
        settings.WindowWidth = Width;
        settings.WindowHeight = Height;
        _viewModel.SaveSoon();
    }
}
