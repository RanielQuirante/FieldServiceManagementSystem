using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace FieldService.Mobile.Shared.Controls;

public partial class BottomSheet : ContentView, ISystemBarInsetsAware, IKeepInViewHost
{
    public const double TopGap = 24;
    public const double MinimumContentHeight = 160;
    public const double CentredWidth = 480;
    public const double HandleHeight = 5;
    public const double ContentSpacing = 18;
    public const double ContentSide = 16;
    public const double HairlineHeight = 1;
    public const double ContentTopUnderHeader = 8;

    public static readonly Thickness BasePadding = new(0, 10, 0, 28);

    public static readonly BindableProperty HeaderProperty = BindableProperty.Create(
        nameof(Header),
        typeof(View),
        typeof(BottomSheet),
        propertyChanged: (bindable, _, newValue) => ((BottomSheet)bindable).OnHeaderChanged(newValue as View));

    public static readonly BindableProperty HasHeaderProperty = BindableProperty.Create(nameof(HasHeader), typeof(bool), typeof(BottomSheet), false);

    public static readonly BindableProperty ContentPaddingProperty = BindableProperty.Create(nameof(ContentPadding), typeof(Thickness), typeof(BottomSheet), ContentPaddingFor(false));

    public static readonly BindableProperty ShowsHairlineProperty = BindableProperty.Create(nameof(ShowsHairline), typeof(bool), typeof(BottomSheet), false);

    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen),
        typeof(bool),
        typeof(BottomSheet),
        false,
        propertyChanged: (bindable, _, newValue) => ((BottomSheet)bindable).OnOpenChanged((bool)newValue));

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(BottomSheet));

    public static readonly BindableProperty CloseCommandProperty = BindableProperty.Create(nameof(CloseCommand), typeof(ICommand), typeof(BottomSheet));

    public static readonly BindableProperty SheetPaddingProperty = BindableProperty.Create(nameof(SheetPadding), typeof(Thickness), typeof(BottomSheet), BasePadding);

    public static readonly BindableProperty ContentMaxHeightProperty = BindableProperty.Create(nameof(ContentMaxHeight), typeof(double), typeof(BottomSheet), double.PositiveInfinity);

    public static readonly BindableProperty IsCentredProperty = BindableProperty.Create(
        nameof(IsCentred),
        typeof(bool),
        typeof(BottomSheet),
        false,
        propertyChanged: (bindable, _, _) => ((BottomSheet)bindable).ApplyPlacement());

    public static readonly BindableProperty ScrollToEndRequestProperty = BindableProperty.Create(
        nameof(ScrollToEndRequest),
        typeof(int),
        typeof(BottomSheet),
        0,
        propertyChanged: (bindable, oldValue, newValue) => ((BottomSheet)bindable).OnScrollToEndRequested((int)oldValue, (int)newValue));

    private Grid? _root;
    private Border? _surface;
    private BoxView? _handle;
    private ContentView? _header;
    private BoxView? _hairline;
    private ScrollView? _scroll;
    private double _statusBar;
    private double _navigationBar;
    private double _keyboard;
    private VisualElement? _kept;

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

    public double ContentMaxHeight
    {
        get => (double)GetValue(ContentMaxHeightProperty);
        private set => SetValue(ContentMaxHeightProperty, value);
    }

    public bool IsCentred
    {
        get => (bool)GetValue(IsCentredProperty);
        set => SetValue(IsCentredProperty, value);
    }

    public int ScrollToEndRequest
    {
        get => (int)GetValue(ScrollToEndRequestProperty);
        set => SetValue(ScrollToEndRequestProperty, value);
    }

    public View? Header
    {
        get => (View?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public bool HasHeader
    {
        get => (bool)GetValue(HasHeaderProperty);
        private set => SetValue(HasHeaderProperty, value);
    }

    public Thickness ContentPadding
    {
        get => (Thickness)GetValue(ContentPaddingProperty);
        private set => SetValue(ContentPaddingProperty, value);
    }

    public bool ShowsHairline
    {
        get => (bool)GetValue(ShowsHairlineProperty);
        private set => SetValue(ShowsHairlineProperty, value);
    }

    public static Thickness PaddingFor(double navigationBar) => PaddingFor(navigationBar, 0, false);

    public static Thickness PaddingFor(double navigationBar, double keyboard, bool centred) =>
        centred || keyboard > 0
            ? BasePadding
            : new(BasePadding.Left, BasePadding.Top, BasePadding.Right, BasePadding.Bottom + Math.Max(0, navigationBar));

    public static Thickness ContentPaddingFor(bool hasHeader) => new(ContentSide, hasHeader ? ContentTopUnderHeader : 0, ContentSide, 0);

    public static bool HairlineAt(bool hasHeader, double offset) => hasHeader && offset > 0;

    public static double MaxContentHeightFor(double window, double statusBar, double keyboard, Thickness padding, double header) =>
        Math.Max(
            MinimumContentHeight,
            window - Math.Max(0, statusBar) - TopGap - Math.Max(0, keyboard) - padding.Top - padding.Bottom - HandleHeight - ContentSpacing - Math.Max(0, header));

    public static double KeyboardFrom(double rootHeight, double surfaceY, double surfaceHeight) =>
        rootHeight <= 0 || surfaceHeight <= 0 ? 0 : Math.Max(0, Math.Round(rootHeight - (surfaceY + surfaceHeight)));

    public static double KeepInViewOffset(double elementBottom, double viewport, double current) =>
        Math.Max(current, Math.Max(0, elementBottom - viewport));

    public void ApplySystemBarInsets(double statusBar, double navigationBar)
    {
        _statusBar = statusBar;
        _navigationBar = navigationBar;
        UpdateGeometry();
    }

    public void KeepInView(VisualElement? element)
    {
        _kept = element;
        ScheduleKeepInView();
    }

    private void ScheduleKeepInView()
    {
        if (_kept is not null && _scroll is not null)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), ScrollKeptIntoView);
        }
    }

    private void ScrollKeptIntoView()
    {
        if (_kept is not { } element || _scroll is not { } scroll || Content is not { } content)
        {
            return;
        }

        var bottom = element.Height + ContentPadding.Top;
        Element? current = element;
        while (current is VisualElement visual && !ReferenceEquals(visual, content))
        {
            bottom += visual.Y;
            current = visual.Parent;
        }

        if (current is null || scroll.Height <= 0)
        {
            return;
        }

        var target = KeepInViewOffset(bottom, scroll.Height, scroll.ScrollY);
        if (target - scroll.ScrollY >= 1)
        {
            _ = scroll.ScrollToAsync(0, target, true);
        }
    }

    private void OnScrollSizeChanged(object? sender, EventArgs e) => ScheduleKeepInView();

    private void OnScrolled(object? sender, ScrolledEventArgs e) => ApplyHairline(e.ScrollY);

    private void ApplyHairline(double offset)
    {
        ShowsHairline = HairlineAt(HasHeader, offset);
        if (_hairline is not null)
        {
            _hairline.Opacity = ShowsHairline ? 1 : 0;
        }
    }

    private void OnHeaderChanged(View? header)
    {
        HasHeader = header is not null;
        ContentPadding = ContentPaddingFor(HasHeader);
        ApplyHairline(_scroll?.ScrollY ?? 0);
        UpdateGeometry();
    }

    private void OnHeaderSizeChanged(object? sender, EventArgs e) => UpdateGeometry();

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_root is not null)
        {
            _root.PropertyChanged -= OnLayoutChanged;
        }

        if (_surface is not null)
        {
            _surface.PropertyChanged -= OnLayoutChanged;
        }

        if (_scroll is not null)
        {
            _scroll.SizeChanged -= OnScrollSizeChanged;
            _scroll.Scrolled -= OnScrolled;
        }

        if (_header is not null)
        {
            _header.SizeChanged -= OnHeaderSizeChanged;
        }

        _root = GetTemplateChild("SheetRoot") as Grid;
        _surface = GetTemplateChild("SheetSurface") as Border;
        _handle = GetTemplateChild("SheetHandle") as BoxView;
        _header = GetTemplateChild("SheetHeader") as ContentView;
        _hairline = GetTemplateChild("SheetHairline") as BoxView;
        _scroll = GetTemplateChild("SheetScroll") as ScrollView;
        if (_scroll is not null)
        {
            _scroll.SizeChanged += OnScrollSizeChanged;
            _scroll.Scrolled += OnScrolled;
        }

        if (_header is not null)
        {
            _header.SizeChanged += OnHeaderSizeChanged;
        }

        ApplyHairline(_scroll?.ScrollY ?? 0);
        if (_root is not null)
        {
            _root.PropertyChanged += OnLayoutChanged;
        }

        if (_surface is not null)
        {
            _surface.PropertyChanged += OnLayoutChanged;
        }

        ApplyPlacement();
    }

    private void OnLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Height) or nameof(Y))
        {
            UpdateGeometry();
        }
    }

    private void UpdateGeometry()
    {
        if (_root is not null && _surface is not null && !IsCentred)
        {
            var keyboard = KeyboardFrom(_root.Height, _surface.Y, _surface.Height);
            if (Math.Abs(keyboard - _keyboard) >= 1)
            {
                _keyboard = keyboard;
            }
        }

        var padding = PaddingFor(_navigationBar, _keyboard, IsCentred);
        if (SheetPadding != padding)
        {
            SheetPadding = padding;
        }

        var window = _root?.Height ?? Height;
        if (window > 0)
        {
            var header = HasHeader && _header is { Height: > 0 } shown ? shown.Height + HairlineHeight : 0;
            var maximum = MaxContentHeightFor(window, IsCentred ? 0 : _statusBar, _keyboard, padding, header);
            if (Math.Abs(maximum - ContentMaxHeight) >= 1)
            {
                ContentMaxHeight = maximum;
            }
        }
    }

    private void ApplyPlacement()
    {
        if (_surface is null)
        {
            return;
        }

        if (IsCentred)
        {
            _surface.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(28) };
            _surface.VerticalOptions = LayoutOptions.Center;
            _surface.HorizontalOptions = LayoutOptions.Center;
            _surface.WidthRequest = CentredWidth;
            _surface.Margin = new Thickness(16);
        }

        if (_handle is not null)
        {
            _handle.IsVisible = !IsCentred;
        }

        UpdateGeometry();
    }

    private void OnScrollToEndRequested(int previous, int requested)
    {
        if (requested == previous || requested == 0 || _scroll is not { } scroll)
        {
            return;
        }

        _ = scroll.ScrollToAsync(0, Math.Max(0, scroll.ContentSize.Height - scroll.Height), true);
    }

    public static int ReleaseInputs(IEnumerable<object> descendants)
    {
        ArgumentNullException.ThrowIfNull(descendants);

        var released = 0;
        foreach (var input in descendants.OfType<IReleasesInput>().ToList())
        {
            if (input.HoldsInput)
            {
                input.ReleaseInput();
                released++;
            }

            input.EndInput();
        }

        return released;
    }

    private void OnOpenChanged(bool isOpen)
    {
        if (!isOpen)
        {
            ReleaseInputs(this.GetVisualTreeDescendants());
            _kept = null;
        }

        IsVisible = isOpen;
        if (isOpen && Handler is not null && !string.IsNullOrWhiteSpace(Title))
        {
            SemanticScreenReader.Default.Announce(Title);
        }
    }
}
