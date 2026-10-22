namespace FieldService.Mobile.Shared.Controls;

public enum CapsuleVariant
{
    Default,
    Selected,
    OnDark,
}

public partial class CountCapsule : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(CountCapsule));

    public static readonly BindableProperty VariantProperty = BindableProperty.Create(
        nameof(Variant),
        typeof(CapsuleVariant),
        typeof(CountCapsule),
        CapsuleVariant.Default,
        propertyChanged: (bindable, _, _) => ((CountCapsule)bindable).ApplyVariant());

    public CountCapsule()
    {
        InitializeComponent();
        ApplyVariant();
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public CapsuleVariant Variant
    {
        get => (CapsuleVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public static (string Background, string Stroke, string Text) ResourceKeys(CapsuleVariant variant) => variant switch
    {
        CapsuleVariant.Selected => ("FsCapsuleSelectedBrush", "FsTransparentBrush", "FsInk"),
        CapsuleVariant.OnDark => ("FsGlassOnDarkControlBrush", "FsGlassDarkPillStrokeBrush", "FsOnDark"),
        _ => ("FsCapsuleBrush", "FsTransparentBrush", "FsInk"),
    };

    private void ApplyVariant()
    {
        if (Capsule is null)
        {
            return;
        }

        var (background, stroke, text) = ResourceKeys(Variant);
        Capsule.SetDynamicResource(Border.BackgroundProperty, background);
        Capsule.SetDynamicResource(Border.StrokeProperty, stroke);
        Capsule.StrokeThickness = Variant == CapsuleVariant.OnDark ? 1 : 0;
        CountLabel.SetDynamicResource(Label.TextColorProperty, text);
    }
}
