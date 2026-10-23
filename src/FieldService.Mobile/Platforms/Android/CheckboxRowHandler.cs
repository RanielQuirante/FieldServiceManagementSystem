using Android.Content;
using Android.OS;
using Android.Views.Accessibility;
using FieldService.Mobile.Shared.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace FieldService.Mobile;

public sealed class CheckboxRowHandler : ContentViewHandler
{
    public static readonly IPropertyMapper<CheckboxRow, CheckboxRowHandler> CheckboxRowMapper =
        new PropertyMapper<CheckboxRow, CheckboxRowHandler>(Mapper)
        {
            [nameof(CheckboxRow.IsChecked)] = (handler, _) => handler.PlatformView.SendAccessibilityEvent(EventTypes.ViewClicked),
        };

    public CheckboxRowHandler()
        : base(CheckboxRowMapper)
    {
    }

    protected override ContentViewGroup CreatePlatformView() => new CheckboxRowView(Context, this);

    private sealed class CheckboxRowView(Context context, CheckboxRowHandler handler) : ContentViewGroup(context)
    {
        private CheckboxRow? Row => handler.VirtualView as CheckboxRow;

        public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
        {
            base.OnInitializeAccessibilityNodeInfo(info);
            if (info is null || Row is not { } row)
            {
                return;
            }

            info.ClassName = CheckboxRow.AndroidClassName;
            info.Checkable = true;
            if (OperatingSystem.IsAndroidVersionAtLeast(36))
            {
                info.CheckedState = row.IsChecked ? CheckedState.True : CheckedState.False;
            }
            else
            {
                info.Checked = row.IsChecked;
            }

            info.Clickable = true;
            info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionClick);
        }

        public override bool PerformAccessibilityAction(Android.Views.Accessibility.Action action, Bundle? arguments)
        {
            if (action == Android.Views.Accessibility.Action.Click && Row is { } row)
            {
                return row.Toggle();
            }

            return base.PerformAccessibilityAction(action, arguments);
        }
    }
}
