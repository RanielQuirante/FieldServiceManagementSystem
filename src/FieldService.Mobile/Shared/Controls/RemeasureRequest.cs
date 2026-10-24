namespace FieldService.Mobile.Shared.Controls;

public sealed class RemeasureRequest
{
    public double? AskedWidth { get; private set; }

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
