using AndroidX.Core.View;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Navigation;
using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platform;

namespace FieldService.Mobile;

public static class AndroidPlatformSetup
{
    public const string FieldChromeMappingKey = "FsFieldChrome";
    public const string SystemBarInsetsMappingKey = "FsSystemBarInsets";
    public const string UnclippedMappingKey = "FsUnclipped";

    public static void Configure(MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureLifecycleEvents(events => events.AddAndroid(android => android
            .OnCreate((activity, _) =>
            {
                var window = activity.Window;
                if (window is null)
                {
                    return;
                }

                var insets = WindowCompat.GetInsetsController(window, window.DecorView);
                if (insets is null)
                {
                    return;
                }

                insets.AppearanceLightStatusBars = false;
                insets.AppearanceLightNavigationBars = true;
            })
            .OnPostCreate((activity, _) => HoldSplashUntilStarted(activity))));
        foreach (var key in new[] { FieldChromeMappingKey, nameof(IView.Background), nameof(IView.IsEnabled), nameof(IEntry.IsPassword) })
        {
            EntryHandler.Mapper.AppendToMapping(key, (handler, _) => RemoveFieldChrome(handler.PlatformView));
        }

        foreach (var key in new[] { FieldChromeMappingKey, nameof(IView.Background), nameof(IView.IsEnabled) })
        {
            EditorHandler.Mapper.AppendToMapping(key, (handler, _) => RemoveFieldChrome(handler.PlatformView));
        }

        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<JobCard, TappableCardHandler>();
            handlers.AddHandler<OptionRow, OptionRowHandler>();
            handlers.AddHandler<Label, WrapSafeLabelHandler>();
        });
        ContentViewHandler.Mapper.AppendToMapping(SystemBarInsetsMappingKey, (handler, view) =>
        {
            if (view is ISystemBarInsetsAware aware)
            {
                TrackSystemBarInsets(handler.PlatformView, aware);
            }
        });
        ContentViewHandler.Mapper.AppendToMapping(nameof(IView.Visibility), (handler, view) =>
        {
            if (view is BottomSheet sheet)
            {
                ApplySoftInputMode(handler.PlatformView, sheet.IsOpen);
            }
        });
        LayoutHandler.Mapper.AppendToMapping(UnclippedMappingKey, (handler, _) => KeepEffectsUnclipped(handler.PlatformView));
        BorderHandler.Mapper.AppendToMapping(UnclippedMappingKey, (handler, _) => KeepEffectsUnclipped(handler.PlatformView));
        ContentViewHandler.Mapper.AppendToMapping(UnclippedMappingKey, (handler, _) => KeepEffectsUnclipped(handler.PlatformView));
        CollectionViewHandler.Mapper.AppendToMapping(UnclippedMappingKey, (handler, _) => Unclip(handler.PlatformView));
    }

    private static void KeepEffectsUnclipped(Android.Views.ViewGroup platformView)
    {
        Unclip(platformView);
        platformView.ViewAttachedToWindow += (_, _) => UnclipParent(platformView);
        if (platformView.IsAttachedToWindow)
        {
            UnclipParent(platformView);
        }
    }

    private static void UnclipParent(Android.Views.View platformView)
    {
        if (platformView.Parent is not Android.Views.ViewGroup parent
            || parent is AndroidX.Core.Widget.NestedScrollView or Android.Widget.ScrollView or Android.Widget.HorizontalScrollView)
        {
            return;
        }

        Unclip(parent);
    }

    private static void Unclip(Android.Views.ViewGroup viewGroup)
    {
        viewGroup.SetClipChildren(false);
        viewGroup.SetClipToPadding(false);
    }

    private static void HoldSplashUntilStarted(Android.App.Activity activity)
    {
        var startup = IPlatformApplication.Current?.Services.GetService<AppStartup>();
        var content = activity.FindViewById(Android.Resource.Id.Content);
        if (startup is null || startup.IsComplete || content?.ViewTreeObserver is not { } observer)
        {
            return;
        }

        observer.AddOnPreDrawListener(new SplashHold(content, startup));
    }

    private static void ApplySoftInputMode(Android.Views.View platformView, bool sheetOpen)
    {
        if (platformView.Context?.GetActivity()?.Window is not { } window)
        {
            return;
        }

        window.SetSoftInputMode(sheetOpen ? Android.Views.SoftInput.AdjustResize : Android.Views.SoftInput.AdjustPan);
        if (!sheetOpen && window.DecorView is { } decor)
        {
            WindowCompat.GetInsetsController(window, decor)?.Hide(WindowInsetsCompat.Type.Ime());
        }
    }

    private static void RemoveFieldChrome(Android.Widget.EditText platformView)
    {
        platformView.Background = null;
        platformView.SetPadding(0, platformView.PaddingTop, 0, platformView.PaddingBottom);
    }

    private static void TrackSystemBarInsets(Android.Views.View platformView, ISystemBarInsetsAware aware)
    {
        void Apply()
        {
            var windowInsets = ViewCompat.GetRootWindowInsets(platformView);
            var density = platformView.Resources?.DisplayMetrics?.Density ?? 1f;
            if (windowInsets is null || density <= 0)
            {
                return;
            }

            var statusBar = windowInsets.GetInsets(WindowInsetsCompat.Type.StatusBars() | WindowInsetsCompat.Type.DisplayCutout());
            var navigationBar = windowInsets.GetInsets(WindowInsetsCompat.Type.NavigationBars());
            aware.ApplySystemBarInsets((statusBar?.Top ?? 0) / density, (navigationBar?.Bottom ?? 0) / density);
        }

        platformView.ViewAttachedToWindow += (_, _) => Apply();
        if (platformView.IsAttachedToWindow)
        {
            Apply();
        }
    }

    private sealed class SplashHold(Android.Views.View content, AppStartup startup) : Java.Lang.Object, Android.Views.ViewTreeObserver.IOnPreDrawListener
    {
        public bool OnPreDraw()
        {
            if (startup.HoldsSplash)
            {
                return false;
            }

            content.ViewTreeObserver?.RemoveOnPreDrawListener(this);
            return true;
        }
    }
}
