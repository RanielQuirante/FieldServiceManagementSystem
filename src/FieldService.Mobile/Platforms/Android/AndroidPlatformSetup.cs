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
    public const string ContainerMappingKey = "FsContainerNotAStop";

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Android.Views.ViewGroup, FocusabilityBox> _focusability = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentPage, KeyboardWatch> _keyboardWatches = new();
    private static int _resizingPages;

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
            handlers.AddHandler<CheckboxRow, CheckboxRowHandler>();
            handlers.AddHandler<Label, WrapSafeLabelHandler>();
            handlers.AddHandler<Icon, IconHandler>();
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
                ApplySoftInputMode(handler.PlatformView, sheet.IsOpen, hideKeyboard: !sheet.IsOpen);
            }
        });
        PageHandler.Mapper.AppendToMapping(KeyboardState.ResizesPageProperty.PropertyName, (handler, view) =>
        {
            if (view is ContentPage page && KeyboardState.GetResizesPage(page))
            {
                WatchKeyboard(handler.PlatformView, page);
            }
        });
        ViewHandler.ViewMapper.AppendToMapping(AutomationProperties.ExcludedWithChildrenProperty.PropertyName, (handler, view) =>
        {
            if (view is BindableObject element && handler.PlatformView is Android.Views.View platformView)
            {
                RestoreAccessibilityImportance(platformView, element);
                if (platformView is Android.Views.ViewGroup group)
                {
                    KeepFocusOut(group, AccessibilityExclusion.CoversContent(element));
                }
            }
        });
        ViewHandler.ViewMapper.AppendToMapping(TabStop.IsStopProperty.PropertyName, (handler, view) =>
        {
            if (view is BindableObject element && !TabStop.GetIsStop(element) && handler.PlatformView is Android.Views.View platformView)
            {
                platformView.Focusable = false;
            }
        });
        ViewHandler.ViewMapper.AppendToMapping(ListSemantics.IsOwnStopProperty.PropertyName, (handler, view) =>
        {
            if (view is BindableObject element && element.IsSet(ListSemantics.IsOwnStopProperty) && handler.PlatformView is Android.Views.View platformView && OperatingSystem.IsAndroidVersionAtLeast(28))
            {
                platformView.ScreenReaderFocusable = ListSemantics.GetIsOwnStop(element);
            }
        });
        ViewHandler.ViewCommandMapper.AppendToMapping(FocusMoves.FocusWithinCommand, (handler, _, args) =>
        {
            if (args is FocusQuery query && handler.PlatformView is Android.Views.View platformView)
            {
                query.HasFocusWithin = platformView.HasFocus || HasAccessibilityFocusWithin(platformView);
            }
        });
        EntryHandler.CommandMapper.AppendToMapping(FormField.EndInputCommand, (handler, _, _) => EndInputSession(handler.PlatformView));
        EditorHandler.CommandMapper.AppendToMapping(FormField.EndInputCommand, (handler, _, _) => EndInputSession(handler.PlatformView));
        LayoutHandler.Mapper.AppendToMapping(UnclippedMappingKey, (handler, _) => KeepEffectsUnclipped(handler.PlatformView));
        BorderHandler.Mapper.AppendToMapping(UnclippedMappingKey, (handler, _) => KeepEffectsUnclipped(handler.PlatformView));
        ContentViewHandler.Mapper.AppendToMapping(UnclippedMappingKey, (handler, _) => KeepEffectsUnclipped(handler.PlatformView));
        CollectionViewHandler.Mapper.AppendToMapping(UnclippedMappingKey, (handler, _) => Unclip(handler.PlatformView));
        CollectionViewHandler.Mapper.AppendToMapping(ContainerMappingKey, (handler, _) => NotAKeyboardStop(handler.PlatformView));
        ScrollViewHandler.Mapper.AppendToMapping(ContainerMappingKey, (handler, _) => NotAKeyboardStop(handler.PlatformView));
        ContentViewHandler.Mapper.AppendToMapping(ContainerMappingKey, (handler, view) =>
        {
            if (view is Page)
            {
                KeepAncestorsOutOfTabOrder(handler.PlatformView);
            }
        });
    }

    public static void NotAKeyboardStop(Android.Views.View view)
    {
        ArgumentNullException.ThrowIfNull(view);

        view.Focusable = false;
        view.FocusableInTouchMode = false;
    }

    private static void KeepAncestorsOutOfTabOrder(Android.Views.View page)
    {
        void Apply()
        {
            for (var parent = page.Parent; parent is Android.Views.View container; parent = container.Parent)
            {
                NotAKeyboardStop(container);
            }
        }

        page.ViewAttachedToWindow += (_, _) => Apply();
        if (page.IsAttachedToWindow)
        {
            Apply();
        }
    }

    public static bool HasAccessibilityFocusWithin(Android.Views.View view)
    {
        ArgumentNullException.ThrowIfNull(view);

        using (var info = view.CreateAccessibilityNodeInfo())
        {
            if (info?.AccessibilityFocused == true)
            {
                return true;
            }
        }

        if (view is not Android.Views.ViewGroup group)
        {
            return false;
        }

        for (var index = 0; index < group.ChildCount; index++)
        {
            if (group.GetChildAt(index) is { } child && HasAccessibilityFocusWithin(child))
            {
                return true;
            }
        }

        return false;
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

    private static void ApplySoftInputMode(Android.Views.View platformView, bool sheetOpen, bool hideKeyboard)
    {
        if (platformView.Context?.GetActivity()?.Window is not { } window)
        {
            return;
        }

        window.SetSoftInputMode(KeyboardState.UsesResize(sheetOpen, _resizingPages > 0) ? Android.Views.SoftInput.AdjustResize : Android.Views.SoftInput.AdjustPan);
        if (hideKeyboard && window.DecorView is { } decor)
        {
            WindowCompat.GetInsetsController(window, decor)?.Hide(WindowInsetsCompat.Type.Ime());
        }
    }

    private static void WatchKeyboard(Android.Views.View platformView, ContentPage page)
    {
        if (_keyboardWatches.TryGetValue(page, out _))
        {
            return;
        }

        var watch = new KeyboardWatch(platformView, new WeakReference<ContentPage>(page));
        _keyboardWatches.Add(page, watch);
        page.Appearing += (_, _) =>
        {
            _resizingPages++;
            ApplySoftInputMode(platformView, sheetOpen: false, hideKeyboard: false);
            platformView.ViewTreeObserver?.AddOnGlobalLayoutListener(watch);
        };
        page.Disappearing += (_, _) =>
        {
            platformView.ViewTreeObserver?.RemoveOnGlobalLayoutListener(watch);
            watch.Reset();
            _resizingPages = Math.Max(0, _resizingPages - 1);
            ApplySoftInputMode(platformView, sheetOpen: false, hideKeyboard: false);
        };
        if (ReferenceEquals(Shell.Current?.CurrentPage, page))
        {
            _resizingPages++;
            ApplySoftInputMode(platformView, sheetOpen: false, hideKeyboard: false);
            platformView.ViewTreeObserver?.AddOnGlobalLayoutListener(watch);
        }
    }

    private static void RestoreAccessibilityImportance(Android.Views.View platformView, BindableObject element)
    {
        if (AccessibilityExclusion.RestoresImportance(element) && platformView.ImportantForAccessibility == Android.Views.ImportantForAccessibility.NoHideDescendants)
        {
            platformView.ImportantForAccessibility = Android.Views.ImportantForAccessibility.Auto;
        }
    }

    private static void KeepFocusOut(Android.Views.ViewGroup group, bool covered)
    {
        if (covered)
        {
            if (!_focusability.TryGetValue(group, out _))
            {
                _focusability.AddOrUpdate(group, new FocusabilityBox(group.DescendantFocusability, group.Focusable));
                group.DescendantFocusability = Android.Views.DescendantFocusability.BlockDescendants;
                group.Focusable = false;
            }

            return;
        }

        if (_focusability.TryGetValue(group, out var saved))
        {
            group.DescendantFocusability = saved.Value;
            group.Focusable = saved.Focusable;
            _focusability.Remove(group);
        }
    }

    private static void EndInputSession(Android.Widget.EditText field)
    {
        if (field.Parent is not Android.Views.ViewGroup holder
            || field.Context?.GetSystemService(Android.Content.Context.InputMethodService) is not Android.Views.InputMethods.InputMethodManager input
            || !input.InvokeIsActive(field))
        {
            return;
        }

        var descendants = holder.DescendantFocusability;
        var focusable = holder.Focusable;
        var focusableInTouchMode = holder.FocusableInTouchMode;
        holder.DescendantFocusability = Android.Views.DescendantFocusability.BeforeDescendants;
        holder.FocusableInTouchMode = true;
        holder.RequestFocus();
        holder.FocusableInTouchMode = focusableInTouchMode;
        holder.Focusable = focusable;
        holder.DescendantFocusability = descendants;
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

    private sealed class FocusabilityBox(Android.Views.DescendantFocusability value, bool focusable)
    {
        public Android.Views.DescendantFocusability Value { get; } = value;

        public bool Focusable { get; } = focusable;
    }

    private sealed class KeyboardWatch(Android.Views.View view, WeakReference<ContentPage> page) : Java.Lang.Object, Android.Views.ViewTreeObserver.IOnGlobalLayoutListener
    {
        private bool _open;

        public void OnGlobalLayout()
        {
            var open = ViewCompat.GetRootWindowInsets(view)?.IsVisible(WindowInsetsCompat.Type.Ime()) == true;
            if (open == _open || !page.TryGetTarget(out var target))
            {
                return;
            }

            _open = open;
            KeyboardState.SetIsOpen(target, open);
        }

        public void Reset()
        {
            _open = false;
            if (page.TryGetTarget(out var target))
            {
                KeyboardState.SetIsOpen(target, false);
            }
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
