using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Semi.Avalonia;

namespace BlackLaunch;

public class App : Application
{
    public override void Initialize()
    {
        var semiTheme = new SemiTheme();
        semiTheme.Resources["SemiColorPrimary"] = new SolidColorBrush(Color.Parse("#7C3AED"));
        semiTheme.Resources["SemiColorPrimaryPointerover"] = new SolidColorBrush(Color.Parse("#6D28D9"));
        this.Styles.Add(semiTheme);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
