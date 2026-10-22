namespace FieldService.Mobile.Shared.Controls;

public sealed class SystemBarSpacer : ContentView, ISystemBarInsetsAware
{
    public SystemBarSpacer()
    {
        InputTransparent = true;
        HeightRequest = 0;
        AutomationProperties.SetIsInAccessibleTree(this, false);
    }

    public void ApplySystemBarInsets(double statusBar, double navigationBar) => HeightRequest = Math.Max(0, navigationBar);
}
