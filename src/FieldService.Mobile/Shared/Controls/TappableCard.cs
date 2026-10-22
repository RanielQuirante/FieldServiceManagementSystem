namespace FieldService.Mobile.Shared.Controls;

public static class TappableCard
{
    public const string AndroidClassName = "android.widget.Button";

    public static bool ActsAsButton(bool isLoading, string? description) => !isLoading && !string.IsNullOrWhiteSpace(description);
}
