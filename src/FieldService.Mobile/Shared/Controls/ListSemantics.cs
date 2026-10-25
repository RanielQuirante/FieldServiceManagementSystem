namespace FieldService.Mobile.Shared.Controls;

public static class ListSemantics
{
    public const string JobListName = "Jobs";

    public static readonly BindableProperty IsOwnStopProperty = BindableProperty.CreateAttached(
        "IsOwnStop",
        typeof(bool),
        typeof(ListSemantics),
        false);

    public static readonly BindableProperty NameProperty = BindableProperty.CreateAttached(
        "Name",
        typeof(string),
        typeof(ListSemantics),
        null);

    public static bool GetIsOwnStop(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (bool)view.GetValue(IsOwnStopProperty);
    }

    public static void SetIsOwnStop(BindableObject view, bool value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(IsOwnStopProperty, value);
    }

    public static string? GetName(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (string?)view.GetValue(NameProperty);
    }

    public static void SetName(BindableObject view, string? value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(NameProperty, value);
    }
}
