using Avalonia;
using Avalonia.Controls;
using IconPath = Avalonia.Controls.Shapes.Path;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using CmlLib.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using BlackLaunch.Models;
using BlackLaunch.Services;
using BlackLaunch.Platform;

namespace BlackLaunch.Views;

public class MainWindow : Window
{
    private IconPath _playTabIcon = new();
    private IconPath _serversTabIcon = new();
    private ComboBox _versionBox = new();
    private ComboBox _loaderBox = new();
    private TextBox _nicknameBox = new();
    private Button _playButton = new();
    private Button _openFolderButton = new();
    private TextBlock _statusText = new();
    private ProgressBar _progressBar = new();
    private TextBlock _progressDetailText = new();

    private readonly ContentControl _tabContent = new() { Margin = new Thickness(0) };
    private readonly Control _playView = new();
    private readonly Control _serversView = new();
    private Border _playUnderline = new();
    private Border _serversUnderline = new();
    private Border _playHoverBg = new();
    private Border _serversHoverBg = new();
    private TextBlock _playTabLabel = new();
    private TextBlock _serversTabLabel = new();
    private TextBlock _nickLabel = new();
    private IconPath _nickIcon = new();
    private Border _nickBorder = new();
    private string _activeTab = "Play";

    private readonly string _baseLauncherPath;
    private readonly string _sharedPath;
    private readonly ConfigService _configService;
    private readonly GameLauncher _gameLauncher;
    private readonly Config _config = new();
    
    private sealed record LoaderOption(string Name, string IconData);
    private static void StyleField(TemplatedControl c)
    {
        c.Background = Themes.FieldBg;
        c.BorderBrush = Themes.Border;
        c.BorderThickness = new Thickness(1);
        c.CornerRadius = new CornerRadius(8);
        c.MinHeight = 44;
        c.Foreground = Themes.TextPrimary;
        c.FontSize = 14;
    }

    public MainWindow()
    {
        Title = "BlackLaunch";
        Width = 460;
        Height = 560;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowDecorations = WindowDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        RequestedThemeVariant = ThemeVariant.Dark;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        
        Bitmap? logo = null;
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("BlackLaunch.logo.png"))
            if (s != null) logo = new Bitmap(s);
        if (logo != null) Icon = new WindowIcon(logo);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _baseLauncherPath = Path.Combine(appData, ".black_launch");
        _sharedPath = Path.Combine(_baseLauncherPath, "shared");

        Directory.CreateDirectory(_baseLauncherPath);
        _configService = new ConfigService(Path.Combine(_baseLauncherPath, "config.json"));
        _config = _configService.Load();

        _playView = BuildPlayView();
        _serversView = BuildServersView();
        
        var root = new DockPanel();
        var titleBar = BuildTitleBar(logo);
        var tabStrip = BuildTabStrip;
        
        DockPanel.SetDock(titleBar, Dock.Top);
        DockPanel.SetDock(tabStrip, Dock.Top);
        root.Children.Add(titleBar);
        root.Children.Add(tabStrip);
        root.Children.Add(_tabContent);

