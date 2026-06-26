using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CmlLib.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BlackLaunch.Models;
using BlackLaunch.Services;

namespace BlackLaunch;

public class MainWindow : Window
{
    private readonly ComboBox _versionBox;
    private readonly ComboBox _loaderBox;
    private readonly TextBox _nicknameBox;
    private readonly Button _playButton;
    private readonly Button _openFolderButton;
    private readonly TextBlock _statusText;

    private readonly ProgressBar _progressBar;
    private readonly TextBlock _progressDetailText;

    private readonly string _baseLauncherPath;
    private readonly string _sharedPath;
    private readonly ConfigService _configService;

    private readonly GameLauncher _gameLauncher;
    private Config _config = new();

    public MainWindow()
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BlackLaunch.logo.png")) if (stream != null) Icon = new WindowIcon(stream);
        Title = "BlackLaunch";
        Width = 460;
        Height = 450;
        MinWidth = 450;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _baseLauncherPath = Path.Combine(appData, ".black_launch");
        _sharedPath = Path.Combine(_baseLauncherPath, "shared");

        Directory.CreateDirectory(_baseLauncherPath);
        _configService = new ConfigService(Path.Combine(_baseLauncherPath, "config.json"));
        _config = _configService.Load();

        var titleBar = new Grid {
            Height = 30,
            Background = Brushes.Transparent
        };
        titleBar.PointerPressed += (sender, e) => BeginMoveDrag(e);
        var titleText = new TextBlock {
            Text = "BlackLaunch",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            FontWeight = FontWeight.SemiBold
        };

