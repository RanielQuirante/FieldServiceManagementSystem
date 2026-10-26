using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class AndroidKeyboardRingTests
{
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Theory]
    [InlineData(16, 20)]
    [InlineData(22, 26)]
    [InlineData(18, 22)]
    public void TheRingsOuterRadiusIsTheElementsRadiusPlusTheGapAndTheStroke(double radius, double outer)
    {
        Assert.Equal(outer, FocusRing.OuterRadius(radius));
        Assert.Equal(radius + 3, FocusRing.CentreLineRadius(radius));
    }

    [Fact]
    public void TheRingsCentreLineRunsThreeOutsideTheElementSoTheTwoWideStrokeCoversTwoToFour()
    {
        Assert.Equal(3, FocusRing.CentreLineOffset);
        Assert.Equal(new Rect(-3, -3, 106, 56), FocusRing.CentreLine(100, 50, Thickness.Zero));
        Assert.Equal(new Rect(-3, -3, 106, 36), FocusRing.CentreLine(100, 50, new Thickness(0, 0, 0, 20)));
        Assert.Equal(0, FocusRing.CentreLine(100, 50, Thickness.Zero).Left + FocusRing.CentreLineOffset);
    }

    [Theory]
    [InlineData("Platforms/Android/OptionRowHandler.cs")]
    [InlineData("Platforms/Android/CheckboxRowHandler.cs")]
    public void RowsTurnOffAndroidsGreyPlateAndDrawTheRingOnlyUnderKeyboardFocus(string handler)
    {
        var source = Source(handler);

        Assert.Contains("KeyboardRing.TurnOffDefaultHighlight(view)", source, StringComparison.Ordinal);
        Assert.Contains("protected override void DispatchDraw(", source, StringComparison.Ordinal);
        Assert.Contains("KeyboardRing.Shows(this)", source, StringComparison.Ordinal);
        Assert.Contains("KeyboardRing.Draw(canvas, this, row.FocusCornerRadius, row.FocusInset)", source, StringComparison.Ordinal);
        Assert.Contains("KeyboardRing.Invalidate(this)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRingUsesTheSharedNumbersTheFocusColourAndIsHiddenInTouchMode()
    {
        var ring = Source("Platforms/Android/KeyboardRing.cs");

        Assert.Contains("view.DefaultFocusHighlightEnabled = false", ring, StringComparison.Ordinal);
        Assert.Contains("focusHolder.IsFocused && !focusHolder.IsInTouchMode", ring, StringComparison.Ordinal);
        Assert.Contains("FocusRing.CentreLine(", ring, StringComparison.Ordinal);
        Assert.Contains("FocusRing.CentreLineRadius(radius)", ring, StringComparison.Ordinal);
        Assert.Contains("FocusRing.Thickness * density", ring, StringComparison.Ordinal);
        Assert.Contains("FocusRing.LightColorKey", ring, StringComparison.Ordinal);
    }

    [Fact]
    public void AJobCardsListItemKeepsKeyboardFocusWithoutThePlateAndTheCardDrawsTheRingAroundItself()
    {
        var card = Source("Platforms/Android/TappableCardHandler.cs");

        Assert.Contains("KeyboardRing.TurnOffDefaultHighlight(target)", card, StringComparison.Ordinal);
        Assert.Contains("AddOnGlobalFocusChangeListener(this)", card, StringComparison.Ordinal);
        Assert.Contains("RemoveOnGlobalFocusChangeListener(this)", card, StringComparison.Ordinal);
        Assert.Contains("card.IsCompact ? JobCard.CompactRadius : JobCard.Radius", card, StringComparison.Ordinal);
        Assert.Contains("KeyboardRing.Shows(target)", card, StringComparison.Ordinal);
        Assert.Equal(22, JobCard.Radius);
        Assert.Equal(18, JobCard.CompactRadius);
    }

    [Fact]
    public void TheCheckboxRowsInnerBoxIsNotASecondKeyboardStop()
    {
        var box = Load("Shared/Controls/CheckboxRow.xaml").Descendants(_maui + "CheckBox").Single();

        Assert.Equal("False", box.Attribute(_controls + "TabStop.IsStop")?.Value);
    }

    [Fact]
    public void ListsScrollContainersAndEveryAncestorOfAPageAreNeverKeyboardStops()
    {
        var setup = Source("Platforms/Android/AndroidPlatformSetup.cs");

        Assert.Contains("CollectionViewHandler.Mapper.AppendToMapping(ContainerMappingKey, (handler, _) => NotAKeyboardStop(handler.PlatformView))", setup, StringComparison.Ordinal);
        Assert.Contains("ScrollViewHandler.Mapper.AppendToMapping(ContainerMappingKey, (handler, _) => NotAKeyboardStop(handler.PlatformView))", setup, StringComparison.Ordinal);
        Assert.Contains("if (view is Page)", setup, StringComparison.Ordinal);
        Assert.Contains("KeepAncestorsOutOfTabOrder(handler.PlatformView)", setup, StringComparison.Ordinal);
        Assert.Contains("for (var parent = page.Parent; parent is Android.Views.View container; parent = container.Parent)", setup, StringComparison.Ordinal);
        Assert.Contains("view.Focusable = false;", setup, StringComparison.Ordinal);
        Assert.Contains("view.FocusableInTouchMode = false;", setup, StringComparison.Ordinal);
        Assert.Contains("page.ViewAttachedToWindow += (_, _) => Apply();", setup, StringComparison.Ordinal);
    }

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
