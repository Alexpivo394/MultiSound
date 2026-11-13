using System;
using System.ComponentModel;
using System.Windows.Forms;
using MultiSound.App.ViewModels;

namespace MultiSound.App.Tray;

/// <summary>The notification-area icon: a left click shows the window, a right click opens <see cref="TrayMenu"/>.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const int MaxTooltipLength = 63;

    private readonly MainViewModel _viewModel;
    private readonly System.Drawing.Icon _icon;
    private readonly NotifyIcon _notifyIcon;
    private readonly TrayMenu _menu;

    public TrayIcon(MainViewModel viewModel, Action show, Action exit)
    {
        _viewModel = viewModel;
        // The small size (16 px at 100 % scale, more at higher DPI) is what the notification area shows.
        using (var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/MultiSound.ico")).Stream)
            _icon = new System.Drawing.Icon(stream, SystemInformation.SmallIconSize);

        _menu = new TrayMenu(viewModel, show, exit);

        _notifyIcon = new NotifyIcon { Icon = _icon, Visible = true };
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) show();
            else if (e.Button == MouseButtons.Right) _menu.Open(Cursor.Position);
        };

        _viewModel.PropertyChanged += OnViewModelChanged;
        UpdateState();
    }

    /// <summary>Tells the user once that closing the window left the app running.</summary>
    public void ShowHiddenHintOnce()
    {
        if (_viewModel.Settings.TrayHintShown) return;
        _notifyIcon.ShowBalloonTip(5000, "MultiSound", "MultiSound работает в трее. Выйти — через меню значка.", ToolTipIcon.Info);
        _viewModel.Settings.TrayHintShown = true;
        _viewModel.SaveSoon();
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _menu.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.TrayStatus) or nameof(MainViewModel.IsRunning)) UpdateState();
    }

    private void UpdateState()
    {
        var text = $"MultiSound — {_viewModel.TrayStatus}";
        _notifyIcon.Text = text.Length > MaxTooltipLength ? text[..MaxTooltipLength] : text;
    }
}
