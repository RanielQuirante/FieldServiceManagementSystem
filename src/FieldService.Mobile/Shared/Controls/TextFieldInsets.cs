namespace FieldService.Mobile.Shared.Controls;

public static class TextFieldInsets
{
    public const double WinUiLeft = 10;
    public const double WinUiTop = 5;
    public const double WinUiRight = 6;
    public const double WinUiBottom = 6;
    public const double EditorFirstLineDrop = 10;

    public static Thickness WinUiTextBoxPadding { get; } = new(WinUiLeft, WinUiTop, WinUiRight, WinUiBottom);

    public static Thickness WindowsEntryPadding { get; } = new(0, WinUiTop, 0, WinUiBottom);

    public static Thickness WindowsEditorPadding { get; } = new(0, WinUiTop + EditorFirstLineDrop, 0, WinUiBottom);

    public static double TextStart(double contentInset, double leadingInset, double platformLeft) => contentInset + leadingInset + platformLeft;

    public static double FirstLineTop(double contentInset, double editorTop, double platformTop) => contentInset + editorTop + platformTop;
}
