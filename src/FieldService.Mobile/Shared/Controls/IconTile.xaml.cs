using Microsoft.Maui.Controls.Shapes;

namespace FieldService.Mobile.Shared.Controls;

public partial class IconTile : ContentView
{
    public const double Small = 36;
    public const double Medium = 44;
    public const double Large = 56;

    public static readonly BindableProperty IconDataProperty = BindableProperty.Create(nameof(IconData), typeof(string), typeof(IconTile));

    public static readonly BindableProperty TileSizeProperty = BindableProperty.Create(
        nameof(TileSize),
        typeof(double),
        typeof(IconTile),
        Small,
        propertyChanged: (bindable, _, _) => ((IconTile)bindable).ApplySize());

    public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(nameof(IconSize), typeof(double), typeof(IconTile), 18d);

    public IconTile()
    {
        InitializeComponent();
        ApplySize();
    }

    public string? IconData
    {
        get => (string?)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public double TileSize
    {
        get => (double)GetValue(TileSizeProperty);
        set => SetValue(TileSizeProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        private set => SetValue(IconSizeProperty, value);
    }

    public static (double Radius, double Icon) Geometry(double tileSize) => tileSize switch
    {
        >= Large => (18, 28),
        >= Medium => (14, 22),
        _ => (12, 18),
    };

    private void ApplySize()
    {
        var (radius, icon) = Geometry(TileSize);
        IconSize = icon;
        if (Tile is not null)
        {
            Tile.StrokeShape = new RoundRectangle { CornerRadius = radius };
        }
    }
}
