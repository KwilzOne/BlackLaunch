using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Transformation;
using IconPath = Avalonia.Controls.Shapes.Path;
using CmlLib.Core;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ModLoaders.QuiltMC;
using CmlLib.Core.ModLoaders.LiteLoader;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using BlackLaunch.Models;
using BlackLaunch.Services;
using BlackLaunch.Platform;
using Path = System.IO.Path;

namespace BlackLaunch.Views;

public class MainWindow : Window
{
    private IconPath _playTabIcon = new();
    private IconPath _serversTabIcon = new();
    private Button _playButton = new();
    private Button _openFolderButton = new();
    private TextBlock _statusText = new();
    private ProgressBar _progressBar = new();

    private readonly ContentControl _tabContent = new();
    private readonly DockPanel _playView;
    private readonly TextBlock _serversView;
    private Border _playUnderline = new();
    private Border _serversUnderline = new();
    private Border _playHoverBg = new();
    private Border _serversHoverBg = new();
    private TextBlock _playTabLabel = new();
    private TextBlock _serversTabLabel = new();

    private readonly string _baseLauncherPath;
    private readonly string _sharedPath;
    private readonly ConfigService _configService;
    private readonly GameLauncher _gameLauncher;
    private readonly Config _config;

    private MinecraftHeadViewer _headViewer = new();
    private TextBlock _profileNameText = new();
    private Viewbox _chevronBox = new();
    private Border _profileCard = new();
    private Border _profilesBody = new();
    private readonly StackPanel _profilesList = new();
    private Popup _profilesPopup = new();

    private IconPath _instanceIcon = new();
    private TextBlock _selectedTitle = new();
    private TextBlock _instanceDetailsText = new();
    private readonly Grid _overlayGrid;
    private readonly Border _overlayCard;
    private Action _refreshInstancesList = () => { };

    private Border _loaderCard = new();
    private TextBlock _loaderStageText = new();
    private TextBlock _loaderPercentText = new();
    private TextBlock _loaderSpeedText = new();
    private Viewbox _loaderSpinnerBox = new();
    private Grid _loaderDetailsRow = new();
    private int _loaderEpoch;

    private static readonly string[] Loaders =
        { "Vanilla", "Fabric", "Forge", "Quilt", "NeoForge", "LiteLoader" };
    private readonly HttpClient _httpClient = new();

    private enum TabName
    {
        Play,
        Servers
    }
    private TabName _activeTab = TabName.Play;

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

        _config.Profiles ??= [];
        if (_config.Profiles.Count == 0) {
            var defaultProfile = new Profile
            {
                Id = Guid.NewGuid().ToString(),
                Nickname = "Player", // HARDCODE
                SkinPath = ""
            };
            _config.Profiles.Add(defaultProfile);
            _config.SelectedProfileId = defaultProfile.Id;
            _configService.Save(_config);
        }