        var minimizeButton = new Button {
            Content = "—",
            Width = 40,
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(0, 0, 0, 5),
            BorderThickness = new Thickness(0)
        };
        minimizeButton.Click += (s, e) => WindowState = WindowState.Minimized;
        var closeButton = new Button {
            Content = "✕",
            Width = 40,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        closeButton.Click += (s, e) => Close();

        var systemButtonsPanel = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        systemButtonsPanel.Children.Add(minimizeButton);
        systemButtonsPanel.Children.Add(closeButton);
        titleBar.Children.Add(titleText);
        titleBar.Children.Add(systemButtonsPanel);

        _nicknameBox = new TextBox { PlaceholderText = i18n.Get("NicknamePlaceholder"), Text = _config.Nickname };
        _loaderBox = new ComboBox {
            ItemsSource = (string[])["Vanilla", "Fabric", "Forge", "Quilt", "NeoForge", "LiteLoader"],
            SelectedItem = string.IsNullOrEmpty(_config.Loader) ? "Vanilla" : _config.Loader,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _versionBox = new ComboBox {
            PlaceholderText = i18n.Get("LoadingVersions"),
            IsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        if (_config.CachedVersions != null && _config.CachedVersions.Count > 0) {
            _versionBox.ItemsSource = _config.CachedVersions;
            _versionBox.SelectedItem = _config.Version ?? _config.CachedVersions.FirstOrDefault();
            _versionBox.IsEnabled = true;
        }

        _playButton = new Button {
            Content = i18n.Get("Play"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        _playButton.Click += PlayButton_Click;

        _openFolderButton = new Button {
            Content = "📁",
            Width = 40,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        _openFolderButton.Click += OpenFolderButton_Click;
        ToolTip.SetTip(_openFolderButton, i18n.Get("OpenFolderTooltip"));

        var buttonPanel = new Grid { ColumnDefinitions = new ColumnDefinitions("*, 10, Auto") };
        Grid.SetColumn(_playButton, 0);
        Grid.SetColumn(_openFolderButton, 2);
        buttonPanel.Children.Add(_playButton);
        buttonPanel.Children.Add(_openFolderButton);

        _statusText = new TextBlock {
            Text = "",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        };
        _progressBar = new ProgressBar {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 20,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _progressDetailText = new TextBlock {
            Text = "",
            FontSize = 12,
            IsVisible = false,
            TextAlignment = TextAlignment.Center,
            Foreground = Brushes.Gray
        };
        var mainContent = new StackPanel {
            Spacing = 15,
            Margin = new Thickness(30),
            VerticalAlignment = VerticalAlignment.Center,
            Children = {
                new TextBlock {
                    Text = i18n.Get("LaunchSettings"),
                    FontWeight = FontWeight.Bold,
                    TextAlignment = TextAlignment.Center
                },
                _nicknameBox,
                _loaderBox,
                _versionBox,
                buttonPanel,
                _statusText,
                _progressBar,
                _progressDetailText
            }
        };
        var rootPanel = new DockPanel();
        DockPanel.SetDock(titleBar, Dock.Top);

        rootPanel.Children.Add(titleBar);
        rootPanel.Children.Add(mainContent);

        Content = rootPanel;
        _gameLauncher = new GameLauncher(_sharedPath);
        WireGameLauncherEvents();
        LoadVersionsAsync();
    }

    private void WireGameLauncherEvents()
    {
        _gameLauncher.StatusChanged += msg =>
            Dispatcher.UIThread.Post(() => _statusText.Text = msg);

        _gameLauncher.ProgressChanged += p =>
            Dispatcher.UIThread.Post(() => {
                _progressBar.Value = p.Percentage;
                _progressDetailText.Text = $"{p.Percentage}% | {p.Speed} | {i18n.Get("StatusTimeLeft", p.Eta)}";
                var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd != IntPtr.Zero) TaskbarProgress.SetProgress(hwnd, p.Percentage, 100);
            });

        _gameLauncher.ProgressFinished += ResetProgressUI;

        _gameLauncher.GameStarted += () =>
            Dispatcher.UIThread.Post(() => {
                _statusText.Text = i18n.Get("StatusGameRunning");
                _playButton.Content = i18n.Get("Stop");
                _playButton.IsEnabled = true;
            });
        
        _gameLauncher.GameExited += () =>
            Dispatcher.UIThread.Post(() => {
                _playButton.Content = i18n.Get("Play");
                _playButton.IsEnabled = true;
                _statusText.Text = i18n.Get("StatusReady");
            });
    }

    private async void LoadVersionsAsync()
    {
        try {
            var path = new MinecraftPath(_sharedPath);
            var launcher = new MinecraftLauncher(path);
            var versions = await launcher.GetAllVersionsAsync();
            var releases = versions.Where(v => v.Type == "release").Select(v => v.Name).ToList();
            Dispatcher.UIThread.Post(() => {
                _config.CachedVersions = releases;
                _versionBox.ItemsSource = releases;
                if (!string.IsNullOrEmpty(_config.Version) && releases.Contains(_config.Version))
                    _versionBox.SelectedItem = _config.Version;
                else if (releases.Count > 0)
                    _versionBox.SelectedIndex = 0;
                _versionBox.IsEnabled = true;
                _configService.Save(_config);
            });
        } catch (Exception ex) {
            Dispatcher.UIThread.Post(() => {
                if (_config.CachedVersions == null || _config.CachedVersions.Count == 0) _statusText.Text = i18n.Get("ErrorFetchingVersions", ex.Message);
            });
        }
    }

    private string GetCurrentInstancePath()
    {
        var mcVersion = _versionBox.SelectedItem?.ToString() ?? "unknown";
        var loader = _loaderBox.SelectedItem?.ToString() ?? "Vanilla";
        var instanceName = $"{mcVersion}-{loader}";
        return Path.Combine(_baseLauncherPath, "instances", instanceName);
    }

    private void OpenFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        var path = GetCurrentInstancePath();
        Directory.CreateDirectory(path);
        try {
            Process.Start(new ProcessStartInfo {
                FileName = path,
                UseShellExecute = true,
                Verb = "open"
            });
        } catch (Exception ex) {
            _statusText.Text = i18n.Get("ErrorOpeningFolder", ex.Message); 
        }
    }

    private async void PlayButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_gameLauncher.IsRunning) {
            _gameLauncher.Stop();
            return;
        }
        if (string.IsNullOrWhiteSpace(_nicknameBox.Text) || _versionBox.SelectedItem == null) {
            _statusText.Text = i18n.Get("ErrorEmptyNicknameOrVersion");
            return;
        }

        _config.Nickname = _nicknameBox.Text;
        _config.Version = _versionBox.SelectedItem.ToString()!;
        _config.Loader = _loaderBox.SelectedItem?.ToString() ?? "Vanilla";
        _configService.Save(_config);

        _playButton.IsEnabled = false;
        _progressBar.Value = 0;
        _progressBar.IsVisible = true;
        _progressDetailText.IsVisible = true;
        _progressDetailText.Text = "";
        try {
            await _gameLauncher.LaunchAsync(_config.Nickname, _config.Version, _config.Loader, GetCurrentInstancePath());
        } catch (Exception ex) {
            _statusText.Text = i18n.Get("ErrorLaunch", ex.Message);
            _playButton.IsEnabled = true;
            ResetProgressUI();
        }
    }

    private void ResetProgressUI()
    {
        Dispatcher.UIThread.Post(() => {
            _progressBar.IsVisible = false;
            _progressDetailText.IsVisible = false;
            _progressBar.Value = 0;
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero) TaskbarProgress.SetState(hwnd, TaskbarProgress.TaskbarStates.NoProgress);
        });
    }
}
