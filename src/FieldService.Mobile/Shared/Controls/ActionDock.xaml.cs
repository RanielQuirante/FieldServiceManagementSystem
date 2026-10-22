namespace FieldService.Mobile.Shared.Controls;

public partial class ActionDock : ContentView, ISystemBarInsetsAware
{
    public const double Bleed = 1;

    public static readonly Thickness BasePadding = new(16, 16, 16, 28);

    public static readonly BindableProperty HelperTextProperty = BindableProperty.Create(
        nameof(HelperText),
        typeof(string),
        typeof(ActionDock),
        propertyChanged: (bindable, _, newValue) => ((ActionDock)bindable).HasHelperText = !string.IsNullOrWhiteSpace(newValue as string));

    public static readonly BindableProperty HasHelperTextProperty = BindableProperty.Create(nameof(HasHelperText), typeof(bool), typeof(ActionDock), false);

    public static readonly BindableProperty DockPaddingProperty = BindableProperty.Create(nameof(DockPadding), typeof(Thickness), typeof(ActionDock), PaddingFor(0));

    public ActionDock()
    {
        InitializeComponent();
    }

    public string? HelperText
    {
        get => (string?)GetValue(HelperTextProperty);
        set => SetValue(HelperTextProperty, value);
    }

    public bool HasHelperText
    {
        get => (bool)GetValue(HasHelperTextProperty);
        private set => SetValue(HasHelperTextProperty, value);
    }

    public Thickness DockPadding
    {
        get => (Thickness)GetValue(DockPaddingProperty);
        private set => SetValue(DockPaddingProperty, value);
    }

    public static Thickness PaddingFor(double navigationBar) =>
        new(BasePadding.Left + Bleed, BasePadding.Top, BasePadding.Right + Bleed, BasePadding.Bottom + Bleed + Math.Max(0, navigationBar));

    public void ApplySystemBarInsets(double statusBar, double navigationBar) => DockPadding = PaddingFor(navigationBar);
}