        _config.Instances ??= [];
        _config.CachedVersions ??= [];
        if (_config.Instances.Count == 0 && _config.CachedVersions.Count > 0) {
            var defaultInstance = new Instance
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"Minecraft {_config.CachedVersions[0]}",
                Version = _config.CachedVersions[0],
                Loader = "Vanilla",
                LoaderVersion = ""
            };
            _config.Instances.Add(defaultInstance);
            _config.SelectedInstanceId = defaultInstance.Id;
            _configService.Save(_config);
        }

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

        _overlayGrid = new Grid {
            Background = new SolidColorBrush(Color.FromArgb(160, 10, 10, 12)), // HARDCODE
            IsVisible = false,
            Transitions = new Transitions {
                new DoubleTransition {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(150)
                    
                }
            }
        };
        _overlayCard = new Border {
            Background = Themes.WindowBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Width = 360,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _overlayGrid.Children.Add(_overlayCard);

        var mainGrid = new Grid();
        mainGrid.Children.Add(root);
        mainGrid.Children.Add(_overlayGrid);

        Content = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            Background = Themes.WindowBg,
            ClipToBounds = true,
            Child = mainGrid
        };

        UpdateActiveProfileUI();
        UpdateActiveInstanceUI();

        SelectTab(TabName.Play);
        _gameLauncher = new GameLauncher(_sharedPath);
        WireGameLauncherEvents();
        _ = LoadVersionsAsync();
    }

    private Border BuildTitleBar(Bitmap? logo)
    {
        var bar = new Grid { Background = Brushes.Transparent };
        bar.PointerPressed += (_, e) => BeginMoveDrag(e);

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

        var settingsButton = new Border
        {
            Width = 24,
            Height = 20,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(3)
        };
        var iconSettings = MakeIcon(Icons.Settings);
        iconSettings.Stroke = Themes.IconNeutral;
        var iconWrapper = SizedIcon(iconSettings, 15);
        iconWrapper.RenderTransform = TransformOperations.Parse("rotate(0deg)");
        iconWrapper.Transitions = new Transitions
        {
            new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(450),
                Easing = new Avalonia.Animation.Easings.CubicEaseOut()
            }
        };
        settingsButton.Child = iconWrapper;
        settingsButton.PointerEntered += (_, _) =>
        {
            iconSettings.Stroke = Themes.TextPrimary;
            iconWrapper.RenderTransform = TransformOperations.Parse("rotate(90deg) scale(1)");
        };
        settingsButton.PointerExited += (_, _) =>
        {
            iconSettings.Stroke = Themes.IconNeutral;
            iconWrapper.RenderTransform = TransformOperations.Parse("rotate(0deg) scale(1)");
        };
        settingsButton.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            iconWrapper.RenderTransform = TransformOperations.Parse("rotate(115deg) scale(0.88)");
        };
        settingsButton.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            iconWrapper.RenderTransform = TransformOperations.Parse("rotate(90deg) scale(1)");
        };

        var minimizeButton = new Button
        {
            Width = 24,
            Height = 20,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(0, 5),
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Bottom
        };
        var iconMinimize = MakeIcon(Icons.Minimize);
        iconMinimize.Stroke = Themes.IconNeutral;
        minimizeButton.Content = SizedIcon(iconMinimize, 15);
        minimizeButton.PointerEntered += (_, _) => iconMinimize.Stroke = Themes.TextPrimary;
        minimizeButton.PointerExited += (_, _) => iconMinimize.Stroke = Themes.IconNeutral;
        minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;

        var closeButton = new Button
        {
            Width = 24,
            Height = 20,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0)
        };
        var iconClose = MakeIcon(Icons.Close);
        iconClose.Stroke = Themes.IconNeutral;
        closeButton.Content = SizedIcon(iconClose, 15);
        closeButton.PointerEntered += (_, _) => iconClose.Stroke = Themes.TextPrimary;
        closeButton.PointerExited += (_, _) => iconClose.Stroke = Themes.IconNeutral;
        closeButton.Click += (_, _) => Close();

        right.Styles.Add(new Style(x => x.OfType<Button>().Class(":pointerover").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) }
        });
        right.Styles.Add(new Style(x => x.OfType<Button>().Class(":pressed").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) }
        });
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

    private static IconPath MakeIcon(string data, double thickness = 2) => new() {
        Data = Geometry.Parse(data),
        StrokeThickness = thickness,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        Stretch = Stretch.None
    };

    private static Viewbox SizedIcon(IconPath icon, double size) => new() {
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        Child = new Canvas { Width = 24, Height = 24, Children = { icon } }
    };

    private Border BuildTabStrip
    {
        get
        {
            _playTabIcon = MakeIcon(Icons.Play);
            _playTabIcon.Stroke = Themes.IconNeutral;
            _serversTabIcon = MakeIcon(Icons.Servers);
            _serversTabIcon.Stroke = Themes.IconNeutral;
            var strip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(12, 6, 16, 0)
            };
            strip.Children.Add(BuildTab(TabName.Play, _playTabIcon, out _playTabLabel, out _playUnderline, out _playHoverBg));
            strip.Children.Add(BuildTab(TabName.Servers, _serversTabIcon, out _serversTabLabel, out _serversUnderline, out _serversHoverBg));
            return new Border
            {
                Background = Themes.WindowBg,
                BorderBrush = Themes.Divider,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = strip
            };
        }
    }

    private StackPanel BuildTab(TabName name, IconPath icon, out TextBlock label, out Border underline, out Border hover)
    {
        var lbl = new TextBlock {
            Text = i18n.Get($"{name}Tab"),
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

    private void SelectTab(TabName name)
    {
        bool play = name == TabName.Play;
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

    private static Border MineHeadWrapper(MinecraftHeadViewer viewer, double radius = 8)
    {
        return new Border {
            Child = viewer,
            CornerRadius = new CornerRadius(radius),
            ClipToBounds = true
        };
    }

    private DockPanel BuildPlayView()
    {
        _headViewer = new MinecraftHeadViewer {
            Width = 36,
            Height = 36
        };
        var headWrap = MineHeadWrapper(_headViewer);
        headWrap.Margin = new Thickness(0, 0, 12, 0);

        _profileNameText = new TextBlock {
            Text = i18n.Get("NoProfile"),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Themes.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        };

        var chevronIcon = MakeIcon(Icons.DownArrow);
        chevronIcon.Stroke = Themes.IconNeutral;
        _chevronBox = SizedIcon(chevronIcon, 16);
        _chevronBox.Margin = new Thickness(0, 0, -4, 0);
        var profileSelectBtn = new Button {
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = Themes.SecondaryButtonBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            Content = new StackPanel {
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Orientation = Orientation.Horizontal,
                Children = {
                    new TextBlock {
                        Text = i18n.Get("ChangeBtnText"),
                        Foreground = Themes.TextPrimary,
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    _chevronBox
                }
            }
        };
        profileSelectBtn.Click += (_, _) => ShowManageProfilesPopup();

        var profileInner = new Grid {
            ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(headWrap, 0);
        Grid.SetColumn(_profileNameText, 1);
        Grid.SetColumn(profileSelectBtn, 2);
        profileInner.Children.Add(headWrap);
        profileInner.Children.Add(_profileNameText);
        profileInner.Children.Add(profileSelectBtn);

        _profileCard = new Border {
            Background = Themes.FieldBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8),
            Child = profileInner,
            Margin = new Thickness(20, 14, 20, 12)
        };
        _profilesPopup = BuildChangeProfilesPopup(_profileCard);
        profileInner.Children.Add(_profilesPopup);

        var instancesBlock = BuildInstancesList();

        _instanceIcon = new IconPath {
            Stroke = Themes.IconICard,
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.None
        };
        var barIconBox = new Border {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(11),
            BorderBrush = Themes.IconICardBorder,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = {
                    new GradientStop(Themes.IconICardBgFirst, 0),
                    new GradientStop(Themes.IconICardBgSecond, 1)
                }
            },
            Child = SizedIcon(_instanceIcon, 22)
        };

        _selectedTitle = new TextBlock {
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = Themes.TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _instanceDetailsText = new TextBlock {
            FontSize = 11,
            Foreground = Themes.TextTertiary,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var barInfo = new StackPanel {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 8, 0),
            Children = { _selectedTitle, _instanceDetailsText }
        };

        var folderIcon = MakeIcon(Icons.Folder);
        folderIcon.Stroke = Themes.IconNeutral;
        _openFolderButton = new Button {
            Content = SizedIcon(folderIcon, 16),
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(11),
            Background = Themes.FieldBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0)
        };
        _openFolderButton.Click += OpenFolderButton_Click;
        ToolTip.SetTip(_openFolderButton, i18n.Get("OpenFolderTooltip"));

        var playTriangle = MakeIcon(Icons.Play);
        playTriangle.Stroke = Themes.TextPrimary;
        playTriangle.Fill = Themes.TextPrimary;
        _playButton = new Button {
            Background = Themes.Accent,
            Foreground = Themes.TextPrimary,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Height = 46,
            MinWidth = 140,
            CornerRadius = new CornerRadius(11),
            Content = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children = {
                    SizedIcon(playTriangle, 12),
                    new TextBlock {
                        Text = i18n.Get("Play"),
                        VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = FontWeight.SemiBold
                    }
                }
            }
        };
        _playButton.Click += PlayButton_Click;
        _playButton.Styles.Add(new Style(x => x.Class(":pointerover").Template().OfType<ContentPresenter>()) {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Themes.PlayButtonBgHover) }
        });
        _playButton.Styles.Add(new Style(x => x.Class(":pressed").Template().OfType<ContentPresenter>()) {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Themes.PlayButtonBgPressed) }
        });

        var playBarGrid = new Grid {
            ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto, Auto")
        };
        Grid.SetColumn(barIconBox, 0);
        Grid.SetColumn(barInfo, 1);
        Grid.SetColumn(_openFolderButton, 2);
        Grid.SetColumn(_playButton, 3);
        playBarGrid.Children.Add(barIconBox);
        playBarGrid.Children.Add(barInfo);
        playBarGrid.Children.Add(_openFolderButton);
        playBarGrid.Children.Add(_playButton);

        var playBar = new Border {
            Background = Themes.TitleBarBg,
            BorderBrush = Themes.Divider,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 12, 20, 14),
            Child = playBarGrid
        };

        var spinnerIcon = MakeIcon(Icons.SpinnerIcon, 2.5);
        spinnerIcon.Stroke = Themes.Accent;
        _loaderSpinnerBox = SizedIcon(spinnerIcon, 18);
        var spinStyle = new Style(x => x.OfType<Viewbox>().Class("spinning"));
        spinStyle.Animations.Add(new Animation {
            Duration = TimeSpan.FromSeconds(1.1),
            IterationCount = IterationCount.Infinite,
            Children = {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(RotateTransform.AngleProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(RotateTransform.AngleProperty, 360d) } }
            }
        });
        _loaderSpinnerBox.Styles.Add(spinStyle);

        _loaderStageText = new TextBlock {
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = Themes.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(12, 0, 8, 0)
        };
        _loaderPercentText = new TextBlock {
            FontSize = 17,
            FontWeight = FontWeight.Bold,
            Foreground = Themes.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        };
        var loaderTopRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto") };
        Grid.SetColumn(_loaderSpinnerBox, 0);
        Grid.SetColumn(_loaderStageText, 1);
        Grid.SetColumn(_loaderPercentText, 2);
        loaderTopRow.Children.Add(_loaderSpinnerBox);
        loaderTopRow.Children.Add(_loaderStageText);
        loaderTopRow.Children.Add(_loaderPercentText);

        _progressBar = new ProgressBar {
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = Themes.Border,
            Foreground = new LinearGradientBrush {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = {
                    new GradientStop(((SolidColorBrush)Themes.Accent).Color, 0),
                    new GradientStop(Color.Parse("#C4B5FD"), 1) // HARDCODE
                }
            },
            Margin = new Thickness(0, 12, 0, 10)
        };

        _statusText = new TextBlock {
            FontSize = 13,
            Foreground = Themes.TextSecondary,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        _loaderSpeedText = new TextBlock {
            FontSize = 13,
            Foreground = Themes.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center
        };
        _loaderDetailsRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto") };
        Grid.SetColumn(_statusText, 0);
        Grid.SetColumn(_loaderSpeedText, 1);
        _loaderDetailsRow.Children.Add(_statusText);
        _loaderDetailsRow.Children.Add(_loaderSpeedText);

        _loaderCard = new Border {
            Background = new SolidColorBrush(Color.Parse("#141317"), 0.97), // HARDCODE
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18, 14),
            Margin = new Thickness(14, 0, 14, 10),
            VerticalAlignment = VerticalAlignment.Bottom,
            IsVisible = false,
            Opacity = 0,
            Child = new StackPanel { Children = { loaderTopRow, _progressBar, _loaderDetailsRow } },
            Transitions = new Transitions {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) }
            }
        };

        var centerHost = new Grid();
        centerHost.Children.Add(instancesBlock);
        centerHost.Children.Add(_loaderCard);

        var dock = new DockPanel();
        DockPanel.SetDock(_profileCard, Dock.Top);
        DockPanel.SetDock(playBar, Dock.Bottom);
        dock.Children.Add(_profileCard);
        dock.Children.Add(playBar);
        dock.Children.Add(centerHost);
        return dock;
    }

    private DockPanel BuildInstancesList()
    {
        string searchQuery = "";
        string filterLoader = "";
        string filterVersion = "";
        string sortMode = "recent";
        var filterGroupRefreshers = new List<Action>();

        var accentColor = ((SolidColorBrush)Themes.Accent).Color;
        var activeBadgeBg = new SolidColorBrush(accentColor, 0.18);
        var activeCardBg = new SolidColorBrush(accentColor, 0.06);

        var title = new TextBlock {
            Text = "МОИ СБОРКИ", // HARDCODE
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.5,
            Foreground = Themes.TextTertiary,
            VerticalAlignment = VerticalAlignment.Center
        };
        var instancesCount = new TextBlock {
            Text = _config.Instances.Count.ToString(),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = Themes.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var countBadge = new Border {
            Background = Themes.FieldBg,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(7, 2),
            MinWidth = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Child = instancesCount
        };
        var titleRow = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { title, countBadge }
        };

        var filterIcon = MakeIcon(Icons.Ic_Filter);
        filterIcon.Stroke = Themes.IconNeutral;
        var filterBtn = new Button {
            Content = SizedIcon(filterIcon, 15),
            Width = 40,
            Height = 38,
            CornerRadius = new CornerRadius(10),
            Background = Themes.FieldBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1)
        };

        var plusIcon = MakeIcon(Icons.Plus);
        plusIcon.Stroke = Themes.TextPrimary;
        var createBtn = new Button {
            Background = Themes.Accent,
            Foreground = Themes.TextPrimary,
            CornerRadius = new CornerRadius(10),
            Height = 38,
            Padding = new Thickness(14, 0),
            Content = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                VerticalAlignment = VerticalAlignment.Center,
                Children = {
                    SizedIcon(plusIcon, 15),
                    new TextBlock {
                        Text = "Создать", // HARDCODE
                        FontSize = 14,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
        createBtn.Styles.Add(new Style(x => x.Class(":pointerover").Template().OfType<ContentPresenter>()) {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Themes.PlayButtonBgHover) }
        });
        createBtn.Styles.Add(new Style(x => x.Class(":pressed").Template().OfType<ContentPresenter>()) {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Themes.PlayButtonBgPressed) }
        });
        createBtn.Click += (_, _) => ShowInstanceModal();

        var controls = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new Grid {
            ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto"),
            Margin = new Thickness(20, 2, 20, 10)
        };
        Grid.SetColumn(titleRow, 0);
        Grid.SetColumn(controls, 2);
        header.Children.Add(titleRow);
        header.Children.Add(controls);

        var searchIcon = MakeIcon(Icons.Ic_Search);
        searchIcon.Stroke = Themes.IconNeutral;
        var searchBox = new TextBox {
            PlaceholderText = "Поиск сборки", // HARDCODE
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 0),
            InnerLeftContent = new Border {
                Padding = new Thickness(10, 0, 2, 0),
                Child = SizedIcon(searchIcon, 14)
            }
        };
        StyleField(searchBox);
        searchBox.MinHeight = 40;
        var searchRow = new Border {
            Margin = new Thickness(20, 0, 20, 12),
            Child = searchBox
        };

        var instancesList = new StackPanel {
            Spacing = 8,
            Margin = new Thickness(20, 0, 20, 10)
        };
        var instancesScroll = new ScrollViewer {
            Content = instancesList,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        instancesScroll.Styles.Add(new Style(x => x.OfType<ScrollBar>().Class(":vertical").Template().OfType<Thumb>()) {
            Setters = {
                new Setter(MinWidthProperty, 4d),
                new Setter(WidthProperty, 4d),
                new Setter(MaxWidthProperty, 4d),
                new Setter(RenderTransformProperty, null)
            }
        });
        instancesScroll.Styles.Add(new Style(x => x.OfType<RepeatButton>().Name("PART_LineUpButton")) {
            Setters = { new Setter(IsVisibleProperty, false) }
        });
        instancesScroll.Styles.Add(new Style(x => x.OfType<RepeatButton>().Name("PART_LineDownButton")) {
            Setters = { new Setter(IsVisibleProperty, false) }
        });

        var emptyIcon = MakeIcon(Icons.Ic_Search, 1.6);
        emptyIcon.Stroke = Themes.TextTertiary;
        var emptyResetBtn = new Button {
            Content = new TextBlock {
                Text = "Сбросить фильтры", // HARDCODE
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = Themes.Accent
            },
            Background = Themes.FieldBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Height = 36,
            Padding = new Thickness(18, 0)
        };
        var emptyState = new StackPanel {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12,
            IsVisible = false,
            Children = {
                SizedIcon(emptyIcon, 36),
                new TextBlock {
                    Text = "Ничего не найдено по фильтрам", // HARDCODE
                    FontSize = 13,
                    Foreground = Themes.TextSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                emptyResetBtn
            }
        };

        var resetIcon = MakeIcon(Icons.IcReset, 1.8);
        resetIcon.Stroke = Themes.Accent;
        var resetLink = new Button {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 2),
            CornerRadius = new CornerRadius(6),
            Content = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Children = {
                    SizedIcon(resetIcon, 14),
                    new TextBlock {
                        Text = "Сбросить", // HARDCODE
                        FontSize = 13,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Themes.Accent,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
        resetLink.Styles.Add(new Style(x => x.OfType<Button>().Class(":pointerover").Template().OfType<ContentPresenter>()) {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) }
        });

        var filterHeaderTitle = new TextBlock {
            Text = "Фильтры", // HARDCODE
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = Themes.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        };
        var filterHeaderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto") };
        Grid.SetColumn(filterHeaderTitle, 0);
        Grid.SetColumn(resetLink, 1);
        filterHeaderRow.Children.Add(filterHeaderTitle);
        filterHeaderRow.Children.Add(resetLink);

        var filterPanel = new StackPanel { Spacing = 12, Children = { filterHeaderRow } };

        Control BuildFilterGroup(string label, List<(string val, string text)> options, Func<string> getValue,Action<string> setValue, bool expanded = false)
        {
            var valueText = new TextBlock {
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = Themes.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            };
            var chevron = MakeIcon(Icons.DownArrow);
            chevron.Stroke = Themes.IconNeutral;
            var chevronBox = SizedIcon(chevron, 14);
            chevronBox.RenderTransform = new RotateTransform(expanded ? 180 : 0);

            var headerLabel = new TextBlock {
                Text = label.ToUpper(),
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                LetterSpacing = 0.8,
                Foreground = Themes.TextTertiary,
                VerticalAlignment = VerticalAlignment.Center
            };
            var valueWrap = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { valueText, chevronBox }
            };
            var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto") };
            Grid.SetColumn(headerLabel, 0);
            Grid.SetColumn(valueWrap, 1);
            headerGrid.Children.Add(headerLabel);
            headerGrid.Children.Add(valueWrap);

            var headerBtn = new Border {
                Background = Themes.FieldBg,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = headerGrid
            };
            var optionsList = new StackPanel
            {
                Spacing = 2,
                Margin = new Thickness(0, 6, 0, 0),
                IsVisible = expanded
            };

            void RefreshGroup()
            {
                var cur = getValue();
                valueText.Text = options.FirstOrDefault(o => o.val == cur).text ?? "Все"; // HARDCODE
                optionsList.Children.Clear();
                foreach (var (val, text) in options) {
                    bool sel = val == cur;
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto") };
                    var rowLabel = new TextBlock {
                        Text = text,
                        FontSize = 13,
                        FontWeight = sel ? FontWeight.SemiBold : FontWeight.Normal,
                        Foreground = sel ? Themes.TextPrimary : Themes.TextTertiary,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(rowLabel, 0);
                    row.Children.Add(rowLabel);
                    if (sel) {
                        var check = MakeIcon(Icons.Check);
                        check.Stroke = Themes.Accent;
                        var checkWrap = SizedIcon(check, 15);
                        Grid.SetColumn(checkWrap, 1);
                        row.Children.Add(checkWrap);
                    }
                    var item = new Border {
                        Background = sel ? Themes.SecondaryButtonBg : Brushes.Transparent,
                        CornerRadius = new CornerRadius(7),
                        Padding = new Thickness(10, 8),
                        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                        Child = row
                    };
                    item.PointerReleased += (_, _) => { setValue(val); RefreshGroup(); Rebuild(); };
                    optionsList.Children.Add(item);
                }
            }

            headerBtn.PointerReleased += (_, _) => {
                optionsList.IsVisible = !optionsList.IsVisible;
                chevronBox.RenderTransform = new RotateTransform(optionsList.IsVisible ? 180 : 0);
            };

            filterGroupRefreshers.Add(RefreshGroup);
            RefreshGroup();
            return new StackPanel { Children = { headerBtn, optionsList } };
        }

        // HARDCODE
        var loaderOptions = new List<(string val, string text)> { ("", "Все загрузчики") };
        loaderOptions.AddRange(Loaders.Select(l => (l, l)));
        filterPanel.Children.Add(BuildFilterGroup(
            "Загрузчик",
            loaderOptions,
            () => filterLoader,
            v => filterLoader = v,
            expanded: true));

        var versionOptions = new List<(string val, string text)> { ("", "Все версии") };
        versionOptions.AddRange(_config.Instances
            .Select(i => i.Version)
            .Where(v => !string.IsNullOrEmpty(v)).Distinct().Select(v => (v, v)));
        filterPanel.Children.Add(BuildFilterGroup(
            "Версия",
            versionOptions,
            () => filterVersion,
            v => filterVersion = v));

        var sortOptions = new List<(string val, string text)> {
            ("recent", "Недавние"), ("playtime", "По времени"), ("name", "По имени")
        };
        filterPanel.Children.Add(BuildFilterGroup(
            "Сортировка",
            sortOptions,
            () => sortMode,
            v => sortMode = v
            ));

        var filterPopup = new Popup {
            PlacementTarget = filterBtn,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            IsLightDismissEnabled = true,
            VerticalOffset = 8,
            Child = new Border {
                Background = Themes.WindowBg,
                BorderBrush = Themes.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Width = 280,
                Child = filterPanel
            }
        };
        filterBtn.Click += (_, _) => filterPopup.IsOpen = !filterPopup.IsOpen;
        var filterBtnHost = new Panel { Children = { filterBtn, filterPopup } };
        controls.Children.Add(filterBtnHost);
        controls.Children.Add(createBtn);

        Border BuildCard(Instance inst)
        {
            bool isActive = inst.Id == _config.SelectedInstanceId;

            var boxIcon = MakeIcon(GetLoaderIcon(inst.Loader));
            boxIcon.Stroke = Themes.IconICard;
            var iconBox = new Border {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(12),
                BorderBrush = Themes.IconICardBorder,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = {
                        new GradientStop(Themes.IconICardBgFirst, 0),
                        new GradientStop(Themes.IconICardBgSecond, 1)
                    }
                },
                Child = SizedIcon(boxIcon, 22)
            };

            var nameText = new TextBlock {
                Text = inst.Name,
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                Foreground = Themes.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var nameRow = new DockPanel { HorizontalAlignment = HorizontalAlignment.Left };
            if (isActive) {
                var activeBadge = new Border {
                    Background = activeBadgeBg,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(7, 2),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock {
                        Text = "ВЫБРАНА", // HARDCODE
                        FontSize = 9,
                        FontWeight = FontWeight.Bold,
                        LetterSpacing = 0.8,
                        Foreground = Themes.Accent
                    }
                };
                DockPanel.SetDock(activeBadge, Dock.Right);
                nameRow.Children.Add(activeBadge);
            }
            nameRow.Children.Add(nameText);

            var versionTag = new Border {
                Background = Themes.TagICardBg,
                BorderBrush = Themes.TagICardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2),
                Child = new TextBlock
                {
                    Text = inst.Version,
                    FontSize = 10.5,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Themes.TagICardText
                }
            };
            var loaderTag = new Border {
                Background = Themes.TagICardBg,
                BorderBrush = Themes.TagICardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2),
                Child = new TextBlock {
                    Text = inst.Loader,
                    FontSize = 10.5,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Themes.TagICardText
                }
            };
            var tagsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 6, 0, 0),
                Children = { versionTag, loaderTag }
            };

            var info = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 8, 0),
                Children = { nameRow, tagsRow }
            };

            // HARDCODE
            string lastLaunchText;
            if (inst.LastLaunch is not { } last) {
                lastLaunchText = "—";
            } else {
                var span = DateTime.Now - last;
                lastLaunchText = span.TotalHours < 1 ? "недавно"
                    : span.TotalHours < 24 ? $"{(int)span.TotalHours} ч назад"
                    : span.TotalDays < 2 ? "вчера"
                    : span.TotalDays < 7 ? $"{(int)span.TotalDays} дн назад"
                    : "неделю назад";
            }
            string playtimeText = $"{inst.PlaytimeHours:0} ч в игре";

            var timeInfo = new StackPanel {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 3,
                Children = {
                    new TextBlock
                    {
                        Text = lastLaunchText,
                        FontSize = 12,
                        Foreground = Themes.TextSecondary,
                        HorizontalAlignment = HorizontalAlignment.Right
                    },
                    new TextBlock
                    {
                        Text = playtimeText,
                        FontSize = 11,
                        Foreground = Themes.TextTertiary,
                        HorizontalAlignment = HorizontalAlignment.Right
                    }
                }
            };

            var editIcon = MakeIcon(Icons.Edit);
            editIcon.Stroke = Themes.IconNeutral;
            var editBtn = new Button {
                Content = SizedIcon(editIcon, 14),
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(10),
                Background = Themes.FieldBg,
                BorderBrush = Themes.Border,
                BorderThickness = new Thickness(1)
            };
            editBtn.Click += (_, e) => { e.Handled = true; ShowInstanceModal(inst); };

            var cardFolderIcon = MakeIcon(Icons.Folder);
            cardFolderIcon.Stroke = Themes.IconNeutral;
            var folderBtn = new Button {
                Content = SizedIcon(cardFolderIcon, 14),
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(10),
                Background = Themes.FieldBg,
                BorderBrush = Themes.Border,
                BorderThickness = new Thickness(1)
            };
            folderBtn.Click += (_, e) => {
                e.Handled = true;
                OpenFolder(GetInstancePath(inst));
            };

            var trashIcon = MakeIcon(Icons.Trash);
            trashIcon.Stroke = Themes.Error;
            var trashBtn = new Button {
                Content = SizedIcon(trashIcon, 14),
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(10),
                Background = Themes.FieldBg,
                BorderBrush = Themes.Border,
                BorderThickness = new Thickness(1)
            };
            trashBtn.Click += (_, e) => {
                e.Handled = true;
                _config.Instances.Remove(inst);
                if (_config.SelectedInstanceId == inst.Id)
                    _config.SelectedInstanceId = _config.Instances.FirstOrDefault()?.Id ?? "";
                _configService.Save(_config);
                UpdateActiveInstanceUI();
                Rebuild();
            };

            var actions = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false,
                Children = { editBtn, folderBtn, trashBtn }
            };

            var rightCell = new Panel { VerticalAlignment = VerticalAlignment.Center };
            rightCell.Children.Add(timeInfo);
            rightCell.Children.Add(actions);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *, 128") };
            Grid.SetColumn(iconBox, 0);
            Grid.SetColumn(info, 1);
            Grid.SetColumn(rightCell, 2);
            grid.Children.Add(iconBox);
            grid.Children.Add(info);
            grid.Children.Add(rightCell);

            var card = new Border {
                Background = isActive ? activeCardBg : Themes.InstanceCardBg,
                BorderBrush = isActive ? Themes.Accent : Themes.InstanceCardBorder,
                BorderThickness = new Thickness(isActive ? 1.5 : 1),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(12, 11),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = grid
            };
            card.PointerEntered += (_, _) => {
                timeInfo.IsVisible = false;
                actions.IsVisible = true;
                if (!isActive) card.BorderBrush = Themes.Border;
            };
            card.PointerExited += (_, _) => {
                timeInfo.IsVisible = true;
                actions.IsVisible = false;
                if (!isActive) card.BorderBrush = Themes.InstanceCardBorder;
            };
            card.PointerReleased += (_, e) => {
                if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return;
                _config.SelectedInstanceId = inst.Id;
                _configService.Save(_config);
                UpdateActiveInstanceUI();
                Rebuild();
            };

            return card;
        }

        void Rebuild()
        {
            instancesList.Children.Clear();

            IEnumerable<Instance> items = _config.Instances;
            if (!string.IsNullOrEmpty(filterLoader)) items = items.Where(i => i.Loader == filterLoader);
            if (!string.IsNullOrEmpty(filterVersion)) items = items.Where(i => i.Version == filterVersion);
            if (!string.IsNullOrWhiteSpace(searchQuery)) items = items.Where(i => i.Name.Contains(
                searchQuery, StringComparison.OrdinalIgnoreCase));

            items = sortMode switch {
                "playtime" => items.OrderByDescending(i => i.PlaytimeHours),
                "name" => items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
                _ => items.OrderByDescending(i => i.LastLaunch ?? DateTime.MinValue)
            };

            foreach (var inst in items) instancesList.Children.Add(BuildCard(inst));

            bool empty = instancesList.Children.Count == 0;
            emptyState.IsVisible = empty;
            instancesScroll.IsVisible = !empty;
            instancesCount.Text = _config.Instances.Count.ToString();
        }

        void ResetFilters()
        {
            filterLoader = "";
            filterVersion = "";
            sortMode = "recent";
            searchQuery = "";
            searchBox.Text = "";
            foreach (var refresh in filterGroupRefreshers) refresh();
            Rebuild();
        }

        searchBox.PropertyChanged += (_, e) => {
            if (e.Property == TextBox.TextProperty)
            {
                searchQuery = searchBox.Text ?? "";
                Rebuild();
            }
        };
        resetLink.Click += (_, _) => ResetFilters();
        emptyResetBtn.Click += (_, _) => ResetFilters();

        var contentHost = new Grid();
        contentHost.Children.Add(instancesScroll);
        contentHost.Children.Add(emptyState);

        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(searchRow, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(searchRow);
        dock.Children.Add(contentHost);

        _refreshInstancesList = Rebuild;
        Rebuild();
        return dock;
    }

    private static TextBlock BuildServersView() => new()
    {
        Text = i18n.Get("ServersSoon"),
        Foreground = Themes.TextTertiary,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static string GetLoaderIcon(string loader)
    {
        return loader switch
        {
            "Fabric" => Icons.Fabric,
            "Forge" => Icons.Forge,
            "Quilt" => Icons.Quilt,
            "NeoForge" => Icons.NeoForge,
            "LiteLoader" => Icons.LiteLoader,
            _ => Icons.Vanilla
        };
    }

    private static Bitmap? LoadProfileSkin(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path)) {
            try {
                using var stream = File.OpenRead(path);
                return new Bitmap(stream);
            } catch (Exception ex) {
                Debug.WriteLine($"Failed to load skin '{path}': {ex}");
            }
        }
        try {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BlackLaunch.Resources.Default.png");
            if (stream != null) return new Bitmap(stream);
        } catch (Exception ex) {
            Debug.WriteLine($"Failed to load default skin: {ex}");
        }
        return null;
    }

    private void UpdateActiveProfileUI()
    {
        var profile = _config.Profiles.FirstOrDefault(p => p.Id == _config.SelectedProfileId);
        if (profile != null) {
            _profileNameText.Text = profile.Nickname;
            _headViewer.Skin = LoadProfileSkin(profile.SkinPath);
        } else {
            _profileNameText.Text = i18n.Get("NoProfile");
            _headViewer.Skin = LoadProfileSkin(null);
        }
    }

    private void UpdateActiveInstanceUI()
    {
        var inst = _config.Instances.FirstOrDefault(i => i.Id == _config.SelectedInstanceId);
        if (inst != null) {
            _selectedTitle.Text = inst.Name;
            _instanceDetailsText.Text = inst.Loader == "Vanilla"
                ? inst.Version
                : $"{inst.Version} • {inst.Loader}";
            _instanceIcon.Data = Geometry.Parse(GetLoaderIcon(inst.Loader));

            _playButton.IsEnabled = true;
            _openFolderButton.IsEnabled = true;
        } else {
            _selectedTitle.Text = i18n.Get("NoInstanceSelected");
            _instanceDetailsText.Text = "";
            _instanceIcon.Data = Geometry.Parse(GetLoaderIcon(""));

            _playButton.IsEnabled = false;
            _openFolderButton.IsEnabled = false;
        }
    }

    private void ShowModal(Control content)
    {
        _overlayCard.Child = content;
        _overlayGrid.Opacity = 0;
        _overlayGrid.IsVisible = true;
        _overlayGrid.Opacity = 1;
    }

    private async Task HideModal()
    {
        _overlayGrid.Opacity = 0;
        await Task.Delay(150);
        _overlayGrid.IsVisible = false;
        _overlayCard.Child = null;
    }

    private void ShowManageProfilesPopup()
    {
        _profilesBody.Width = _profileCard.Bounds.Width;
        _profilesPopup.IsOpen = true;
    }

    private Popup BuildChangeProfilesPopup(Control anchor)
    {
        var popup = new Popup
        {
            Name = "Profiles",
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
            VerticalOffset = 6
        };

        _profilesBody = new Border
        {
            Background = Themes.FieldBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4)
        };

        var panel = new StackPanel();

        _profilesList.Spacing = 4;
        _profilesList.Styles.Add(new Style(x => x.OfType<Button>().Class(":pointerover").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Themes.SecondaryButtonBgHover) }
        });
        _profilesList.Styles.Add(new Style(x => x.OfType<Button>().Class(":pressed").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Themes.SecondaryButtonBgPressed) }
        });

        RebuildProfilesPopup();

        // the most beautiful docs:
        // https://github.com/AvaloniaUI/Avalonia/blob/master/src/Avalonia.Themes.Fluent/Controls/ScrollBar.xaml
        const double barSize = 4d;
        var scroll = new ScrollViewer {
            MinHeight = 150,
            MaxHeight = 150,
            Content = _profilesList,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        scroll.Resources["ScrollBarSize"] = barSize;
        _profilesBody.Styles.Add(new Style(x => x.OfType<ScrollBar>()
            .Class(":vertical").Template().OfType<Thumb>())
        {
            Setters =
            {
                new Setter(MinWidthProperty, barSize),
                new Setter(WidthProperty, barSize),
                new Setter(MaxWidthProperty, barSize),
                new Setter(RenderTransformProperty, null)
            }
        });
        _profilesBody.Styles.Add(new Style(x => x.OfType<ScrollBar>()
            .PropertyEquals(ScrollBar.IsExpandedProperty, true)
            .Template().OfType<Thumb>())
        {
            Setters =
            {
                new Setter(MinWidthProperty, barSize),
                new Setter(WidthProperty, barSize),
                new Setter(MaxWidthProperty, barSize),
                new Setter(RenderTransformProperty, null)
            }
        });
        _profilesBody.Styles.Add(new Style(x => x.OfType<RepeatButton>().Name("PART_LineUpButton"))
        {
            Setters = { new Setter(IsVisibleProperty, false) }
        });
        _profilesBody.Styles.Add(new Style(x => x.OfType<RepeatButton>().Name("PART_LineDownButton"))
        {
            Setters = { new Setter(IsVisibleProperty, false) }
        });
        panel.Children.Add(scroll);

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = Themes.Border
        });

        var plusIconCreateBtn = MakeIcon(Icons.Plus);
        plusIconCreateBtn.Stroke = Themes.Accent;
        var createBtn = new Button {
            Content = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children = {
                    SizedIcon(plusIconCreateBtn, 16),
                    new TextBlock {
                        Text = i18n.Get("CreateProfileBtn"),
                        Foreground = Themes.Accent,
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            },
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 8, 0, 4),
            Padding = new Thickness(8, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        createBtn.Classes.Add("add-account-btn");
        createBtn.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(150)
            },
            new TransformOperationsTransition {
                Property = RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(100)
            }
        };
        createBtn.Click += async (_, _) =>
        {
            _profilesPopup.IsOpen = false;
            await Task.Delay(150);
            ShowCreateProfileModal();
        };
        panel.Children.Add(createBtn);
        panel.Styles.Add(new Style(x => x.OfType<Button>().Class(":pointerover").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) }
        });
        panel.Styles.Add(new Style(x => x.OfType<Button>().Class(":pressed").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) }
        });
        panel.Styles.Add(new Style(x => x.OfType<Button>().Class("add-account-btn"))
        {
            Setters = {
                new Setter(OpacityProperty, 0.75),
                new Setter(RenderTransformProperty, TransformOperations.Parse("scale(1)")),
                new Setter(BackgroundProperty, Brushes.Transparent)
            }
        });
        panel.Styles.Add(new Style(x => x.OfType<Button>().Class("add-account-btn").Class(":pointerover"))
        {
            Setters = {
                new Setter(OpacityProperty, 1.0),
                new Setter(BackgroundProperty, Brushes.Transparent)
            }
        });
        panel.Styles.Add(new Style(x => x.OfType<Button>().Class("add-account-btn").Class(":pressed"))
        {
            Setters = {
                new Setter(OpacityProperty, 0.5),
                new Setter(RenderTransformProperty, TransformOperations.Parse("scale(0.96)")),
                new Setter(BackgroundProperty, Brushes.Transparent)
            }
        });

        _profilesBody.Child = panel;
        popup.Opened += (_, _) => _chevronBox.RenderTransform = new RotateTransform(180);
        popup.Closed += (_, _) => _chevronBox.RenderTransform = new RotateTransform(0);
        popup.Child = _profilesBody;
        return popup;
    }

    private void RebuildProfilesPopup()
    {
        _profilesList.Children.Clear();
        foreach (var profile in _config.Profiles) {
            bool isActive = profile.Id == _config.SelectedProfileId;
            var itemHead = new MinecraftHeadViewer {
                Width = 30,
                Height = 30,
                Skin = LoadProfileSkin(profile.SkinPath),
                VerticalAlignment = VerticalAlignment.Center
            };
            var itemHeadWrap = MineHeadWrapper(itemHead);
            var itemText = new TextBlock {
                Text = profile.Nickname,
                FontSize = 13,
                FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = isActive ? Themes.TextPrimary : Themes.TextTertiary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };

            var actions = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!isActive) {
                var deleteBtnIcon = MakeIcon(Icons.Trash);
                deleteBtnIcon.Stroke = Themes.Error;
                var deleteBtn = new Button {
                    Content = SizedIcon(deleteBtnIcon, 12),
                    Padding = new Thickness(8, 4),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(6)
                };
                deleteBtn.Click += (_, e) => {
                    e.Handled = true;
                    if (!string.IsNullOrEmpty(profile.SkinPath) && File.Exists(profile.SkinPath)) {
                        try { File.Delete(profile.SkinPath); }
                        catch (Exception ex) { Debug.WriteLine($"Failed to delete skin: {ex}"); }
                    }
                    _config.Profiles.Remove(profile);
                    _configService.Save(_config);
                    RebuildProfilesPopup();
                };
                actions.Children.Add(deleteBtn);
            } else {
                var checkIcon = MakeIcon(Icons.Check);
                checkIcon.Stroke = Themes.Accent;
                actions.Children.Add(new Border
                {
                    Padding = new Thickness(8, 4),
                    Child = SizedIcon(checkIcon, 16)
                });
            }

            var itemGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto") };
            Grid.SetColumn(itemHeadWrap, 0);
            Grid.SetColumn(itemText, 1);
            Grid.SetColumn(actions, 2);
            itemGrid.Children.Add(itemHeadWrap);
            itemGrid.Children.Add(itemText);
            itemGrid.Children.Add(actions);
            var itemButton = new Button {
                Padding = new Thickness(8),
                Background = isActive ? Themes.SecondaryButtonBg : Brushes.Transparent,
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = itemGrid
            };
            itemButton.Click += (_, _) =>
            {
                _config.SelectedProfileId = profile.Id;
                _configService.Save(_config);
                UpdateActiveProfileUI();
                RebuildProfilesPopup();
                _profilesPopup.IsOpen = false;
            };
            _profilesList.Children.Add(itemButton);
        }
    }

    private void ShowCreateProfileModal()
    {
        var panel = new StackPanel { Spacing = 16 };

        panel.Children.Add(new TextBlock {
            Text = i18n.Get("CreateProfileTitle"),
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = Themes.TextPrimary
        });

        panel.Children.Add(new TextBlock {
            Text = i18n.Get("NicknameLabel"),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Themes.TextTertiary,
            LetterSpacing = 1
        });

        var nickInput = new TextBox {
            PlaceholderText = i18n.Get("EnterNicknamePlaceholder"),
            MinHeight = 40
        };
        StyleField(nickInput);
        panel.Children.Add(nickInput);

        panel.Children.Add(new TextBlock {
            Text = i18n.Get("SkinLabel"),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Themes.TextTertiary,
            LetterSpacing = 1
        });

        string selectedSkinPath = "";
        var selectSkinBtn = new Button {
            Content = i18n.Get("ChooseSkinBtn"),
            Background = Themes.FieldBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        selectSkinBtn.Click += async (_, _) => {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null) {
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
                    Title = i18n.Get("SelectSkinDialogTitle"),
                    AllowMultiple = false,
                    FileTypeFilter = [FilePickerFileTypes.ImagePng]
                });
                if (files.Count > 0) {
                    selectedSkinPath = files[0].Path.LocalPath;
                    selectSkinBtn.Content = Path.GetFileName(selectedSkinPath);
                }
            }
        };
        panel.Children.Add(selectSkinBtn);

        var buttons = new Grid {
            ColumnDefinitions = new ColumnDefinitions("*, 8, *"),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var cancelBtn = new Button {
            Content = i18n.Get("CancelBtn"),
            Background = Themes.FieldBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        cancelBtn.Click += (_, _) => _ = HideModal();
        Grid.SetColumn(cancelBtn, 0);

        var saveBtn = new Button {
            Content = i18n.Get("SaveBtn"),
            Background = Themes.Accent,
            Foreground = Themes.TextPrimary,
            FontWeight = FontWeight.SemiBold,
            CornerRadius = new CornerRadius(8),
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        saveBtn.Click += (_, _) => {
            var nickname = nickInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(nickname)) {
                nickInput.BorderBrush = Themes.Error;
                return;
            }

            var profileId = Guid.NewGuid().ToString();
            string destSkinPath = "";
            if (!string.IsNullOrEmpty(selectedSkinPath) && File.Exists(selectedSkinPath)) {
                try {
                    var skinsDir = Path.Combine(_baseLauncherPath, "skins");
                    Directory.CreateDirectory(skinsDir);
                    destSkinPath = Path.Combine(skinsDir, $"{profileId}.png");
                    File.Copy(selectedSkinPath, destSkinPath, true);
                } catch (Exception ex) {
                    Debug.WriteLine($"Failed to copy skin: {ex}");
                }
            }

            _config.Profiles.Add(new Profile {
                Id = profileId,
                Nickname = nickname,
                SkinPath = destSkinPath
            });
            _config.SelectedProfileId = profileId;
            _configService.Save(_config);

            UpdateActiveProfileUI();
            RebuildProfilesPopup();
            _ = HideModal();
        };
        Grid.SetColumn(saveBtn, 2);

        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(saveBtn);
        panel.Children.Add(buttons);

        ShowModal(panel);
    }

    private async Task<List<string>> FetchLoaderVersionsAsync(string mcVersion, string loader)
    {
        try {
            var path = new MinecraftPath(_sharedPath);
            var launcher = new MinecraftLauncher(path);
            if (loader == "Fabric") {
                var installer = new FabricInstaller(_httpClient);
                var loaders = await installer.GetLoaders(mcVersion);
                return [.. loaders.Select(l => l.Version ?? "").Where(v => !string.IsNullOrEmpty(v))];
            } else if (loader == "Forge") {
                var installer = new ForgeInstaller(launcher);
                var versions = await installer.GetForgeVersions(mcVersion);
                return [.. versions.Select(v => v.ForgeVersionName ?? "").Where(v => !string.IsNullOrEmpty(v))];
            } else if (loader == "Quilt") {
                var installer = new QuiltInstaller(_httpClient);
                var loaders = await installer.GetLoaders(mcVersion);
                return [.. loaders.Select(l => l.Version ?? "").Where(v => !string.IsNullOrEmpty(v))];
            } else if (loader == "NeoForge") {
                var installer = new NeoForgeInstaller(launcher);
                var versions = await installer.GetForgeVersions(mcVersion);
                return [.. versions.Select(v => v.VersionName ?? "").Where(v => !string.IsNullOrEmpty(v))];
            } else if (loader == "LiteLoader") {
                var installer = new LiteLoaderInstaller(_httpClient);
                var loaders = await installer.GetAllLiteLoaders();
                return [.. loaders.Where(l => l.BaseVersion == mcVersion).Select(l => l.Version ?? "").Where(v => !string.IsNullOrEmpty(v))];
            }
        } catch (Exception ex) {
            Debug.WriteLine($"Failed to load versions for {loader} on {mcVersion}: {ex}");
        }
        return [];
    }

    private void ShowInstanceModal(Instance? editInstance = null)
    {
        var panel = new StackPanel { Spacing = 16 };

        panel.Children.Add(new TextBlock {
            Text = editInstance == null ? i18n.Get("CreateInstanceTitle") : i18n.Get("EditInstanceTitle"),
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = Themes.TextPrimary
        });

        panel.Children.Add(new TextBlock {
            Text = i18n.Get("InstanceNameLabel"),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Themes.TextTertiary,
            LetterSpacing = 1
        });
        var nameInput = new TextBox {
            PlaceholderText = i18n.Get("InstanceNamePlaceholder"),
            Text = editInstance?.Name ?? "",
            MinHeight = 40
        };
        StyleField(nameInput);
        panel.Children.Add(nameInput);

        panel.Children.Add(new TextBlock {
            Text = i18n.Get("GameVersionLabel"),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Themes.TextTertiary,
            LetterSpacing = 1
        });
        var gameVersionBox = new ComboBox {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ItemsSource = _config.CachedVersions
        };
        StyleField(gameVersionBox);
        if (editInstance != null && _config.CachedVersions.Contains(editInstance.Version)) {
            gameVersionBox.SelectedItem = editInstance.Version;
        } else if (_config.CachedVersions.Count > 0) gameVersionBox.SelectedIndex = 0;

        panel.Children.Add(gameVersionBox);

        panel.Children.Add(new TextBlock {
            Text = i18n.Get("ModLoaderLabel"),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Themes.TextTertiary,
            LetterSpacing = 1
        });
        var loaderBox = new ComboBox {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ItemsSource = Loaders
        };
        StyleField(loaderBox);
        if (editInstance != null && Loaders.Contains(editInstance.Loader)) {
            loaderBox.SelectedItem = editInstance.Loader;
        } else loaderBox.SelectedIndex = 0;

        panel.Children.Add(loaderBox);

        var loaderVersionLabel = new TextBlock {
            Text = i18n.Get("LoaderVersionLabel"),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Themes.TextTertiary,
            LetterSpacing = 1,
            IsVisible = false
        };
        panel.Children.Add(loaderVersionLabel);

        var loaderVersionBox = new ComboBox {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            IsVisible = false
        };
        StyleField(loaderVersionBox);
        panel.Children.Add(loaderVersionBox);

        CancellationTokenSource? cts = null;
        async Task UpdateLoaderVersionsAsync() {
            cts?.Cancel();
            cts = new CancellationTokenSource();
            var token = cts.Token;
            var selectedGameVersion = gameVersionBox.SelectedItem?.ToString();
            var selectedLoader = loaderBox.SelectedItem?.ToString() ?? "Vanilla";
            if (string.IsNullOrEmpty(selectedGameVersion) || selectedLoader == "Vanilla") {
                loaderVersionLabel.IsVisible = false;
                loaderVersionBox.IsVisible = false;
                return;
            }

            loaderVersionLabel.IsVisible = true;
            loaderVersionBox.IsVisible = true;
            loaderVersionBox.IsEnabled = false;
            loaderVersionBox.ItemsSource = null;
            loaderVersionBox.PlaceholderText = i18n.Get("LoadingVersionsPlaceholder");

            try {
                var versions = await FetchLoaderVersionsAsync(selectedGameVersion, selectedLoader);
                if (token.IsCancellationRequested) return;
                if (versions.Count > 0) {
                    loaderVersionBox.ItemsSource = versions;
                    if (editInstance != null && editInstance.Loader == selectedLoader && versions.Contains(editInstance.LoaderVersion)) {
                        loaderVersionBox.SelectedItem = editInstance.LoaderVersion;
                    } else loaderVersionBox.SelectedIndex = 0;
                    loaderVersionBox.IsEnabled = true;
                    loaderVersionBox.PlaceholderText = "";
                } else {
                    loaderVersionBox.PlaceholderText = i18n.Get("NoVersionsPlaceholder");
                    loaderVersionBox.IsEnabled = false;
                }
            } catch (Exception ex) {
                Debug.WriteLine($"Failed to update loader versions: {ex}");
                if (token.IsCancellationRequested) return;
                loaderVersionBox.PlaceholderText = i18n.Get("ErrorLoadingVersionsPlaceholder");
                loaderVersionBox.IsEnabled = false;
            }
        }

        gameVersionBox.SelectionChanged += (_, _) => _ = UpdateLoaderVersionsAsync();
        loaderBox.SelectionChanged += (_, _) => _ = UpdateLoaderVersionsAsync();
        _ = UpdateLoaderVersionsAsync();

        var buttons = new Grid {
            ColumnDefinitions = new ColumnDefinitions("*, 8, *"),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var cancelBtn = new Button {
            Content = i18n.Get("CancelBtn"),
            Background = Themes.FieldBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        cancelBtn.Click += (_, _) =>
        {
            cts?.Cancel();
            _ = HideModal();
        };
        Grid.SetColumn(cancelBtn, 0);

        var saveBtn = new Button {
            Content = editInstance == null ? i18n.Get("CreateBtn") : i18n.Get("SaveBtn"),
            Background = Themes.Accent,
            Foreground = Themes.TextPrimary,
            FontWeight = FontWeight.SemiBold,
            CornerRadius = new CornerRadius(8),
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        saveBtn.Click += (_, _) => {
            var name = nameInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name)) {
                nameInput.BorderBrush = Themes.Error;
                return;
            }

            var gameVersion = gameVersionBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(gameVersion)) {
                gameVersionBox.BorderBrush = Themes.Error;
                return;
            }

            var loader = loaderBox.SelectedItem?.ToString() ?? "Vanilla";
            var loaderVersion = loaderVersionBox.SelectedItem?.ToString() ?? "";

            if (loader != "Vanilla" && string.IsNullOrEmpty(loaderVersion)) {
                loaderVersionBox.BorderBrush = Themes.Error;
                return;
            }

            cts?.Cancel();
            if (editInstance == null) {
                var newInstance = new Instance {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Version = gameVersion,
                    Loader = loader,
                    LoaderVersion = loaderVersion
                };
                _config.Instances.Add(newInstance);
                _config.SelectedInstanceId = newInstance.Id;
            } else {
                editInstance.Name = name;
                editInstance.Version = gameVersion;
                editInstance.Loader = loader;
                editInstance.LoaderVersion = loaderVersion;
            }

            _configService.Save(_config);

            UpdateActiveInstanceUI();
            _refreshInstancesList();
            _ = HideModal();
        };
        Grid.SetColumn(saveBtn, 2);

        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(saveBtn);
        panel.Children.Add(buttons);

        ShowModal(panel);
    }

    private void WireGameLauncherEvents()
    {
        _gameLauncher.StatusChanged += msg =>
            Dispatcher.UIThread.Post(() => {
                if (string.IsNullOrEmpty(msg)) return;
                if (msg.Contains('/') || msg.Contains('\\')) {
                    _statusText.Text = Path.GetFileName(msg);
                } else {
                    _loaderStageText.Text = msg;
                }
            });

        _gameLauncher.ProgressChanged += p =>
            Dispatcher.UIThread.Post(() => {
                if (!_loaderCard.IsVisible) ShowLoader("Загрузка"); // HARDCODE
                if (p.Percentage > 0 && _progressBar.IsIndeterminate)
                    _progressBar.IsIndeterminate = false;
                _progressBar.Value = p.Percentage;
                _loaderPercentText.Text = $"{p.Percentage}%";
                _loaderSpeedText.Text = p.Speed;
                var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd != IntPtr.Zero) TaskbarProgress.SetProgress(hwnd, p.Percentage, 100);
            });

        _gameLauncher.ProgressFinished += ResetProgressUI;

        _gameLauncher.GameStarted += () =>
            Dispatcher.UIThread.Post(() => {
                ShowLoaderRunning();
                _playButton.Content = i18n.Get("Stop");
                _playButton.IsEnabled = true;
            });

        _gameLauncher.GameExited += () =>
            Dispatcher.UIThread.Post(() => {
                _ = HideLoaderAsync();
                _playButton.Content = i18n.Get("Play");
                _playButton.IsEnabled = true;
                UpdateActiveInstanceUI();
            });
    }

    private async Task LoadVersionsAsync()
    {
        try {
            var path = new MinecraftPath(_sharedPath);
            var launcher = new MinecraftLauncher(path);
            var versions = await launcher.GetAllVersionsAsync();
            var releases = versions.Where(v => v.Type == "release").Select(v => v.Name).ToList();

            _config.CachedVersions = releases;
            if (_config.Instances.Count == 0 && releases.Count > 0) {
                var defaultInstance = new Instance {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"Minecraft {releases[0]}",
                    Version = releases[0],
                    Loader = "Vanilla",
                    LoaderVersion = ""
                };
                _config.Instances.Add(defaultInstance);
                _config.SelectedInstanceId = defaultInstance.Id;
            }
            _configService.Save(_config);
            UpdateActiveInstanceUI();
            _refreshInstancesList();
        } catch (Exception ex) {
            if (_config.CachedVersions.Count == 0) {
                _statusText.Text = i18n.Get("ErrorFetchingVersions", ex.Message);
            }
        }
    }

    private string GetInstancePath(Instance instance)
    {
        var name = string.Join("_", instance.Name.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(name)) name = instance.Id;
        return Path.Combine(_baseLauncherPath, "instances", name);
    }

    private string GetCurrentInstancePath()
    {
        var inst = _config.Instances.FirstOrDefault(i => i.Id == _config.SelectedInstanceId);
        if (inst == null) return Path.Combine(_baseLauncherPath, "instances", "default");
        return GetInstancePath(inst);
    }

    private void OpenFolder(string path)
    {
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

    private void OpenFolderButton_Click(object? sender, RoutedEventArgs e) => OpenFolder(GetCurrentInstancePath());

    private async void PlayButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_gameLauncher.IsRunning) {
            _gameLauncher.Stop();
            return;
        }

        var profile = _config.Profiles.FirstOrDefault(p => p.Id == _config.SelectedProfileId);
        var instance = _config.Instances.FirstOrDefault(i => i.Id == _config.SelectedInstanceId);

        if (profile == null || instance == null) {
            _statusText.Text = i18n.Get("ErrorSelectProfileAndInstance");
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.Nickname)) {
            _statusText.Text = i18n.Get("ErrorEmptyNicknameOrVersion");
            return;
        }

        _playButton.IsEnabled = false;
        ShowLoader("Подготовка к запуску..."); // HARDCODE
        try {
            await _gameLauncher.LaunchAsync(
                profile.Nickname,
                instance.Version,
                instance.Loader,
                instance.LoaderVersion,
                GetInstancePath(instance)
            );
        } catch (Exception ex) {
            _statusText.Text = i18n.Get("ErrorLaunch", ex.Message);
            _playButton.IsEnabled = true;
            ResetProgressUI();
        }
    }

    private void ResetProgressUI()
    {
        Dispatcher.UIThread.Post(() => {
            _ = HideLoaderAsync();
            _progressBar.IsIndeterminate = false;
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero) TaskbarProgress.SetState(hwnd, TaskbarProgress.TaskbarStates.NoProgress);
        });
    }
    
    private void ShowLoader(string stage)
    {
        _loaderEpoch++;
        _loaderStageText.Text = stage;
        _loaderPercentText.Text = "";
        _loaderSpeedText.Text = "";
        _statusText.Text = "";
        _loaderSpinnerBox.IsVisible = true;
        _loaderPercentText.IsVisible = true;
        _progressBar.IsVisible = true;
        _loaderDetailsRow.IsVisible = true;
        _progressBar.IsIndeterminate = true;
        _loaderCard.IsVisible = true;
        _loaderCard.Opacity = 1;
        _loaderSpinnerBox.Classes.Add("spinning");
    }

    private void ShowLoaderRunning()
    {
        _loaderEpoch++;
        _loaderSpinnerBox.Classes.Remove("spinning");
        _loaderSpinnerBox.IsVisible = false;
        _loaderPercentText.IsVisible = false;
        _progressBar.IsVisible = false;
        _loaderDetailsRow.IsVisible = false;
        _loaderStageText.Text = "Игра запущена"; // HARDCODE
        _loaderCard.IsVisible = true;
        _loaderCard.Opacity = 1;
    }

    private async Task HideLoaderAsync()
    {
        var epoch = ++_loaderEpoch;
        _loaderSpinnerBox.Classes.Remove("spinning");
        _loaderCard.Opacity = 0;
        await Task.Delay(180);
        if (epoch == _loaderEpoch) _loaderCard.IsVisible = false;
    }
}