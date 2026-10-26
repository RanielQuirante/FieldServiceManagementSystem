using Android.Content;
using Android.OS;
using Android.Views.Accessibility;
using FieldService.Mobile.Shared.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace FieldService.Mobile;

public sealed class OptionRowHandler : ContentViewHandler
{
    public static readonly IPropertyMapper<OptionRow, OptionRowHandler> OptionRowMapper =
        new PropertyMapper<OptionRow, OptionRowHandler>(Mapper)
        {
            [nameof(OptionRow.IsSelected)] = (handler, _) => handler.PlatformView.SendAccessibilityEvent(EventTypes.ViewSelected),
            [nameof(OptionRow.IsAvailable)] = (handler, row) =>
            {
                handler.PlatformView.Enabled = row.IsAvailable;
                handler.PlatformView.Focusable = row.IsOperable;
            },
        };

    public OptionRowHandler()
        : base(OptionRowMapper)
    {
    }

    protected override ContentViewGroup CreatePlatformView()
    {
        var view = new OptionRowView(Context, this);
        KeyboardRing.TurnOffDefaultHighlight(view);
        return view;
    }

    private sealed class OptionRowView(Context context, OptionRowHandler handler) : ContentViewGroup(context)
    {
        private OptionRow? Row => handler.VirtualView as OptionRow;

        protected override void DispatchDraw(Android.Graphics.Canvas canvas)
        {
            base.DispatchDraw(canvas);
            if (Row is { } row && KeyboardRing.Shows(this))
            {
                KeyboardRing.Draw(canvas, this, row.FocusCornerRadius, row.FocusInset);
            }
        }

        protected override void OnFocusChanged(bool gainFocus, Android.Views.FocusSearchDirection direction, Android.Graphics.Rect? previouslyFocusedRect)
        {
            base.OnFocusChanged(gainFocus, direction, previouslyFocusedRect);
            KeyboardRing.Invalidate(this);
        }

        public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
        {
            base.OnInitializeAccessibilityNodeInfo(info);
            if (info is null || Row is not { } row)
            {
                return;
            }

            info.ClassName = OptionRow.AndroidClassName;
            info.Checkable = true;
            if (OperatingSystem.IsAndroidVersionAtLeast(36))
            {
                info.CheckedState = row.IsSelected ? CheckedState.True : CheckedState.False;
            }
            else
            {
                info.Checked = row.IsSelected;
            }
            info.Enabled = row.IsAvailable;
            info.Clickable = row.IsAvailable;
            if (row.IsAvailable)
            {
                info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionClick);
            }
            else
            {
                info.RemoveAction(AccessibilityNodeInfo.AccessibilityAction.ActionClick);
            }
        }

        public override bool PerformAccessibilityAction(Android.Views.Accessibility.Action action, Bundle? arguments)
        {
            if (action == Android.Views.Accessibility.Action.Click && Row is { } row)
            {
                var activated = row.Activate();
                if (activated)
                {
                    SendAccessibilityEvent(EventTypes.ViewClicked);
                }

                return activated;
            }

            return base.PerformAccessibilityAction(action, arguments);
        }
    }
}