        Content = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderBrush = Themes.Border,
            BorderThickness = new  Thickness(1),
            Background = Themes.WindowBg,
            ClipToBounds = true,
            Child = root
        };

        SelectTab("Play");
        _gameLauncher = new GameLauncher(_sharedPath);
        WireGameLauncherEvents();
        LoadVersionsAsync();
    }

    private Control BuildTitleBar(Bitmap? logo)
    {
        var bar = new Grid { Background = Brushes.Transparent };
        bar.PointerPressed += (s, e) => BeginMoveDrag(e);

        var left = new StackPanel {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            Spacing = 8
        };
        if (logo != null) left.Children.Add(new Image { Source = logo, Width = 18, Height = 18 });
        left.Children.Add(new TextBlock {
            Text = "BlackLaunch",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Foreground = Themes.TextPrimary
        });

        var right = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Spacing = 12
        };
        var settingsButton = SysButton(MakeIcon(Icons.Settings, Themes.IconNeutral), 15,
            Brushes.Transparent, Brushes.Transparent, () => { });
        var minimizeButton = SysButton(MakeIcon(Icons.Minimize, Themes.IconNeutral), 15,
            Themes.MinimizeBtnHover, Themes.MinimizeBtnPressed, () => WindowState = WindowState.Minimized, true);
        var closeButton = SysButton(MakeIcon(Icons.Close, Themes.IconNeutral), 15, Themes.Error, Themes.CloseBtnPressed, Close);
        right.Children.Add(settingsButton);
        right.Children.Add(minimizeButton);
        right.Children.Add(closeButton);

        bar.Children.Add(left);
        bar.Children.Add(right);

        return new Border {
            Height = 32,
            Background = Themes.TitleBarBg,
            CornerRadius = new CornerRadius(12, 12, 0, 0),
            Child = bar
        };
    }

    private static Control SysButton(IconPath icon, double size, IBrush hover, IBrush pressed, Action onClick, bool minimize = false)
    {
        var pill = new Border {
            CornerRadius = new CornerRadius(3),
            Background = Brushes.Transparent
        };
        var root = new Panel {
            Width = 24,
            Height = 20,
            Background = Brushes.Transparent,
            Children = { pill, SizedIcon(icon, size, minimize) }
        };

        bool over = false, down = false;
        void Apply() {
            pill.Background = down ? pressed : over ? hover : Brushes.Transparent;
            icon.Stroke = over ? Themes.TextPrimary : Themes.IconNeutral;
        }
        root.PointerEntered += (_, _) => { over = true; Apply(); };
        root.PointerExited += (_, _) => { over = false; down = false; Apply(); };
        root.PointerPressed += (_, e) => { down = true; Apply(); e.Handled = true; };
        root.PointerReleased += (_, _) => { if (down && over) onClick(); down = false; Apply(); };
        return root;
    }
    
    private static IconPath MakeIcon(string data, IBrush stroke, double thickness = 2) => new() {
        Data = Geometry.Parse(data),
        Stroke = stroke,
        StrokeThickness = thickness,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        Stretch = Stretch.None
    };

    private static Control SizedIcon(IconPath icon, double size, bool minimize = false) => new Viewbox {
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = minimize ? VerticalAlignment.Bottom : VerticalAlignment.Center,
        Child = new Canvas { Width = 24, Height = 24, Children = { icon } }
    };

    private Control BuildTabStrip
    {
        get
        {
            _playTabIcon = MakeIcon(Icons.Play, Themes.IconNeutral);
            _serversTabIcon = MakeIcon(Icons.Servers, Themes.IconNeutral);
            var strip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(12, 6, 16, 0)
            };
            strip.Children.Add(BuildTab("Play", _playTabIcon, out _playTabLabel, out _playUnderline, out _playHoverBg));
            strip.Children.Add(BuildTab("Servers", _serversTabIcon, out _serversTabLabel, out _serversUnderline, out _serversHoverBg));
            return new Border
            {
                Background = Themes.WindowBg,
                BorderBrush = Themes.Divider,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = strip
            };
        }
    }

    private Control BuildTab(string name, IconPath icon, out TextBlock label, out Border underline, out Border hover)
    {
        var lbl = new TextBlock {
            Text = name,
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Themes.TextSecondary
        };
        var row = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { SizedIcon(icon, 15), lbl }
        };
        var hov = new Border {
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Child = row
        };
        var line = new Border {
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = Brushes.Transparent,
            Margin = new Thickness(8, 2, 8, 0)
        };
        var stack = new StackPanel {
            Background = Brushes.Transparent,
            Children = { hov, line }
        };

        stack.PointerEntered += (_, _) => {
            if (_activeTab == name) return;
            hov.Background = Themes.FieldBg;
            lbl.Foreground = Themes.TextPrimary;
            icon.Stroke = Themes.TextPrimary;
        };
        stack.PointerExited += (_, _) => {
            if (_activeTab == name) return;
            hov.Background = Brushes.Transparent;
            lbl.Foreground = Themes.TextSecondary;
            icon.Stroke = Themes.IconNeutral;
        };
        stack.PointerReleased += (_, _) => SelectTab(name);

        label = lbl;
        underline = line;
        hover = hov;
        return stack;
    }
    
    private void SelectTab(string name)
    {
        bool play = name == "Play";
        _activeTab = name;
        _tabContent.Content = play ? _playView : _serversView;

        _playUnderline.Background = play ? Themes.Accent : Brushes.Transparent;
        _serversUnderline.Background = play ? Brushes.Transparent : Themes.Accent;
        _playTabLabel.Foreground = play ? Themes.TextPrimary : Themes.TextSecondary;
        _serversTabLabel.Foreground = play ? Themes.TextSecondary : Themes.TextPrimary;
        _playTabIcon.Stroke = play ? Themes.TextPrimary : Themes.IconNeutral;
        _serversTabIcon.Stroke = play ? Themes.IconNeutral : Themes.TextPrimary;
        _playHoverBg.Background = Brushes.Transparent;
        _serversHoverBg.Background = Brushes.Transparent;
    }

    private Control BuildNicknameField
    {
        get
        {
            _nickIcon = MakeIcon(Icons.User, Themes.IconNeutral);

            _nicknameBox = new TextBox
            {
                Text = _config.Nickname,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Themes.TextPrimary,
                FontSize = 14,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 12, 0),
                InnerLeftContent = new Border
                {
                    Margin = new Thickness(10, 0, 0, 0),
                    Child = SizedIcon(_nickIcon, 16)
                }
            };

            _nickLabel = new TextBlock
            {
                Text = i18n.Get("NicknamePlaceholder"),
                IsHitTestVisible = false,
                Padding = new Thickness(6, 0)
            };

            _nickBorder = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Themes.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                MinHeight = 44,
                Child = _nicknameBox
            };
            var overlay = new Canvas { IsHitTestVisible = false, Children = { _nickLabel } };
            var root = new Grid { Children = { _nickBorder, overlay } };

            foreach (var pc in new string?[] { null, ":pointerover", ":focus", ":focus-within" })
            {
                var sel = pc is null
                    ? new Style(x => x.OfType<TextBox>().Template().OfType<Border>())
                    : new Style(x => x.OfType<TextBox>().Class(pc).Template().OfType<Border>());
                sel.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0)));
                sel.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
                root.Styles.Add(sel);
            }

            SetNickState();
            var dur = TimeSpan.FromMilliseconds(180);
            Easing ease = new CubicEaseOut();
            _nickLabel.Transitions = [
                new DoubleTransition { Property = Canvas.TopProperty, Duration = dur, Easing = ease },
                new DoubleTransition { Property = Canvas.LeftProperty, Duration = dur, Easing = ease },
                new DoubleTransition { Property = TextBlock.FontSizeProperty, Duration = dur, Easing = ease }
            ];
            _nickBorder.Transitions = [ new BrushTransition { Property = Border.BorderBrushProperty, Duration = dur, Easing = ease } ];
            _nickIcon.Transitions = [ new BrushTransition { Property = IconPath.StrokeProperty, Duration = dur, Easing = ease } ];

            _nicknameBox.GotFocus += (_, _) => SetNickState();
            _nicknameBox.LostFocus += (_, _) => SetNickState();
            _nicknameBox.TextChanged += (_, _) => SetNickState();

            return root;
        }
    }

    private void SetNickState()
    {
        bool focused = _nicknameBox.IsFocused;
        bool floated = focused || !string.IsNullOrEmpty(_nicknameBox.Text);

        Canvas.SetTop(_nickLabel, floated ? -8 : 13);
        Canvas.SetLeft(_nickLabel, floated ? 12 : 40);
        _nickLabel.FontSize = floated ? 11 : 14;
        _nickLabel.Foreground = floated && focused ? Themes.Accent : Themes.TextSecondary;
        _nickLabel.Background = floated ? Themes.WindowBg : Brushes.Transparent;

        _nickBorder.BorderBrush = focused ? Themes.Accent : Themes.Border;
        _nickIcon.Stroke = focused ? Themes.Accent : Themes.IconNeutral;
    }
    
    private Control BuildPlayView()
    {
        var nicknameField = BuildNicknameField;

        var loaders = new[] {
            new LoaderOption("Vanilla", Icons.Vanilla),
            new LoaderOption("Fabric", Icons.Fabric),
            new LoaderOption("Forge", Icons.Forge),
            new LoaderOption("Quilt", Icons.Quilt),
            new LoaderOption("NeoForge", Icons.NeoForge),
            new LoaderOption("LiteLoader", Icons.LiteLoader)
        };
        _loaderBox = new ComboBox {
            ItemsSource = loaders,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ItemTemplate = new FuncDataTemplate<LoaderOption>((opt, _) => opt is null ? new Control() : new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Children = {
                    SizedIcon(MakeIcon(opt.IconData, Themes.IconNeutral), 18),
                    new TextBlock { Text = opt.Name, VerticalAlignment = VerticalAlignment.Center, Foreground = Themes.TextPrimary, FontSize = 14 }
                }
            }, false)
        };
        StyleField(_loaderBox);
        _loaderBox.SelectedItem = loaders.FirstOrDefault(l => l.Name == _config.Loader) ?? loaders[0];

        _versionBox = new ComboBox {
            PlaceholderText = i18n.Get("LoadingVersions"),
            IsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        StyleField(_versionBox);
        if (_config.CachedVersions is { Count: > 0 }) {
            _versionBox.ItemsSource = _config.CachedVersions;
            _versionBox.SelectedItem = _config.Version ?? _config.CachedVersions.FirstOrDefault();
            _versionBox.IsEnabled = true;
        }

        _playButton = new Button {
            Content = i18n.Get("Play"),
            Background = Themes.Accent,
            Foreground = Themes.TextPrimary,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            MinHeight = 44,
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _playButton.Click += PlayButton_Click;

        _openFolderButton = new Button {
            Content = SizedIcon(MakeIcon(Icons.Folder, Themes.IconNeutral), 18),
            Width = 48,
            MinHeight = 48,
            CornerRadius = new CornerRadius(10),
            Background = Themes.FieldBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _openFolderButton.Click += OpenFolderButton_Click;
        ToolTip.SetTip(_openFolderButton, i18n.Get("OpenFolderTooltip"));

        var buttonPanel = new Grid {
            ColumnDefinitions = new ColumnDefinitions("*, 8, Auto"),
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetColumn(_playButton, 0);
        Grid.SetColumn(_openFolderButton, 2);
        buttonPanel.Children.Add(_playButton);
        buttonPanel.Children.Add(_openFolderButton);

        _statusText = new TextBlock {
            Text = "", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            FontSize = 12, Foreground = Themes.TextSecondary
        };
        _progressBar = new ProgressBar {
            Minimum = 0, Maximum = 100, Value = 0, Height = 4,
            CornerRadius = new CornerRadius(2),
            Foreground = Themes.Accent, Background = Themes.Border,
            IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _progressDetailText = new TextBlock {
            Text = "", FontSize = 11, IsVisible = false,
            TextAlignment = TextAlignment.Center, Foreground = Themes.TextTertiary
        };

        var label = new TextBlock {
            Text = i18n.Get("LaunchSettings").ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeight.Medium,
            LetterSpacing = 1,
            Foreground = Themes.TextTertiary,
            Margin = new Thickness(2, 0, 0, 10)
        };

        var settingsPanel = new StackPanel {
            Spacing = 12,
            Margin = new Thickness(20, 16, 20, 16),
            Children = { label, nicknameField, _loaderBox, _versionBox, buttonPanel }
        };

        var statusPanel = new StackPanel {
            Spacing = 6,
            Margin = new Thickness(20, 0, 20, 16),
            Children = { _statusText, _progressBar, _progressDetailText }
        };

        var dock = new DockPanel();
        DockPanel.SetDock(statusPanel, Dock.Bottom);
        dock.Children.Add(statusPanel);
        dock.Children.Add(settingsPanel);
        return dock;
    }

    private Control BuildServersView() => new TextBlock {
        Text = "Servers soon..",
        Foreground = Themes.TextTertiary,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

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
        var loader = (_loaderBox.SelectedItem as LoaderOption)?.Name ?? "Vanilla";
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
        _config.Loader = (_loaderBox.SelectedItem as LoaderOption)?.Name ?? "Vanilla";
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
