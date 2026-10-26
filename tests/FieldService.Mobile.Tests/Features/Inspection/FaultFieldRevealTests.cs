using System.Xml.Linq;
using FieldService.Mobile.Features.Inspection;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Features.Inspection;

public sealed class FaultFieldRevealTests
{
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Fact]
    public async Task FailRevealsTheFaultFieldAndAsksForItsFocusInTheSameCallSoTheRevealMustWaitForItsHeight()
    {
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => InspectionDtoBuilder.Json(InspectionDtoBuilder.Draft1042())));
        harness.Store.RememberJob(new InspectionJob(JobDtoBuilder.Job1042, 1042, "Rooftop HVAC unit not cooling", "Rooftop HVAC service"));
        var viewModel = harness.CreateInspection();
        viewModel.ApplyQueryAttributes(JobsHarness.JobQuery(JobDtoBuilder.Job1042));
        await viewModel.AppearingCommand.ExecuteAsync(null);
        var item = viewModel.Items[6];
        var changes = new List<string>();
        item.PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? string.Empty);

        item.SelectCommand.Execute(InspectionResult.Fail);

        var shown = changes.IndexOf(nameof(InspectionItemViewModel.ShowsFaultField));
        var focus = changes.IndexOf(nameof(InspectionItemViewModel.FaultFocusRequest));
        Assert.True(shown >= 0 && shown < focus);
        Assert.True(item.ShowsFaultField);
        Assert.Equal(1, item.FaultFocusRequest);
    }

    [Fact]
    public void AFieldsFocusRequestRevealsTheWholeFieldBeforeItsInputTakesFocus()
    {
        var field = Source("Shared/Controls/FormField.xaml.cs");
        var moves = Source("Shared/Controls/FocusMoves.cs");
        var request = field[field.IndexOf("private void OnFocusRequested", StringComparison.Ordinal)..field.IndexOf("private void UpdateCount", StringComparison.Ordinal)];
        var reveal = moves[moves.IndexOf("public static bool RevealThenFocus", StringComparison.Ordinal)..moves.IndexOf("public static void MoveSoon", StringComparison.Ordinal)];
        var page = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Features", "Inspection", "InspectionPage.xaml"));
        var fault = page.Descendants(_controls + "FormField").Single(element => element.Attribute("FocusRequest")?.Value == "{Binding FaultFocusRequest}");

        Assert.Contains("FocusMoves.RevealThenFocus(this, IsMultiline ? Area : Input);", request, StringComparison.Ordinal);
        Assert.DoesNotContain("Area.Focus()", request, StringComparison.Ordinal);
        Assert.DoesNotContain("Input.Focus()", request, StringComparison.Ordinal);
        Assert.True(reveal.IndexOf("container.Handler?.Invoke(RevealCommand);", StringComparison.Ordinal) < reveal.IndexOf("return input.Focus();", StringComparison.Ordinal));
        Assert.Equal("{Binding ShowsFaultField}", fault.Attribute("IsVisible")?.Value);
        Assert.Equal("True", fault.Attribute("IsMultiline")?.Value);
    }

    [Fact]
    public void OnWindowsTheRevealLaysOutFirstSoAFieldShownByTheSameActionHasItsHeightAndThenAsksForItsRectanglePlus16()
    {
        var setup = Source("Platforms/Windows/WindowsPlatformSetup.cs");
        var reveal = setup[setup.IndexOf("ViewHandler.ViewCommandMapper.AppendToMapping(FocusMoves.RevealCommand", StringComparison.Ordinal)..];
        reveal = reveal[..reveal.IndexOf("});", StringComparison.Ordinal)];
        var layout = reveal.IndexOf("platformView.UpdateLayout();", StringComparison.Ordinal);
        var size = reveal.IndexOf("platformView is { ActualWidth: > 0, ActualHeight: > 0 } element", StringComparison.Ordinal);
        var bring = reveal.IndexOf("element.StartBringIntoView(", StringComparison.Ordinal);

        Assert.True(layout > 0 && layout < size && size < bring);
        Assert.Contains("AnimationDesired = false", reveal, StringComparison.Ordinal);
        Assert.Contains("TargetRect = new PlatformRect(-clearance, -clearance, element.ActualWidth + (2 * clearance), element.ActualHeight + (2 * clearance)),", reveal, StringComparison.Ordinal);
        Assert.Contains("var clearance = ScrollRequests.RevealClearance;", reveal, StringComparison.Ordinal);
        Assert.Equal(16, ScrollRequests.RevealClearance);
        Assert.DoesNotContain("RevealCommand", Source("Platforms/Android/AndroidPlatformSetup.cs"), StringComparison.Ordinal);
    }

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
