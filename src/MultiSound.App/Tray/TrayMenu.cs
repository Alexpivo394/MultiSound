using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MultiSound.App.ViewModels;
using ToadDesign;

namespace MultiSound.App.Tray;

/// <summary>
/// The tray icon's right-click menu as a WPF <see cref="ContextMenu"/> in the ToadDesign style, instead of the
/// WinForms one that looks like Windows 95.
/// </summary>
/// <remarks>
/// A menu needs a window of the app in the foreground to close on an outside click, and a theme root to pick up
/// ToadDesign styles: a transparent 1×1 host window provides both.
/// </remarks>
internal sealed class TrayMenu
{
    private readonly MainViewModel _viewModel;
    private readonly Window _host;
    private readonly ContextMenu _menu;
    private readonly MenuItem _toggleItem;

    public TrayMenu(MainViewModel viewModel, Action show, Action exit)
    {
        _viewModel = viewModel;
        _host = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Width = 1,
            Height = 1,
        };
        Theme.SetIsRoot(_host, true);

        _toggleItem = Item("", Symbol.PlayerPlay, () => _viewModel.ToggleRunCommand.Execute(null));
        var openItem = Item("Открыть", Symbol.AppWindow, show);
        openItem.FontWeight = FontWeights.SemiBold;
        var exitItem = Item("Выход", Symbol.Logout, exit);
        Theme.SetTone(exitItem, Tone.Danger);

        _menu = new ContextMenu { Placement = PlacementMode.AbsolutePoint };
        _menu.Items.Add(openItem);
        _menu.Items.Add(_toggleItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(exitItem);
        _menu.Closed += (_, _) => _host.Hide();
        _host.ContextMenu = _menu;
    }

    /// <summary>Opens the menu at <paramref name="screenPixels"/> (the cursor, in physical pixels).</summary>
    public void Open(System.Drawing.Point screenPixels)
    {
        var playing = _viewModel.IsRunning;
        _toggleItem.Header = playing ? "Остановить" : "Запустить";
        ((ToadDesign.Icon)_toggleItem.Icon).Symbol = playing ? Symbol.PlayerStop : Symbol.PlayerPlay;

        _host.Left = -10000;
        _host.Top = -10000;
        _host.Show();
        _host.Activate();

        var dpi = VisualTreeHelper.GetDpi(_host);
        _menu.PlacementTarget = _host;
        _menu.HorizontalOffset = screenPixels.X / dpi.DpiScaleX;
        _menu.VerticalOffset = screenPixels.Y / dpi.DpiScaleY;
        _menu.IsOpen = true;
    }

    public void Close()
    {
        _menu.IsOpen = false;
        _host.Close();
    }

    private static MenuItem Item(string header, Symbol symbol, Action action)
    {
        var item = new MenuItem { Header = header, Icon = new ToadDesign.Icon { Symbol = symbol } };
        item.Click += (_, _) => action();
        return item;
    }
}
