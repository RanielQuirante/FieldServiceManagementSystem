using FieldService.Mobile.Shared.Controls;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class RemeasureRequestTests
{
    [Fact]
    public void ALayoutAsksForANewMeasureOnlyWhenTheArrangedWidthWouldChooseTheOtherMode()
    {
        var request = new RemeasureRequest();

        Assert.False(request.AsksAt(250.0741, measuredMode: false, modeAtArrangedWidth: false));
        Assert.False(request.AsksAt(250.0741, measuredMode: true, modeAtArrangedWidth: true));
        Assert.Null(request.AskedWidth);
        Assert.True(request.AsksAt(250.0741, measuredMode: false, modeAtArrangedWidth: true));
        Assert.Equal(250.0741, request.AskedWidth);
    }

    [Fact]
    public void ALayoutAsksAtMostOncePerArrangedWidthSoAParentCannotStartALoop()
    {
        var request = new RemeasureRequest();

        var asks = Enumerable.Range(0, 10).Count(_ => request.AsksAt(250.0741, measuredMode: false, modeAtArrangedWidth: true));

        Assert.Equal(1, asks);
        Assert.False(request.AsksAt(250.0741, measuredMode: true, modeAtArrangedWidth: false));
    }

    [Fact]
    public void ANewArrangedWidthMayAskAgainAndTheLayoutKeepsOnlyTheLastWidthItAskedFor()
    {
        var request = new RemeasureRequest();

        Assert.True(request.AsksAt(250.0741, measuredMode: false, modeAtArrangedWidth: true));
        Assert.True(request.AsksAt(249.8704, measuredMode: false, modeAtArrangedWidth: true));
        Assert.Equal(249.8704, request.AskedWidth);
        Assert.True(request.AsksAt(250.0741, measuredMode: false, modeAtArrangedWidth: true));
        Assert.False(request.AsksAt(250.0741, measuredMode: false, modeAtArrangedWidth: true));
    }
}
