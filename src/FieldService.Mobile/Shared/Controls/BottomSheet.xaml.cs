using System.Windows.Input;

namespace FieldService.Mobile.Shared.Controls;

public partial class BottomSheet : ContentView, ISystemBarInsetsAware
{
    public static readonly Thickness BasePadding = new(16, 10, 16, 28);

    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen),
        typeof(bool),
        typeof(BottomSheet),
        false,
        propertyChanged: (bindable, _, newValue) => ((BottomSheet)bindable).OnOpenChanged((bool)newValue));

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(BottomSheet));

    public static readonly BindableProperty CloseCommandProperty = BindableProperty.Create(nameof(CloseCommand), typeof(ICommand), typeof(BottomSheet));

    public static readonly BindableProperty SheetPaddingProperty = BindableProperty.Create(nameof(SheetPadding), typeof(Thickness), typeof(BottomSheet), BasePadding);

    public BottomSheet()
    {
        InitializeComponent();
        IsVisible = false;
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public Thickness SheetPadding
    {
        get => (Thickness)GetValue(SheetPaddingProperty);
        private set => SetValue(SheetPaddingProperty, value);
    }

    public static Thickness PaddingFor(double navigationBar) =>
        new(BasePadding.Left, BasePadding.Top, BasePadding.Right, BasePadding.Bottom + Math.Max(0, navigationBar));

    public void ApplySystemBarInsets(double statusBar, double navigationBar) => SheetPadding = PaddingFor(navigationBar);

    private void OnOpenChanged(bool isOpen)
    {
        IsVisible = isOpen;
        if (isOpen && Handler is not null && !string.IsNullOrWhiteSpace(Title))
        {
            SemanticScreenReader.Default.Announce(Title);
        }
    }
}
