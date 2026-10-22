namespace FieldService.Mobile.Shared.Controls;

public sealed class Icon : GraphicsView
{
    public const double ViewBoxSize = 24;
    public const double DefaultSize = 20;
    public const double StrokeWidth = 2;
    public const double SmallStrokeWidth = 2.25;
    public const double SmallSizeLimit = 16;
    public const double DashLength = 3;

    public static readonly BindableProperty PathDataProperty = BindableProperty.Create(
        nameof(PathData),
        typeof(string),
        typeof(Icon),
        propertyChanged: (bindable, _, newValue) => ((Icon)bindable).ApplyPathData(newValue as string));

    public static readonly BindableProperty SizeProperty = BindableProperty.Create(
        nameof(Size),
        typeof(double),
        typeof(Icon),
        DefaultSize,
        propertyChanged: (bindable, _, _) => ((Icon)bindable).ApplySize());

    public static readonly BindableProperty ColorProperty = BindableProperty.Create(
        nameof(Color),
        typeof(Color),
        typeof(Icon),
        propertyChanged: (bindable, _, newValue) => ((Icon)bindable).ApplyColors());

    public static readonly BindableProperty FillColorProperty = BindableProperty.Create(
        nameof(FillColor),
        typeof(Color),
        typeof(Icon),
        propertyChanged: (bindable, _, newValue) => ((Icon)bindable).ApplyColors());

    public static readonly BindableProperty StrokeWidthOverrideProperty = BindableProperty.Create(
        nameof(StrokeWidthOverride),
        typeof(double),
        typeof(Icon),
        0d,
        propertyChanged: (bindable, _, _) => ((Icon)bindable).ApplySize());

    public static readonly BindableProperty IsDashedProperty = BindableProperty.Create(
        nameof(IsDashed),
        typeof(bool),
        typeof(Icon),
        false,
        propertyChanged: (bindable, _, _) => ((Icon)bindable).ApplySize());

    private readonly IconDrawable _drawable = new();

    public Icon()
    {
        Drawable = _drawable;
        InputTransparent = true;
        AutomationProperties.SetIsInAccessibleTree(this, false);
        AutomationProperties.SetExcludedWithChildren(this, true);
        ApplySize();
    }

    public string? PathData
    {
        get => (string?)GetValue(PathDataProperty);
        set => SetValue(PathDataProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Color? Color
    {
        get => (Color?)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public Color? FillColor
    {
        get => (Color?)GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    public double StrokeWidthOverride
    {
        get => (double)GetValue(StrokeWidthOverrideProperty);
        set => SetValue(StrokeWidthOverrideProperty, value);
    }

    public bool IsDashed
    {
        get => (bool)GetValue(IsDashedProperty);
        set => SetValue(IsDashedProperty, value);
    }

    public PathF? Geometry => _drawable.Path;

    public float[]? DashPattern => _drawable.DashPattern;

    public static double StrokeWidthFor(double size) => size <= SmallSizeLimit ? SmallStrokeWidth : StrokeWidth;

    public static double StrokeThicknessFor(double size) => StrokeWidthFor(size) * size / ViewBoxSize;

    public static float[] DashPatternFor(double strokeWidth) => [(float)(DashLength / strokeWidth), (float)(DashLength / strokeWidth)];

    private void ApplyPathData(string? data)
    {
        _drawable.Path = string.IsNullOrWhiteSpace(data) ? null : IconPath.Parse(data);
        Invalidate();
    }

    private void ApplySize()
    {
        var size = Size;
        var strokeWidth = StrokeWidthOverride > 0 ? StrokeWidthOverride : StrokeWidthFor(size);
        WidthRequest = size;
        HeightRequest = size;
        _drawable.Scale = (float)(size / ViewBoxSize);
        _drawable.StrokeWidth = (float)strokeWidth;
        _drawable.DashPattern = IsDashed ? DashPatternFor(strokeWidth) : null;
        Invalidate();
    }

    private void ApplyColors()
    {
        _drawable.Stroke = Color;
        _drawable.Fill = FillColor;
        Invalidate();
    }

    private sealed class IconDrawable : IDrawable
    {
        public PathF? Path { get; set; }

        public float Scale { get; set; } = 1;

        public float StrokeWidth { get; set; } = (float)Icon.StrokeWidth;

        public Color? Stroke { get; set; }

        public Color? Fill { get; set; }

        public float[]? DashPattern { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (Path is null)
            {
                return;
            }

            canvas.SaveState();
            canvas.Scale(Scale, Scale);
            if (Fill is not null)
            {
                canvas.FillColor = Fill;
                canvas.FillPath(Path);
            }

            if (Stroke is not null)
            {
                canvas.StrokeColor = Stroke;
                canvas.StrokeSize = StrokeWidth;
                canvas.StrokeLineCap = LineCap.Round;
                canvas.StrokeLineJoin = LineJoin.Round;
                if (DashPattern is not null)
                {
                    canvas.StrokeDashPattern = DashPattern;
                }

                canvas.DrawPath(Path);
            }

            canvas.RestoreState();
        }
    }
}
