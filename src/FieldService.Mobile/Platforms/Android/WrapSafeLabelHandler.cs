using Android.Content;
using Android.Views;
using FieldService.Mobile.Shared.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace FieldService.Mobile;

public sealed class WrapSafeLabelHandler : LabelHandler
{
    protected override MauiTextView CreatePlatformView() => new WrapSafeTextView(Context);

    private sealed class WrapSafeTextView(Context context) : MauiTextView(context)
    {
        private TextWrapWidth _wrap = TextWrapWidth.None;
        private int _spec = -1;

        protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
        {
            var widthMode = MeasureSpec.GetMode(widthMeasureSpec);
            var arrangePass = widthMode == MeasureSpecMode.Exactly && MeasureSpec.GetMode(heightMeasureSpec) == MeasureSpecMode.Exactly;
            if (widthMode != MeasureSpecMode.Unspecified && !arrangePass)
            {
                _spec = MeasureSpec.GetSize(widthMeasureSpec);
                var width = _wrap.WidthFor(_spec);
                if (width != _spec)
                {
                    widthMeasureSpec = MeasureSpec.MakeMeasureSpec(width, widthMode);
                }
            }

            base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
        }

        protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
        {
            base.OnLayout(changed, left, top, right, bottom);
            if (Layout is not { } layout || _spec <= 0)
            {
                return;
            }

            var lines = MaxLines > 0 ? Math.Min(layout.LineCount, MaxLines) : layout.LineCount;
            var needed = layout.GetLineTop(lines) + CompoundPaddingTop + CompoundPaddingBottom;
            var next = _wrap.After(_spec, right - left, needed, bottom - top);
            if (next == _wrap)
            {
                return;
            }

            var remeasure = _wrap.Remeasures(next, _spec);
            _wrap = next;
            if (remeasure)
            {
                Post(RequestLayout);
            }
        }
    }
}
