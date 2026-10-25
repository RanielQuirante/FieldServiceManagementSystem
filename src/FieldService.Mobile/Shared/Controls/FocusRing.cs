namespace FieldService.Mobile.Shared.Controls;

public static class FocusRing
{
    public const string FocusStatesGroup = "FocusStates";
    public const string FocusedState = "Focused";
    public const string LightColorKey = "FsFocus";
    public const string DarkColorKey = "FsFocusOnDark";
    public const double Thickness = 2;
    public const double Gap = 2;
    public const double ControlMargin = -(Gap + Thickness);

    public static readonly BindableProperty IsOnDarkProperty = BindableProperty.CreateAttached(
        "IsOnDark",
        typeof(bool),
        typeof(FocusRing),
        false);

    private static readonly BindableProperty _colorKeyProperty = BindableProperty.CreateAttached(
        "ColorKey",
        typeof(string),
        typeof(FocusRing),
        null);

    public static bool GetIsOnDark(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (bool)view.GetValue(IsOnDarkProperty);
    }

    public static void SetIsOnDark(BindableObject view, bool value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(IsOnDarkProperty, value);
    }

    public static Thickness MarginAround(Thickness inset) =>
        new(inset.Left + ControlMargin, inset.Top + ControlMargin, inset.Right + ControlMargin, inset.Bottom + ControlMargin);

    public static double CornerRadiusAround(double radius) => Math.Max(0, radius);

    public static double ContainerRadius(double radius, double margin) => Math.Max(0, radius - ControlMargin + margin);

    public static bool IsEven(Thickness inset) =>
        inset.Left == inset.Top && inset.Top == inset.Right && inset.Right == inset.Bottom;

    public static (Thickness Inset, Thickness Outside) EvenInset(Thickness inset)
    {
        var even = Math.Max(0, Math.Min(Math.Min(inset.Left, inset.Right), Math.Min(inset.Top, inset.Bottom)));
        return (new Thickness(even), new Thickness(inset.Left - even, inset.Top - even, inset.Right - even, inset.Bottom - even));
    }

    public static Color? FocusedColor(VisualElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var focused = VisualStateManager.GetVisualStateGroups(element)
            .FirstOrDefault(group => group.Name == FocusStatesGroup)?
            .States.FirstOrDefault(state => state.Name == FocusedState);
        var setter = focused?.Setters.FirstOrDefault(candidate =>
            candidate.Property == Button.BorderColorProperty || candidate.Property == Border.StrokeProperty);
        return setter?.Value switch
        {
            Color color => color,
            SolidColorBrush brush => brush.Color,
            _ => null,
        };
    }

    public static string ColorKeyFor(VisualElement element, Color darkFocus)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(darkFocus);

        if (element.IsSet(IsOnDarkProperty))
        {
            return GetIsOnDark(element) ? DarkColorKey : LightColorKey;
        }

        if (element.GetValue(_colorKeyProperty) is string known)
        {
            return known;
        }

        return FocusedColor(element) is { } color && color.Equals(darkFocus) ? DarkColorKey : LightColorKey;
    }

    public static string KeepNormalLook(VisualElement element, Color darkFocus)
    {
        ArgumentNullException.ThrowIfNull(element);

        var key = ColorKeyFor(element, darkFocus);
        if (!element.IsSet(IsOnDarkProperty) && element.GetValue(_colorKeyProperty) is null)
        {
            element.SetValue(_colorKeyProperty, key);
        }

        var groups = VisualStateManager.GetVisualStateGroups(element);
        for (var index = groups.Count - 1; index >= 0; index--)
        {
            if (groups[index].Name == FocusStatesGroup)
            {
                groups.RemoveAt(index);
            }
        }

        return key;
    }
}
