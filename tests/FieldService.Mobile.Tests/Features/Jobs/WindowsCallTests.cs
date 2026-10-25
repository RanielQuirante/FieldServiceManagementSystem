using System.Xml.Linq;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using NSubstitute;

namespace FieldService.Mobile.Tests.Features.Jobs;

public sealed class WindowsCallTests
{
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Theory]
    [InlineData(null, "(555) 014-4417")]
    [InlineData("Nina Alvarez", "(555) 014-6620")]
    public async Task OnWindowsCallIsNeverOfferedEvenWhenTheDialerSaysItIsSupported(string? contactName, string phone)
    {
        var job = JobDtoBuilder.Details1047() with { ContactName = contactName, ContactPhone = phone };
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(job)), dialerSupported: true, platform: DevicePlatform.WinUI);
        var viewModel = harness.CreateDetails();
        await viewModel.ShowJobAsync(job.Id);

        Assert.True(harness.Dialer.IsSupported);
        Assert.False(viewModel.CanCall);
        Assert.False(viewModel.ShowsCallAction);
        Assert.False(viewModel.ShowsRowCall);
        Assert.False(viewModel.CallCommand.CanExecute(null));
        viewModel.CallCommand.Execute(null);
        harness.Dialer.DidNotReceive().Open(Arg.Any<string>());
        Assert.True(viewModel.HasContactPhone);
        Assert.True(viewModel.ShowsNavigationRow);
        Assert.True(viewModel.NavigateCommand.CanExecute(null));
        Assert.Equal(contactName ?? phone, viewModel.ContactValue);
    }

    [Fact]
    public async Task OnAndroidCallStaysWhereTheDialerIsSupported()
    {
        var job = JobDtoBuilder.Details1047();
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(job)), dialerSupported: true, platform: DevicePlatform.Android);
        var viewModel = harness.CreateDetails();
        await viewModel.ShowJobAsync(job.Id);

        Assert.True(viewModel.CanCall);
        Assert.True(viewModel.ShowsCallAction);
        Assert.True(viewModel.CallCommand.CanExecute(null));
        viewModel.CallCommand.Execute(null);
        harness.Dialer.Received(1).Open("(555) 014-4417");
    }

    [Fact]
    public async Task AJobWithoutAContactPhoneHasNoSelectableNumber()
    {
        var job = JobDtoBuilder.Details1047() with { ContactName = "Nina Alvarez", ContactPhone = null };
        var harness = new JobsHarness(StubHttpMessageHandler.Sequence(() => JobDtoBuilder.Json(job)), platform: DevicePlatform.WinUI);
        var viewModel = harness.CreateDetails();
        await viewModel.ShowJobAsync(job.Id);

        Assert.False(viewModel.HasContactPhone);
        Assert.False(viewModel.CanCall);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, true)]
    public void OnlyTheLineThatHoldsTheNumberIsSelectable(bool selectsText, bool hasSecondaryValue, bool value, bool secondaryValue)
    {
        Assert.Equal((value, secondaryValue), InfoRow.SelectionFor(selectsText, hasSecondaryValue));
    }

    [Fact]
    public void TheContactRowMakesItsNumberSelectableAndTheRowStillReadsIt()
    {
        var view = Load("Features/Jobs/JobDetailsView.xaml");
        var contact = view.Descendants(_controls + "InfoRow").Single(row => row.Attribute("Label")?.Value == "Contact");
        var row = Load("Shared/Controls/InfoRow.xaml");
        var labels = row.Descendants(_maui + "Label").ToList();

        Assert.Equal("{Binding HasContactPhone}", contact.Attribute("SelectsText")?.Value);
        Assert.Equal("{Binding ShowsRowCall}", contact.Attribute("ShowsAction")?.Value);
        Assert.Equal("{Binding SelectsValue}", labels.Single(label => label.Attribute("Text")?.Value == "{Binding Value}").Attribute(_controls + "TextSelection.IsEnabled")?.Value);
        Assert.Equal("{Binding SelectsSecondaryValue}", labels.Single(label => label.Attribute("Text")?.Value == "{Binding SecondaryValue}").Attribute(_controls + "TextSelection.IsEnabled")?.Value);
        Assert.Equal("Contact, Nina Alvarez, (555) 014-6620", InfoRow.Describe("Contact", "Nina Alvarez", "(555) 014-6620"));
        Assert.Equal(2, row.Descendants().Attributes(_controls + "TextSelection.IsEnabled").Count());
    }

    [Fact]
    public void OnWindowsALabelMarkedSelectableTurnsOnTextSelection()
    {
        var setup = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Platforms", "Windows", "WindowsPlatformSetup.cs"));
        var label = new Label();

        Assert.Contains("LabelHandler.Mapper.AppendToMapping(TextSelection.MappingKey", setup, StringComparison.Ordinal);
        Assert.Contains("handler.PlatformView.IsTextSelectionEnabled = TextSelection.GetIsEnabled(label);", setup, StringComparison.Ordinal);
        Assert.False(TextSelection.GetIsEnabled(label));
        TextSelection.SetIsEnabled(label, true);
        Assert.True(TextSelection.GetIsEnabled(label));
        Assert.Throws<ArgumentNullException>(() => TextSelection.GetIsEnabled(null!));
        Assert.Throws<ArgumentNullException>(() => TextSelection.SetIsEnabled(null!, true));
    }

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));
}
