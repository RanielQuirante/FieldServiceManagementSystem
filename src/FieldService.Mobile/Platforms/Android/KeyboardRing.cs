using Android.Graphics;
using FieldService.Mobile.Shared.Controls;
using Microsoft.Maui.Platform;
using RingPaint = Android.Graphics.Paint;

namespace FieldService.Mobile;

public static class KeyboardRing
{
    private static readonly RingPaint _paint = CreatePaint();

    public static void TurnOffDefaultHighlight(Android.Views.View view)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            view.DefaultFocusHighlightEnabled = false;
        }
    }

    public static bool Shows(Android.Views.View focusHolder)
    {
        ArgumentNullException.ThrowIfNull(focusHolder);

        return focusHolder.IsFocused && !focusHolder.IsInTouchMode;
    }

    public static void Draw(Canvas canvas, Android.Views.View view, double radius, Microsoft.Maui.Thickness inset)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(view);

        var density = view.Resources?.DisplayMetrics?.Density ?? 1f;
        var width = view.Width / density;
        var height = view.Height / density;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var line = FocusRing.CentreLine(width, height, inset);
        var corner = (float)(FocusRing.CentreLineRadius(radius) * density);
        _paint.StrokeWidth = (float)(FocusRing.Thickness * density);
        _paint.Color = RingColor();
        canvas.DrawRoundRect(
            new Android.Graphics.RectF((float)(line.Left * density), (float)(line.Top * density), (float)(line.Right * density), (float)(line.Bottom * density)),
            corner,
            corner,
            _paint);
    }

    public static void Invalidate(Android.Views.View view)
    {
        ArgumentNullException.ThrowIfNull(view);

        view.Invalidate();
        (view.Parent as Android.Views.View)?.Invalidate();
    }

    private static Android.Graphics.Color RingColor() =>
        Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(FocusRing.LightColorKey, out var value) == true && value is Microsoft.Maui.Graphics.Color color
            ? color.ToPlatform()
            : Android.Graphics.Color.Rgb(0x0F, 0x76, 0x6E);

    private static RingPaint CreatePaint()
    {
        var paint = new RingPaint(PaintFlags.AntiAlias);
        paint.SetStyle(RingPaint.Style.Stroke);
        return paint;
    }
}
