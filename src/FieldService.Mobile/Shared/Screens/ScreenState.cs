namespace FieldService.Mobile.Shared.Screens;

public enum ScreenState
{
    Loading,
    Content,
    Empty,
    Error,
    Offline,
}

public enum SyncIndicator
{
    Updated,
    Loading,
    Offline,
}

public sealed record ScreenError(string Title, string Message, string? Reference)
{
    public bool HasReference => !string.IsNullOrEmpty(Reference);
}
