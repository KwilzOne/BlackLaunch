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
using Avalonia.Controls.Templates;
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
    private TextBlock _progressDetailText = new();

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
    private readonly Config _config = new();
    
    private MinecraftHeadViewer _headViewer = new();
    private TextBlock _profileNameText = new();
    private ComboBox _instanceBox = new();
    private TextBlock _instanceDetailsText = new();
    private readonly Grid _overlayGrid = new();
    private readonly Border _overlayCard = new();
    private Button _editInstanceBtn = new();
    private Button _deleteInstanceBtn = new();

    private readonly HttpClient _httpClient = new();

    enum TabName
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
                Nickname = "Player",
                SkinPath = ""
            };
            _config.Profiles.Add(defaultProfile);
            _config.SelectedProfileId = defaultProfile.Id;
            _configService.Save(_config);
        }

        _config.Instances ??= [];
        if (_config.Instances.Count == 0 && _config.CachedVersions != null && _config.CachedVersions.Count > 0) {
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
            Background = new SolidColorBrush(Color.FromArgb(160, 10, 10, 12)),
            IsVisible = false
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
        UpdateInstancesDropdown();
        UpdateActiveInstanceUI();

        SelectTab(TabName.Play);
        _gameLauncher = new GameLauncher(_sharedPath);
        WireGameLauncherEvents();
        LoadVersionsAsync();
    }

    private Border BuildTitleBar(Bitmap? logo)
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
        
        var settingsButton = new Button
        {
            Width = 24,
            Height = 20,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
        };
        var iconSettings = MakeIcon(Icons.Settings);
        iconSettings.Stroke = Themes.IconNeutral;
        settingsButton.Content = SizedIcon(iconSettings, 15);
        settingsButton.PointerEntered += (s, e) =>
        {
            iconSettings.Stroke = Themes.TextPrimary;
        };
        settingsButton.PointerExited += (s, e) =>
        {
            iconSettings.Stroke = Themes.IconNeutral;
        };
        
        var minimizeButton = new Button
        {
            Width = 24,
            Height = 20,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(0, 5),
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Bottom,
        };
        var iconMinimize = MakeIcon(Icons.Minimize);
        iconMinimize.Stroke = Themes.IconNeutral;
        minimizeButton.Content = SizedIcon(iconMinimize, 15);
        minimizeButton.PointerEntered += (s, e) =>
        {
            iconMinimize.Stroke = Themes.TextPrimary;
        };
        minimizeButton.PointerExited += (s, e) =>
        {
            iconMinimize.Stroke = Themes.IconNeutral;
        };
        minimizeButton.Click += (s, e) =>
        {
            WindowState = WindowState.Minimized;
        };
        
        var closeButton = new Button
        {
            Width = 24,
            Height = 20,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
        };
        var iconClose = MakeIcon(Icons.Close);
        iconClose.Stroke = Themes.IconNeutral;
        closeButton.Content = SizedIcon(iconClose, 15);
        closeButton.PointerEntered += (s, e) =>
        {
            iconClose.Stroke = Themes.TextPrimary;
        };
        closeButton.PointerExited += (s, e) =>
        {
            iconClose.Stroke = Themes.IconNeutral;
        };
        closeButton.Click += (s, e) =>
        {
            Close();
        };
        
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
        Stretch = Stretch.Uniform,
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

    private DockPanel BuildPlayView()
    {
        _headViewer = new MinecraftHeadViewer {
            Width = 44,
            Height = 44,
            Margin = new Thickness(0, 0, 12, 0)
        };

        _profileNameText = new TextBlock {
            Text = i18n.Get("NoProfile"),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Themes.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        };

        var profileBtnIcon = MakeIcon(Icons.Settings);
        profileBtnIcon.Stroke = Themes.IconNeutral;
        var profileSelectBtn = new Button {
            Content = SizedIcon(profileBtnIcon, 16),
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = Themes.FieldBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        profileSelectBtn.Click += (_, _) => ShowManageProfilesModal();

        var profileInner = new Grid {
            ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_headViewer, 0);
        Grid.SetColumn(_profileNameText, 1);
        Grid.SetColumn(profileSelectBtn, 2);
        profileInner.Children.Add(_headViewer);
        profileInner.Children.Add(_profileNameText);
        profileInner.Children.Add(profileSelectBtn);

        var profileCard = new Border {
            Background = Themes.FieldBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8),
            Child = profileInner,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var instanceHeader = new Grid {
            ColumnDefinitions = new ColumnDefinitions("*, Auto"),
            Margin = new Thickness(2, 0, 0, 6)
        };
        instanceHeader.Children.Add(new TextBlock {
            Text = i18n.Get("SelectInstanceLabel"),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1,
            Foreground = Themes.TextTertiary,
            VerticalAlignment = VerticalAlignment.Center
        });

        _instanceBox = new ComboBox {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ItemTemplate = new FuncDataTemplate<Instance>((inst, _) => inst is null ? new Control() : new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Children = {
                    SizedIcon(new IconPath {
                        Data = Geometry.Parse(GetLoaderIcon(inst.Loader)),
                        Stroke = Themes.IconNeutral,
                        StrokeThickness = 2,
                        StrokeLineCap = PenLineCap.Round,
                        StrokeJoin = PenLineJoin.Round,
                        Stretch = Stretch.None
                    }, 18),
                    new TextBlock { Text = inst.Name, VerticalAlignment = VerticalAlignment.Center, Foreground = Themes.TextPrimary, FontSize = 14 }
                }
            }, false)
        };
        StyleField(_instanceBox);
        _instanceBox.SelectionChanged += (s, e) => {
            if (_instanceBox.SelectedItem is Instance selected) {
                _config.SelectedInstanceId = selected.Id;
                _configService.Save(_config);
                UpdateActiveInstanceUI();
            }
        };

        var plusBtnIcon = MakeIcon(Icons.Plus);
        plusBtnIcon.Stroke = Themes.IconNeutral;
        var addInstanceBtn = new Button {
            Content = SizedIcon(plusBtnIcon, 16),
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(8),
            Background = Themes.FieldBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        addInstanceBtn.Click += (_, _) => ShowInstanceModal();
        ToolTip.SetTip(addInstanceBtn, i18n.Get("CreateInstanceTooltip"));
        
        var editBtnIcon = MakeIcon(Icons.Edit);
        editBtnIcon.Stroke = Themes.IconNeutral;
        _editInstanceBtn = new Button {
            Content = SizedIcon(editBtnIcon, 16),
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(8),
            Background = Themes.FieldBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _editInstanceBtn.Click += (_, _) => {
            if (_instanceBox.SelectedItem is Instance selected) ShowInstanceModal(selected);
        };
        ToolTip.SetTip(_editInstanceBtn, i18n.Get("EditInstanceTooltip"));

        var deleteBtnIcon = MakeIcon(Icons.Trash);
        deleteBtnIcon.Stroke = Themes.Error;
        _deleteInstanceBtn = new Button {
            Content = SizedIcon(deleteBtnIcon, 16),
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(8),
            Background = Themes.FieldBg,
            BorderBrush = Themes.Border,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _deleteInstanceBtn.Click += (_, _) => {
            if (_instanceBox.SelectedItem is Instance selected) {
                _config.Instances.Remove(selected);
                if (_config.SelectedInstanceId == selected.Id) _config.SelectedInstanceId = _config.Instances.FirstOrDefault()?.Id ?? "";
                _configService.Save(_config);
                UpdateInstancesDropdown();
                UpdateActiveInstanceUI();
            }
        };
        ToolTip.SetTip(_deleteInstanceBtn, i18n.Get("DeleteInstanceTooltip"));

        var dropdownRow = new Grid {
            ColumnDefinitions = new ColumnDefinitions("*, 8, Auto, 8, Auto, 8, Auto"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetColumn(_instanceBox, 0);
        Grid.SetColumn(addInstanceBtn, 2);
        Grid.SetColumn(_editInstanceBtn, 4);
        Grid.SetColumn(_deleteInstanceBtn, 6);
        dropdownRow.Children.Add(_instanceBox);
        dropdownRow.Children.Add(addInstanceBtn);
        dropdownRow.Children.Add(_editInstanceBtn);
        dropdownRow.Children.Add(_deleteInstanceBtn);

        _instanceDetailsText = new TextBlock {
            Text = "",
            FontSize = 12,
            Foreground = Themes.TextTertiary,
            Margin = new Thickness(4, 0, 0, 16)
        };

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
        
        var openFolderBtnIcon = MakeIcon(Icons.Folder);
        openFolderBtnIcon.Stroke = Themes.IconNeutral;
        _openFolderButton = new Button {
            Content = SizedIcon(openFolderBtnIcon, 18),
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

        var settingsPanel = new StackPanel {
            Spacing = 6,
            Margin = new Thickness(20, 16, 20, 16),
            Children = { profileCard, instanceHeader, dropdownRow, _instanceDetailsText, buttonPanel }
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

    private static TextBlock BuildServersView() => new()
    {
        Text = "Servers soon..",
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
            } catch {}
        }
        try {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BlackLaunch.Resources.Default.png");
            if (stream != null) return new Bitmap(stream);
        } catch {}
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

    private void UpdateInstancesDropdown()
    {
        _instanceBox.ItemsSource = null;
        _instanceBox.ItemsSource = _config.Instances;
        _instanceBox.SelectedItem = _config.Instances.FirstOrDefault(i => i.Id == _config.SelectedInstanceId)
                                    ?? _config.Instances.FirstOrDefault();
    }

    private void UpdateActiveInstanceUI()
    {
        if (_instanceBox.SelectedItem is Instance inst) {
            _instanceDetailsText.Text = inst.Loader == "Vanilla" ? $"Minecraft {inst.Version}" : $"Minecraft {inst.Version} • {inst.Loader} {inst.LoaderVersion}";
            _playButton.IsEnabled = true;
            _editInstanceBtn.IsEnabled = true;
            _deleteInstanceBtn.IsEnabled = true;
        } else {
            _instanceDetailsText.Text = i18n.Get("NoInstanceSelected");
            _playButton.IsEnabled = false;
            _editInstanceBtn.IsEnabled = false;
            _deleteInstanceBtn.IsEnabled = false;
        }
    }

    private void HideModal()
    {
        _overlayCard.Child = null;
        _overlayGrid.IsVisible = false;
    }

    private void ShowManageProfilesModal()
    {
        BuildManageProfilesView();
        _overlayGrid.IsVisible = true;
    }

    private void BuildManageProfilesView()
    {
        var panel = new StackPanel { Spacing = 16 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto") };
        header.Children.Add(new TextBlock {
            Text = i18n.Get("ProfilesTitle"),
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = Themes.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        });
        
        var closeBtn = new Button
        {
            Width = 24,
            Height = 20,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
        };
        var closeBtnIcon = MakeIcon(Icons.Close);
        closeBtnIcon.Stroke = Themes.IconNeutral;
        closeBtn.Content = SizedIcon(closeBtnIcon, 14);
        closeBtn.PointerEntered += (s, e) =>
        {
            closeBtnIcon.Stroke = Themes.TextPrimary;
        };
        closeBtn.PointerExited += (s, e) =>
        {
            closeBtnIcon.Stroke = Themes.IconNeutral;
        };
        closeBtn.Click += (s, e) =>
        {
            HideModal();
        };
        
        Grid.SetColumn(closeBtn, 1);
        header.Styles.Add(new Style(x => x.OfType<Button>().Class(":pointerover").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) },
        });
        header.Styles.Add(new Style(x => x.OfType<Button>().Class(":pressed").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) },
        });
        header.Children.Add(closeBtn);
        panel.Children.Add(header);

        var listPanel = new StackPanel { Spacing = 8 };
        foreach (var profile in _config.Profiles) {
            bool isActive = profile.Id == _config.SelectedProfileId;
            var itemHead = new MinecraftHeadViewer {
                Width = 32,
                Height = 32,
                Skin = LoadProfileSkin(profile.SkinPath),
                VerticalAlignment = VerticalAlignment.Center
            };
            var itemText = new TextBlock {
                Text = profile.Nickname,
                FontSize = 13,
                FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = isActive ? Themes.Accent : Themes.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };

            var actions = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!isActive) {
                var selectBtn = new Button {
                    Content = i18n.Get("SelectProfileBtn"),
                    FontSize = 11,
                    Padding = new Thickness(8, 4),
                    Background = Themes.FieldBg,
                    BorderBrush = Themes.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6)
                };
                selectBtn.Click += (_, _) => {
                    _config.SelectedProfileId = profile.Id;
                    _configService.Save(_config);
                    UpdateActiveProfileUI();
                    BuildManageProfilesView();
                };
                actions.Children.Add(selectBtn);
                
                var deleteBtnIcon = MakeIcon(Icons.Trash);
                deleteBtnIcon.Stroke = Themes.Error;
                var deleteBtn = new Button {
                    Content = SizedIcon(deleteBtnIcon, 12),
                    Padding = new Thickness(8, 4),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(6)
                };
                deleteBtn.Click += (_, _) => {
                    if (!string.IsNullOrEmpty(profile.SkinPath) && File.Exists(profile.SkinPath)) try { File.Delete(profile.SkinPath); } catch {}
                    _config.Profiles.Remove(profile);
                    _configService.Save(_config);
                    BuildManageProfilesView();
                };
                actions.Children.Add(deleteBtn);
            } else {
                actions.Children.Add(new TextBlock {
                    Text = i18n.Get("ActiveProfileTag"),
                    FontSize = 11,
                    Foreground = Themes.TextTertiary,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                });
            }

            var itemGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto") };
            Grid.SetColumn(itemHead, 0);
            Grid.SetColumn(itemText, 1);
            Grid.SetColumn(actions, 2);
            itemGrid.Children.Add(itemHead);
            itemGrid.Children.Add(itemText);
            itemGrid.Children.Add(actions);
            var itemBorder = new Border {
                Padding = new Thickness(8),
                Background = Themes.FieldBg,
                CornerRadius = new CornerRadius(8),
                Child = itemGrid
            };
            listPanel.Children.Add(itemBorder);
        }

        var scroll = new ScrollViewer {
            MaxHeight = 200,
            Content = listPanel
        };
        panel.Children.Add(scroll);

        var createBtn = new Button {
            Content = i18n.Get("CreateProfileBtn"),
            Background = Themes.Accent,
            Foreground = Themes.TextPrimary,
            FontWeight = FontWeight.SemiBold,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        createBtn.Click += (_, _) => ShowCreateProfileModal();
        panel.Children.Add(createBtn);
        _overlayCard.Child = panel;
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
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        selectSkinBtn.Click += async (_, _) => {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null) {
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
                    Title = "Select Skin",
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
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        cancelBtn.Click += (_, _) => BuildManageProfilesView();
        Grid.SetColumn(cancelBtn, 0);

        var saveBtn = new Button {
            Content = i18n.Get("SaveBtn"),
            Background = Themes.Accent,
            Foreground = Themes.TextPrimary,
            FontWeight = FontWeight.SemiBold,
            CornerRadius = new CornerRadius(8),
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
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

            var newProfile = new Profile {
                Id = profileId,
                Nickname = nickname,
                SkinPath = destSkinPath
            };

            _config.Profiles.Add(newProfile);
            _config.SelectedProfileId = newProfile.Id;
            _configService.Save(_config);
            
            UpdateActiveProfileUI();
            BuildManageProfilesView();
        };
        Grid.SetColumn(saveBtn, 2);

        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(saveBtn);
        panel.Children.Add(buttons);

        _overlayCard.Child = panel;
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
        var loaders = new[] { "Vanilla", "Fabric", "Forge", "Quilt", "NeoForge", "LiteLoader" };
        var loaderBox = new ComboBox {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ItemsSource = loaders
        };
        StyleField(loaderBox);
        if (editInstance != null && loaders.Contains(editInstance.Loader)) {
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
        async void UpdateLoaderVersions() {
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
                var versions = await Task.Run(() => FetchLoaderVersionsAsync(selectedGameVersion, selectedLoader), token);
                if (token.IsCancellationRequested) return;
                Dispatcher.UIThread.Post(() => {
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
                });
            } catch {
                if (token.IsCancellationRequested) return;
                Dispatcher.UIThread.Post(() => {
                    loaderVersionBox.PlaceholderText = i18n.Get("ErrorLoadingVersionsPlaceholder");
                    loaderVersionBox.IsEnabled = false;
                });
            }
        }

        gameVersionBox.SelectionChanged += (_, _) => UpdateLoaderVersions();
        loaderBox.SelectionChanged += (_, _) => UpdateLoaderVersions();
        UpdateLoaderVersions();

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
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        cancelBtn.Click += (_, _) => { cts?.Cancel(); HideModal(); };
        Grid.SetColumn(cancelBtn, 0);

        var saveBtn = new Button {
            Content = editInstance == null ? i18n.Get("CreateBtn") : i18n.Get("SaveBtn"),
            Background = Themes.Accent,
            Foreground = Themes.TextPrimary,
            FontWeight = FontWeight.SemiBold,
            CornerRadius = new CornerRadius(8),
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
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

            UpdateInstancesDropdown();
            UpdateActiveInstanceUI();
            HideModal();
        };
        Grid.SetColumn(saveBtn, 2);

        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(saveBtn);
        panel.Children.Add(buttons);

        _overlayCard.Child = panel;
        _overlayGrid.IsVisible = true;
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
                UpdateInstancesDropdown();
                UpdateActiveInstanceUI();
            });
        } catch (Exception ex) {
            Dispatcher.UIThread.Post(() => {
                if (_config.CachedVersions == null || _config.CachedVersions.Count == 0) _statusText.Text = i18n.Get("ErrorFetchingVersions", ex.Message);
            });
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
        _progressBar.Value = 0;
        _progressBar.IsVisible = true;
        _progressDetailText.IsVisible = true;
        _progressDetailText.Text = "";
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
            _progressBar.IsVisible = false;
            _progressDetailText.IsVisible = false;
            _progressBar.Value = 0;
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero) TaskbarProgress.SetState(hwnd, TaskbarProgress.TaskbarStates.NoProgress);
        });
    }
}
