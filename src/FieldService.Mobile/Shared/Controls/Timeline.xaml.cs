namespace FieldService.Mobile.Shared.Controls;

public partial class Timeline : ContentView
{
    public const double TimeOnlyWidth = 72;
    public const double WithDayWidth = 88;

    public static readonly BindableProperty EntriesProperty = BindableProperty.Create(
        nameof(Entries),
        typeof(IReadOnlyList<TimelineEntry>),
        typeof(Timeline),
        propertyChanged: (bindable, _, newValue) => ((Timeline)bindable).TimeColumnWidth = WidthFor(newValue as IReadOnlyList<TimelineEntry>));

    public static readonly BindableProperty TimeColumnWidthProperty = BindableProperty.Create(nameof(TimeColumnWidth), typeof(double), typeof(Timeline), TimeOnlyWidth);

    public Timeline()
    {
        InitializeComponent();
    }

    public IReadOnlyList<TimelineEntry>? Entries
    {
        get => (IReadOnlyList<TimelineEntry>?)GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    public double TimeColumnWidth
    {
        get => (double)GetValue(TimeColumnWidthProperty);
        private set => SetValue(TimeColumnWidthProperty, value);
    }

    public static double WidthFor(IReadOnlyList<TimelineEntry>? entries) =>
        entries is not null && entries.Any(entry => entry.HasDay) ? WithDayWidth : TimeOnlyWidth;
}
