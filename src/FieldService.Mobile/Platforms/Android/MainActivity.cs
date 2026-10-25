using Android.App;
using Android.Content.PM;
using Android.Views;
using FieldService.Mobile.Shared.Controls;

namespace FieldService.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is { KeyCode: Keycode.Escape } && Shell.Current?.CurrentPage is { } page && page.GetVisualTreeDescendants().OfType<IClosableSheet>().Any(sheet => sheet.IsOpen))
        {
            if (e.Action == KeyEventActions.Down && e.RepeatCount == 0)
            {
                BottomSheet.CloseOpenSheet(page.GetVisualTreeDescendants());
            }

            return true;
        }

        return base.DispatchKeyEvent(e);
    }
}
