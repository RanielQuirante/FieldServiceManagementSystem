using Android.Content;
using Android.OS;
using Android.Views.Accessibility;
using AndroidX.RecyclerView.Widget;
using FieldService.Mobile.Shared.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace FieldService.Mobile;

public sealed class TappableCardHandler : ContentViewHandler
{
    protected override ContentViewGroup CreatePlatformView() => new TappableCardView(Context, this);

    private sealed class TappableCardView(Context context, TappableCardHandler handler) : ContentViewGroup(context)
    {
        private bool ActsAsButton => handler.VirtualView is ITappableCard { ActsAsButton: true };

        public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
        {
            base.OnInitializeAccessibilityNodeInfo(info);
            if (info is null || !ActsAsButton || ClickTarget() is null)
            {
                return;
            }

            info.ClassName = TappableCard.AndroidClassName;
            info.Clickable = true;
            info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionClick);
        }

        public override bool PerformAccessibilityAction(Android.Views.Accessibility.Action action, Bundle? arguments)
        {
            if (action == Android.Views.Accessibility.Action.Click && ActsAsButton && ClickTarget() is { } target)
            {
                return target.PerformClick();
            }

            return base.PerformAccessibilityAction(action, arguments);
        }

        protected override void OnAttachedToWindow()
        {
            base.OnAttachedToWindow();
            if (ClickTarget() is { } target)
            {
                target.ImportantForAccessibility = Android.Views.ImportantForAccessibility.No;
            }
        }

        private Android.Views.View? ClickTarget()
        {
            Android.Views.View current = this;
            while (current.Parent is Android.Views.View parent)
            {
                if (parent is RecyclerView)
                {
                    return current.Clickable ? current : null;
                }

                current = parent;
            }

            return null;
        }
    }
}
