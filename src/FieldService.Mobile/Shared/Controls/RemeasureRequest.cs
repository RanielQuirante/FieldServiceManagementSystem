namespace FieldService.Mobile.Shared.Controls;

public sealed class RemeasureRequest
{
    public const double SameWidthTolerance = 0.01;

    public double? AskedWidth { get; private set; }

    public static bool IsMeasuredWidth(double measuredWidth, double arrangedWidth) =>
        double.IsFinite(measuredWidth) && Math.Abs(measuredWidth - arrangedWidth) < SameWidthTolerance;

    public bool AsksAt(double arrangedWidth, bool measuredMode, bool modeAtArrangedWidth)
    {
        if (measuredMode == modeAtArrangedWidth || AskedWidth == arrangedWidth)
        {
            return false;
        }

        AskedWidth = arrangedWidth;
        return true;
    }
}
