using Avalonia.Media;

namespace BlackLaunch;

public class Themes
{
    private static SolidColorBrush B(string hex) => new(Color.Parse(hex));
    
    public static readonly IBrush WindowBg = B("#0E0E11");
    public static readonly IBrush TitleBarBg = B("#09090B");
    public static readonly IBrush FieldBg = B("#161619");
    public static readonly IBrush FieldHover = B("#1C1C21");
    public static readonly IBrush RowEven = B("#121214");
    
    public static readonly IBrush Accent = B("#7C3AED");
    public static readonly IBrush AccentHover = B("#8B5CF6");
    public static readonly IBrush AccentPressed = B("#6D28D9");

    public static readonly IBrush Error = B("#EF4444");
    public static readonly IBrush ErrorHover = B("#F87171");
    public static readonly IBrush CloseBtnPressed = B("#DC2626");
    public static readonly IBrush MinimizeBtnHover = B("#6B8B9C");
    public static readonly IBrush MinimizeBtnPressed = B("#587585");

    public static readonly IBrush TextPrimary = B("#FAFAFA");
    public static readonly IBrush TextSecondary = B("#71717A");
    public static readonly IBrush TextTertiary = B("#52525B");
    public static readonly IBrush IconNeutral = B("#A1A1AA");

    public static readonly IBrush Border = B("#27272A");
    public static readonly IBrush BorderHover = B("#3F3F46");
    public static readonly IBrush Divider = B("#1A1A1D");
    public static readonly IBrush DividerRow = B("#141417");

    public static readonly IBrush SecondaryButtonBg = B("#222228");
    public static readonly IBrush SecondaryButtonBgHover = B("#2C2C34");
    public static readonly IBrush SecondaryButtonBgPressed = B("#1C1C22");

    public static readonly IBrush PlayButtonBgHover = B("#764DFF");
    public static readonly IBrush PlayButtonBgPressed = B("#522BC7");
    
    public static readonly IBrush InstanceCardBg = B("#13111A");
    public static readonly IBrush InstanceCardBorder = B("#221C2E");
    public static readonly Color IconICardBgFirst = Color.Parse("#1C152B");
    public static readonly Color IconICardBgSecond = Color.Parse("#120E1C");
    public static readonly IBrush IconICardBorder = B("#32254C");
    public static readonly IBrush IconICard = B("#B599FF");
    public static readonly IBrush ChoosingInstanceText = B("#7C758F");
    public static readonly IBrush TagICardBg = B("#1E182C");
    public static readonly IBrush TagICardBorder = B("#312747");
    public static readonly IBrush TagICardText = B("#B5ACCA");
    public static readonly IBrush MetaICardTextName = B("#6E677B");
    public static readonly IBrush MetaICardTextValue = B("#A59EB2");
}