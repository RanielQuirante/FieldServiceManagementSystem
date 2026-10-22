namespace FieldService.Mobile.Shared.Controls;

public readonly record struct TextWrapWidth(int Spec, int Arranged)
{
    public const int MaxRoundingLoss = 2;
    public const int HeightTolerance = 1;

    public static readonly TextWrapWidth None = new(-1, -1);

    public int WidthFor(int spec) => spec == Spec && Arranged > 0 && Arranged < spec ? Arranged : spec;

    public TextWrapWidth After(int spec, int arranged, int neededHeight, int arrangedHeight)
    {
        var loss = spec - arranged;
        if (spec <= 0 || loss <= 0 || loss > MaxRoundingLoss)
        {
            return None;
        }

        if (neededHeight > arrangedHeight + HeightTolerance)
        {
            return new(spec, spec == Spec && Arranged > 0 ? Math.Min(Arranged, arranged) : arranged);
        }

        return spec == Spec ? this : None;
    }

    public bool Remeasures(TextWrapWidth next, int spec) => next != None && next.WidthFor(spec) != WidthFor(spec);
}
