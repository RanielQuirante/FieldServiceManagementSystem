using FieldService.Mobile.Shared.Controls;
using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PlatformBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using PlatformCheckBox = Microsoft.UI.Xaml.Controls.CheckBox;
using PlatformRect = Windows.Foundation.Rect;
using PlatformThickness = Microsoft.UI.Xaml.Thickness;

namespace FieldService.Mobile.WinUI;

public static class WindowsPlatformSetup
{
    public const string FieldChromeMappingKey = "FsFieldChrome";
    public const string CheckBoxLookMappingKey = "FsCheckBoxLook";
    public const string CheckBoxBoxPartName = "NormalRectangle";
    public const string FocusRingMappingKey = "FsFocusRing";
    public const string SheetKeysMappingKey = "FsSheetKeys";
    public const string ListSemanticsMappingKey = "FsListSemantics";

    private const string FocusedBorderThicknessKey = "TextControlBorderThemeThicknessFocused";

    private static readonly string[] _uncheckedFillKeys =
    [
        "CheckBoxCheckBackgroundFillUnchecked",
        "CheckBoxCheckBackgroundFillUncheckedPointerOver",
        "CheckBoxCheckBackgroundFillUncheckedPressed",
    ];

    public static void Configure(MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<OptionRow, KeyboardRowHandler>();
            handlers.AddHandler<CheckboxRow, KeyboardRowHandler>();
            handlers.AddHandler<OptionGroup, OptionGroupHandler>();
        });
        EntryHandler.Mapper.AppendToMapping(FieldChromeMappingKey, (handler, _) =>
        {
            handler.PlatformView.BorderThickness = new PlatformThickness(0);
            handler.PlatformView.Resources[FocusedBorderThicknessKey] = new PlatformThickness(0);
            handler.PlatformView.Padding = ToPlatform(TextFieldInsets.WindowsEntryPadding);
        });
        EditorHandler.Mapper.AppendToMapping(FieldChromeMappingKey, (handler, _) =>
        {
            handler.PlatformView.BorderThickness = new PlatformThickness(0);
            handler.PlatformView.Resources[FocusedBorderThicknessKey] = new PlatformThickness(0);
            handler.PlatformView.Padding = ToPlatform(TextFieldInsets.WindowsEditorPadding);
        });
        CheckBoxHandler.Mapper.AppendToMapping(CheckBoxLookMappingKey, (handler, _) =>
        {
            if (Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(CheckBoxLook.FillKey, out var value) == true && value is Color fill)
            {
                var brush = new PlatformBrush(fill.ToWindowsColor());
                foreach (var key in _uncheckedFillKeys)
                {
                    handler.PlatformView.Resources[key] = brush;
                }
            }

            handler.PlatformView.Loaded -= FitCheckBoxBox;
            handler.PlatformView.Loaded += FitCheckBoxBox;
            handler.PlatformView.SizeChanged -= FitCheckBoxBoxOnResize;
            handler.PlatformView.SizeChanged += FitCheckBoxBoxOnResize;
        });
        LabelHandler.Mapper.AppendToMapping(TextSelection.MappingKey, (handler, view) =>
        {
            if (view is BindableObject label)
            {
                handler.PlatformView.IsTextSelectionEnabled = TextSelection.GetIsEnabled(label);
            }
        });
        foreach (var key in new[] { nameof(ILabel.LineHeight), nameof(ITextStyle.Font), LineHeights.SpecProperty.PropertyName })
        {
            LabelHandler.Mapper.AppendToMapping(key, (handler, view) =>
            {
                if (view is Label label)
                {
                    WindowsLineHeights.Apply(handler.PlatformView, label.LineHeight, LineHeights.GetSpec(label));
                }
            });
        }

        foreach (var key in new[] { ListSemantics.IsOwnStopProperty.PropertyName, nameof(ILabel.Text) })
        {
            LabelHandler.Mapper.AppendToMapping(key, (handler, view) =>
            {
                if (view is Label label && ListSemantics.GetIsOwnStop(label))
                {
                    ListItems.AttachGroupHeader(handler.PlatformView, label);
                }
            });
        }

        LabelHandler.CommandMapper.PrependToMapping(nameof(IView.Focus), (handler, view, _) => TextTargets.PrepareForFocus(handler.PlatformView, view as BindableObject));
        LabelHandler.CommandMapper.AppendToMapping(ListFocus.HeadingFocusCommand, (handler, _, args) =>
        {
            if (args is FocusQuery query)
            {
                query.HasFocusWithin = ListItems.FocusGroupHeader(handler.PlatformView);
            }
        });
        foreach (var key in new[] { FocusRingMappingKey, nameof(VisualElement.Style), FocusRing.IsOnDarkProperty.PropertyName })
        {
            ButtonHandler.Mapper.AppendToMapping(key, (handler, view) =>
            {
                if (view is Button button)
                {
                    FocusVisuals.Apply(handler.PlatformView, FocusRing.KeepNormalLook(button, FocusVisuals.DarkFocus), FocusVisuals.ControlMargin);
                }
            });
        }

        ViewHandler.ViewMapper.AppendToMapping(AutomationProperties.ExcludedWithChildrenProperty.PropertyName, (handler, view) =>
        {
            if (view is BindableObject element && handler.PlatformView is FrameworkElement platformView)
            {
                CoveredSubtrees.Apply(platformView, AutomationProperties.GetExcludedWithChildren(element) == true);
            }
        });
        ViewHandler.ViewMapper.AppendToMapping(FocusMoves.HoldsHiddenFocusProperty.PropertyName, (handler, view) =>
        {
            if (view is BindableObject element && handler.PlatformView is FrameworkElement platformView)
            {
                KeyboardFocus.HoldHiddenFocus(platformView, FocusMoves.GetHoldsHiddenFocus(element));
            }
        });
        ViewHandler.ViewMapper.AppendToMapping(TabStop.IsStopProperty.PropertyName, (handler, view) =>
        {
            if (view is BindableObject element && !TabStop.GetIsStop(element) && handler.PlatformView is FrameworkElement platformView)
            {
                KeyboardFocus.KeepPointerFocus(platformView);
            }
        });
        foreach (var command in new[] { FocusMoves.FocusWithinCommand, ScreenFocus.KeyboardFocusWithinCommand })
        {
            ViewHandler.ViewCommandMapper.AppendToMapping(command, (handler, _, args) =>
            {
                if (args is FocusQuery query && handler.PlatformView is FrameworkElement platformView)
                {
                    query.HasFocusWithin = KeyboardFocus.IsWithin(platformView);
                }
            });
        }

        ScrollViewHandler.CommandMapper.AppendToMapping(ScrollRequests.RevealFocusedCommand, (handler, _, _) => KeyboardFocus.RevealFocusedWithin(handler.PlatformView));
        ViewHandler.ViewCommandMapper.AppendToMapping(FocusMoves.RevealCommand, (handler, _, _) =>
        {
            if (handler.PlatformView is not FrameworkElement platformView)
            {
                return;
            }

            platformView.UpdateLayout();
            if (platformView is { ActualWidth: > 0, ActualHeight: > 0 } element)
            {
                var clearance = ScrollRequests.RevealClearance;
                element.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = false,
                    TargetRect = new PlatformRect(-clearance, -clearance, element.ActualWidth + (2 * clearance), element.ActualHeight + (2 * clearance)),
                });
            }
        });
        foreach (var key in new[] { SheetKeysMappingKey, nameof(JobCard.Description), nameof(JobCard.IsCompact), nameof(JobCard.IsLoading) })
        {
            ContentViewHandler.Mapper.AppendToMapping(key, (handler, view) =>
            {
                if (view is BottomSheet sheet)
                {
                    SheetKeys.Attach(handler.PlatformView, sheet);
                }
                else if (view is JobCard card)
                {
                    ListItems.AttachCard(handler.PlatformView, card);
                }
            });
        }

        ViewHandler.ViewCommandMapper.AppendToMapping(ListFocus.HoldFocusCommand, (handler, _, args) =>
        {
            if (handler.PlatformView is Microsoft.UI.Xaml.Controls.ListViewBase list)
            {
                ListItems.HoldFocus(list, args is true);
            }
        });
        foreach (var key in new[] { ListSemanticsMappingKey, ListSemantics.NameProperty.PropertyName })
        {
            CollectionViewHandler.Mapper.AppendToMapping(key, (handler, view) =>
            {
                if (view is BindableObject list)
                {
                    ListItems.AttachList(handler.PlatformView, list);
                }
            });
        }
    }

    private static void FitCheckBoxBox(object sender, RoutedEventArgs e) => FitCheckBox(sender as PlatformCheckBox);

    private static void FitCheckBoxBoxOnResize(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e) => FitCheckBox(sender as PlatformCheckBox);

    private static void FitCheckBox(PlatformCheckBox? box)
    {
        if (box is null || CheckBoxLook.InsetFor(box.Width, box.Height) is not { } inset)
        {
            return;
        }

        if (VisualTreeHelper.GetChildrenCount(box) == 0)
        {
            box.ApplyTemplate();
        }

        if (VisualTreeHelper.GetChildrenCount(box) == 0
            || VisualTreeHelper.GetChild(box, 0) is not FrameworkElement root)
        {
            return;
        }

        root.Margin = new PlatformThickness(inset);
        if (FindPart(root, CheckBoxBoxPartName) is Rectangle part)
        {
            part.StrokeThickness = CheckBoxLook.BorderWidth;
        }
    }

    private static PlatformThickness ToPlatform(Microsoft.Maui.Thickness thickness) => new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);

    private static FrameworkElement? FindPart(DependencyObject parent, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element && element.Name == name)
            {
                return element;
            }

            if (FindPart(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
