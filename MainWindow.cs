using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ModLoaders.LiteLoader;
using CmlLib.Core.ModLoaders.QuiltMC;
using CmlLib.Core.ProcessBuilder;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlackLaunch.Models;

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
    private readonly string _configFile;

    private Process? _runningGame;
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
        _configFile = Path.Combine(_baseLauncherPath, "config.json");

        Directory.CreateDirectory(_baseLauncherPath);
        LoadConfig();

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
        LoadVersionsAsync();
    }

    private void LoadConfig()
    {
        try {
            if (File.Exists(_configFile)) {
                var json = File.ReadAllText(_configFile);
                _config = JsonSerializer.Deserialize(json, ConfigContext.Default.Config) ?? new Config();
            }
        } catch { _config = new Config(); }
    }

    private void SaveConfig()
    {
        try {
            var json = JsonSerializer.Serialize(_config, ConfigContext.Default.Config);
            File.WriteAllText(_configFile, json);
        } catch { }
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
                SaveConfig();
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
        if (_runningGame != null && !_runningGame.HasExited) {
            try { _runningGame.Kill(); } catch { }
            return;
        }
        if (string.IsNullOrWhiteSpace(_nicknameBox.Text) || _versionBox.SelectedItem == null) {
            _statusText.Text = i18n.Get("ErrorEmptyNicknameOrVersion");
            return;
        }

        _config.Nickname = _nicknameBox.Text;
        _config.Version = _versionBox.SelectedItem.ToString()!;
        _config.Loader = _loaderBox.SelectedItem?.ToString() ?? "Vanilla";
        SaveConfig();

        _playButton.IsEnabled = false;
        _progressBar.Value = 0;
        _progressBar.IsVisible = true;
        _progressDetailText.IsVisible = true;
        _progressDetailText.Text = "";
        try {
            await LaunchGameAsync(_config.Nickname, _config.Version, _config.Loader);
        } catch (Exception ex) {
            _statusText.Text = i18n.Get("ErrorLaunch", ex.Message);
            _playButton.IsEnabled = true;
            ResetProgressUI();
        }
    }

    private async Task LaunchGameAsync(string nickname, string mcVersion, string loader)
    {
        var instancePath = GetCurrentInstancePath();
        var path = new MinecraftPath() {
            BasePath = instancePath,
            Library = Path.Combine(_sharedPath, "libraries"),
            Versions = Path.Combine(_sharedPath, "versions"),
            Resource = Path.Combine(_sharedPath, "resources"),
            Assets = Path.Combine(_sharedPath, "assets"),
            Runtime = Path.Combine(_sharedPath, "runtime")
        };
        path.CreateDirs();

        var launcher = new MinecraftLauncher(path);
        var sw = Stopwatch.StartNew();
        var updateTimer = Stopwatch.StartNew();
        bool downloadStarted = false;
        launcher.FileProgressChanged += (sender, args) => {
            if (!downloadStarted) {
                downloadStarted = true;
                Dispatcher.UIThread.Post(() => _statusText.Text = i18n.Get("StatusDownloadingFiles"));
            }
            if (updateTimer.ElapsedMilliseconds > 300)
                Dispatcher.UIThread.Post(() => _statusText.Text = i18n.Get("StatusDownloading", args.Name ?? ""));
        };

        launcher.ByteProgressChanged += (sender, args) => {
            if (args.TotalBytes <= 0) return;
            if (updateTimer.ElapsedMilliseconds < 100 && args.ProgressedBytes != args.TotalBytes) return;
            updateTimer.Restart();

            int percentage = (int)((args.ProgressedBytes * 100) / args.TotalBytes);
            double totalSeconds = sw.Elapsed.TotalSeconds;
            string speedStr = "0 MB/s";
            string etaStr = "00:00";
            if (totalSeconds > 0) {
                double bytesPerSec = args.ProgressedBytes / totalSeconds;
                speedStr = (bytesPerSec / 1024 / 1024).ToString("0.00") + " MB/s";
                long remainingBytes = args.TotalBytes - args.ProgressedBytes;
                if (bytesPerSec > 0) {
                    TimeSpan eta = TimeSpan.FromSeconds(remainingBytes / bytesPerSec);
                    etaStr = eta.ToString(@"mm\:ss");
                }
            }
            Dispatcher.UIThread.Post(() => {
                _progressBar.Value = percentage;
                _progressDetailText.Text = $"{percentage}% | {speedStr} | {i18n.Get("StatusTimeLeft", etaStr)}";
                var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd != IntPtr.Zero) TaskbarProgress.SetProgress(hwnd, percentage, 100);
            });
        };

        string versionToLaunch = mcVersion;
        Dispatcher.UIThread.Post(() => _statusText.Text = _statusText.Text = i18n.Get("StatusPreparingLoader", loader));
        using var httpClient = new HttpClient();
        if (loader == "Fabric") {
            var fabricInstaller = new FabricInstaller(httpClient);
            versionToLaunch = await fabricInstaller.Install(mcVersion, path);
        } else if (loader == "Forge") {
            var forgeInstaller = new ForgeInstaller(launcher);
            versionToLaunch = await forgeInstaller.Install(mcVersion);
        } else if (loader == "Quilt") {
            var quiltInstaller = new QuiltInstaller(httpClient);
            versionToLaunch = await quiltInstaller.Install(mcVersion, path);
        } else if (loader == "NeoForge") {
            var neoForgeInstaller = new NeoForgeInstaller(launcher);
            versionToLaunch = await neoForgeInstaller.Install(mcVersion);
        } else if (loader == "LiteLoader") {
            var liteLoaderInstaller = new LiteLoaderInstaller(httpClient);
            var loaders = await liteLoaderInstaller.GetAllLiteLoaders();
            var loaderToInstall = loaders.FirstOrDefault(l => l.BaseVersion == mcVersion)
                ?? throw new Exception(i18n.Get("ErrorLiteLoader", mcVersion));
            var baseVersion = await launcher.GetVersionAsync(mcVersion);
            versionToLaunch = await liteLoaderInstaller.Install(loaderToInstall, baseVersion, path);
        }

        sw.Restart();
        updateTimer.Restart();
        Dispatcher.UIThread.Post(() => _statusText.Text = i18n.Get("StatusCheckingAssets"));
        await launcher.InstallAsync(versionToLaunch);
        ResetProgressUI();
        Dispatcher.UIThread.Post(() => _statusText.Text = i18n.Get("StatusBuildingFiles"));

        var jvmArguments = "-XX:+UseG1GC -Dsun.rmi.dgc.server.gcInterval=2147483646 -XX:+UnlockExperimentalVMOptions -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M -Dfile.encoding=UTF-8";
        if (versionToLaunch == "1.16.4" || versionToLaunch == "1.16.5") {
            jvmArguments += " -Dminecraft.api.env=custom -Dminecraft.api.auth.host=https://invalid.invalid -Dminecraft.api.account.host=https://invalid.invalid -Dminecraft.api.session.host=https://invalid.invalid -Dminecraft.api.services.host=https://invalid.invalid";
        }
        var arguments = new MArgument[] { MArgument.FromCommandLine(jvmArguments) };
        var launchOptions = new MLaunchOption {
            Session = MSession.CreateOfflineSession(nickname),
            MaximumRamMb = 4096,
            MinimumRamMb = 1024,
            FullScreen = false,
            ExtraJvmArguments = arguments
        };
        var process = await launcher.BuildProcessAsync(versionToLaunch, launchOptions);
        _runningGame = process;
        _runningGame.EnableRaisingEvents = true;
        _runningGame.Exited += (s, ev) => {
            Dispatcher.UIThread.Post(() => {
                _runningGame = null;
                _playButton.Content = i18n.Get("Play");
                _playButton.IsEnabled = true;
                _statusText.Text = i18n.Get("StatusReady");
            });
        };

        Dispatcher.UIThread.Post(() => {
            _statusText.Text = i18n.Get("StatusGameRunning");
            _playButton.Content = i18n.Get("Stop");
            _playButton.IsEnabled = true;
        });

        var processWrapper = new ProcessWrapper(process);
        processWrapper.OutputReceived += (s, log) => Console.WriteLine($"[MINECRAFT] {log}");
        processWrapper.StartWithEvents();
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
