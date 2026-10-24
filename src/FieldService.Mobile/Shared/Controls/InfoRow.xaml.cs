using System.Windows.Input;
using FieldService.Mobile.Shared.Formatting;

namespace FieldService.Mobile.Shared.Controls;

public partial class InfoRow : ContentView
{
    public static readonly BindableProperty IconDataProperty = BindableProperty.Create(nameof(IconData), typeof(string), typeof(InfoRow));

    public static readonly BindableProperty LabelProperty = BindableProperty.Create(
        nameof(Label),
        typeof(string),
        typeof(InfoRow),
        propertyChanged: (bindable, _, _) => ((InfoRow)bindable).UpdateText());

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value),
        typeof(string),
        typeof(InfoRow),
        propertyChanged: (bindable, _, _) => ((InfoRow)bindable).UpdateText());

    public static readonly BindableProperty SecondaryValueProperty = BindableProperty.Create(
        nameof(SecondaryValue),
        typeof(string),
        typeof(InfoRow),
        propertyChanged: (bindable, _, _) => ((InfoRow)bindable).UpdateText());

    public static readonly BindableProperty ShowsDividerProperty = BindableProperty.Create(nameof(ShowsDivider), typeof(bool), typeof(InfoRow), true);

    public static readonly BindableProperty ShowsActionProperty = BindableProperty.Create(nameof(ShowsAction), typeof(bool), typeof(InfoRow), false);

    public static readonly BindableProperty ActionIconDataProperty = BindableProperty.Create(nameof(ActionIconData), typeof(string), typeof(InfoRow));

    public static readonly BindableProperty ActionDescriptionProperty = BindableProperty.Create(nameof(ActionDescription), typeof(string), typeof(InfoRow));

    public static readonly BindableProperty ActionCommandProperty = BindableProperty.Create(nameof(ActionCommand), typeof(ICommand), typeof(InfoRow));

    public static readonly BindableProperty HasSecondaryValueProperty = BindableProperty.Create(nameof(HasSecondaryValue), typeof(bool), typeof(InfoRow), false);

    public static readonly BindableProperty RowDescriptionProperty = BindableProperty.Create(nameof(RowDescription), typeof(string), typeof(InfoRow), string.Empty);

    public InfoRow()
    {
        InitializeComponent();
    }

    public string? IconData
    {
        get => (string?)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Value
    {
        get => (string?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? SecondaryValue
    {
        get => (string?)GetValue(SecondaryValueProperty);
        set => SetValue(SecondaryValueProperty, value);
    }

    public bool ShowsDivider
    {
        get => (bool)GetValue(ShowsDividerProperty);
        set => SetValue(ShowsDividerProperty, value);
    }

    public bool ShowsAction
    {
        get => (bool)GetValue(ShowsActionProperty);
        set => SetValue(ShowsActionProperty, value);
    }

    public string? ActionIconData
    {
        get => (string?)GetValue(ActionIconDataProperty);
        set => SetValue(ActionIconDataProperty, value);
    }

    public string? ActionDescription
    {
        get => (string?)GetValue(ActionDescriptionProperty);
        set => SetValue(ActionDescriptionProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public bool HasSecondaryValue
    {
        get => (bool)GetValue(HasSecondaryValueProperty);
        private set => SetValue(HasSecondaryValueProperty, value);
    }

    public string RowDescription
    {
        get => (string)GetValue(RowDescriptionProperty);
        private set => SetValue(RowDescriptionProperty, value);
    }

    public static string Describe(string? label, string? value, string? secondaryValue) =>
        string.Join(", ", new[] { label, value, secondaryValue }.Where(part => !string.IsNullOrWhiteSpace(part)))
            .Replace(DisplayFormats.ZeroWidthSpace, string.Empty, StringComparison.Ordinal);

    private void UpdateText()
    {
        HasSecondaryValue = !string.IsNullOrWhiteSpace(SecondaryValue);
        RowDescription = Describe(Label, Value, SecondaryValue);
    }
}
