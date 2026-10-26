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

    private sealed class TappableCardView(Context context, TappableCardHandler handler) : ContentViewGroup(context), Android.Views.ViewTreeObserver.IOnGlobalFocusChangeListener
    {
        private Android.Views.ViewTreeObserver? _observer;
        private bool _ringShown;

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

        public void OnGlobalFocusChanged(Android.Views.View? oldFocus, Android.Views.View? newFocus)
        {
            if (ShowsRing() != _ringShown)
            {
                KeyboardRing.Invalidate(this);
            }
        }

        protected override void OnAttachedToWindow()
        {
            base.OnAttachedToWindow();
            if (ClickTarget() is { } target)
            {
                target.ImportantForAccessibility = Android.Views.ImportantForAccessibility.No;
                KeyboardRing.TurnOffDefaultHighlight(target);
            }

            _observer = ViewTreeObserver;
            _observer?.AddOnGlobalFocusChangeListener(this);
        }

        protected override void OnDetachedFromWindow()
        {
            if (_observer is { IsAlive: true } observer)
            {
                observer.RemoveOnGlobalFocusChangeListener(this);
            }

            _observer = null;
            base.OnDetachedFromWindow();
        }

        protected override void DispatchDraw(Android.Graphics.Canvas canvas)
        {
            base.DispatchDraw(canvas);
            _ringShown = ShowsRing();
            if (_ringShown && handler.VirtualView is JobCard card)
            {
                KeyboardRing.Draw(canvas, this, card.IsCompact ? JobCard.CompactRadius : JobCard.Radius, Microsoft.Maui.Thickness.Zero);
            }
        }

        private bool ShowsRing() =>
            handler.VirtualView is JobCard { IsLoading: false } && ClickTarget() is { } target && KeyboardRing.Shows(target);

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
