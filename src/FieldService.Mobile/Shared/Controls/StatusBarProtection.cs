namespace FieldService.Mobile.Shared.Controls;

public sealed class StatusBarProtection : ContentView, ISystemBarInsetsAware
{
    public const string BrushKey = "FsNavy950Brush";

    public static readonly BindableProperty IsShownProperty = BindableProperty.Create(
        nameof(IsShown),
        typeof(bool),
        typeof(StatusBarProtection),
        false,
        propertyChanged: (bindable, _, _) => ((StatusBarProtection)bindable).Apply());

    public static readonly BindableProperty InsetProperty = BindableProperty.Create(
        nameof(Inset),
        typeof(double),
        typeof(StatusBarProtection),
        0d,
        propertyChanged: (bindable, _, _) => ((StatusBarProtection)bindable).Apply());

    public static readonly BindableProperty ScrollSourceProperty = BindableProperty.Create(
        nameof(ScrollSource),
        typeof(View),
        typeof(StatusBarProtection),
        propertyChanged: (bindable, oldValue, newValue) => ((StatusBarProtection)bindable).Track(oldValue as View, newValue as View));

    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive),
        typeof(bool),
        typeof(StatusBarProtection),
        true,
        propertyChanged: (bindable, _, _) => ((StatusBarProtection)bindable).Update());

    private double _offset;

    public StatusBarProtection()
    {
        InputTransparent = true;
        VerticalOptions = LayoutOptions.Start;
        AutomationProperties.SetIsInAccessibleTree(this, false);
        AutomationProperties.SetExcludedWithChildren(this, true);
        SetDynamicResource(BackgroundProperty, BrushKey);
        Apply();
    }

    public bool IsShown
    {
        get => (bool)GetValue(IsShownProperty);
        private set => SetValue(IsShownProperty, value);
    }

    public double Inset
    {
        get => (double)GetValue(InsetProperty);
        private set => SetValue(InsetProperty, value);
    }

    public View? ScrollSource
    {
        get => (View?)GetValue(ScrollSourceProperty);
        set => SetValue(ScrollSourceProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static bool IsNeeded(double scrollOffset, double inset) => inset > 0 && scrollOffset > 0;

    public void ApplySystemBarInsets(double statusBar, double navigationBar)
    {
        Inset = Math.Max(0, statusBar);
        Update();
    }

    public void ApplyScrollOffset(double offset)
    {
        _offset = offset;
        Update();
    }

    private void Track(View? previous, View? current)
    {
        switch (previous)
        {
            case ItemsView items:
                items.Scrolled -= OnItemsScrolled;
                break;
            case ScrollView scroll:
                scroll.Scrolled -= OnScrolled;
                break;
        }

        switch (current)
        {
            case ItemsView items:
                items.Scrolled += OnItemsScrolled;
                break;
            case ScrollView scroll:
                scroll.Scrolled += OnScrolled;
                break;
        }
    }

    private void OnItemsScrolled(object? sender, ItemsViewScrolledEventArgs e) => ApplyScrollOffset(e.VerticalOffset);

    private void OnScrolled(object? sender, ScrolledEventArgs e) => ApplyScrollOffset(e.ScrollY);

    private void Update() => IsShown = IsActive && IsNeeded(_offset, Inset);

    private void Apply()
    {
        HeightRequest = Inset;
        IsVisible = IsShown;
    }
}
