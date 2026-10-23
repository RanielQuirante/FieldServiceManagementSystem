using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class IconBesideTextXamlTests
{
    private const string JobCardFile = "Shared/Controls/JobCard.xaml";
    private const string AppHeaderFile = "Shared/Controls/AppHeader.xaml";
    private const string JobDetailsFile = "Features/Jobs/JobDetailsView.xaml";
    private const string FormFieldFile = "Shared/Controls/FormField.xaml";
    private const string CheckboxRowFile = "Shared/Controls/CheckboxRow.xaml";
    private const string InspectionFile = "Features/Inspection/InspectionPage.xaml";
    private const string LoadingText = "{x:Static inspection:InspectionViewModel.LoadingText}";

    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";
    private static readonly string[] _withdrawnMargins = ["FsMarginIconBeside15", "FsMarginIconBeside14"];

    [Theory]
    [InlineData(JobCardFile, "{Binding CustomerName}", null)]
    [InlineData(JobCardFile, "{Binding SiteAddress}", null)]
    [InlineData(JobCardFile, "{Binding WellTime}", "{StaticResource FsTextWell}")]
    [InlineData(AppHeaderFile, "{Binding SyncText}", "{StaticResource FsTextSyncOnDark}")]
    [InlineData(AppHeaderFile, "{Binding StatusLineText}", "{StaticResource FsTextMetaOnDark}")]
    [InlineData(AppHeaderFile, "{Binding ScheduleText}", "{StaticResource FsTextScheduleOnDark}")]
    [InlineData(JobDetailsFile, "{Binding ScheduleLine}", "{StaticResource FsTextScheduleOnDark}")]
    [InlineData(FormFieldFile, "{Binding ErrorText}", "{StaticResource FsTextFieldError}")]
    [InlineData(CheckboxRowFile, "{Binding ErrorText}", "{StaticResource FsTextFieldError}")]
    [InlineData(InspectionFile, LoadingText, "{StaticResource FsTextSyncOnDark}")]
    [InlineData(InspectionFile, "{Binding Note}", "{StaticResource FsTextMeta}")]
    public void EachIconBesideTextThatCanWrapSharesASixteenWideStartCellWithALineBoxOfItsTextsStyle(string file, string text, string? style)
    {
        var (label, cell, box, icon) = Row(Load(file), text);

        Assert.Equal("Grid", cell.Name.LocalName);
        Assert.Equal("16", cell.Attribute("WidthRequest")?.Value);
        Assert.Equal("Start", cell.Attribute("VerticalOptions")?.Value);
        Assert.Equal(["Label", "Icon"], cell.Elements().Select(element => element.Name.LocalName));
        Assert.Equal(style, label.Attribute("Style")?.Value);
        Assert.Equal(style, box.Attribute("Style")?.Value);
        Assert.Equal("16", icon.Attribute("Size")?.Value);
        Assert.Equal("Center", icon.Attribute("VerticalOptions")?.Value);
        Assert.Null(icon.Attribute("Margin"));
        Assert.Null(icon.Attribute("Grid.Column"));
    }

    [Theory]
    [InlineData(JobCardFile, "{Binding CustomerName}")]
    [InlineData(JobCardFile, "{Binding SiteAddress}")]
    [InlineData(JobCardFile, "{Binding WellTime}")]
    [InlineData(AppHeaderFile, "{Binding SyncText}")]
    [InlineData(AppHeaderFile, "{Binding StatusLineText}")]
    [InlineData(AppHeaderFile, "{Binding ScheduleText}")]
    [InlineData(JobDetailsFile, "{Binding ScheduleLine}")]
    [InlineData(FormFieldFile, "{Binding ErrorText}")]
    [InlineData(CheckboxRowFile, "{Binding ErrorText}")]
    [InlineData(InspectionFile, LoadingText)]
    [InlineData(InspectionFile, "{Binding Note}")]
    public void EveryLineBoxIsOneInvisibleLineThatTakesNoInputAndStaysOutOfTheAccessibilityTree(string file, string text)
    {
        var box = Row(Load(file), text).Box;

        Assert.Equal("A", box.Attribute("Text")?.Value);
        Assert.Equal("1", box.Attribute("MaxLines")?.Value);
        Assert.Equal("NoWrap", box.Attribute("LineBreakMode")?.Value);
        Assert.Equal("0", box.Attribute("Opacity")?.Value);
        Assert.Equal("True", box.Attribute("InputTransparent")?.Value);
        Assert.Equal("False", box.Attribute("AutomationProperties.IsInAccessibleTree")?.Value);
        Assert.DoesNotContain(box.Attributes(), attribute => attribute.Value.StartsWith("{Binding", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, "FsTextCardMeta")]
    [InlineData(true, "FsTextCompactMeta")]
    public void AJobCardsLineBoxesSwitchWithTheirMetaLabelsWhenTheCardIsCompact(bool compact, string style)
    {
        var card = Load(JobCardFile);
        var applyLook = Method(Source("Shared/Controls/JobCard.xaml.cs"), "private void ApplyLook()");

        Assert.Equal(style, JobCard.MetaStyleKey(compact));
        Assert.Equal("CustomerLineBox", Row(card, "{Binding CustomerName}").Box.Attribute(_xaml + "Name")?.Value);
        Assert.Equal("SiteLineBox", Row(card, "{Binding SiteAddress}").Box.Attribute(_xaml + "Name")?.Value);
        Assert.Equal("CustomerLabel", Row(card, "{Binding CustomerName}").Label.Attribute(_xaml + "Name")?.Value);
        Assert.Equal("SiteLabel", Row(card, "{Binding SiteAddress}").Label.Attribute(_xaml + "Name")?.Value);
        Assert.Contains("var meta = MetaStyleKey(IsCompact);", applyLook, StringComparison.Ordinal);
        Assert.Contains("CustomerLabel.SetDynamicResource(StyleProperty, meta);", applyLook, StringComparison.Ordinal);
        Assert.Contains("CustomerLineBox.SetDynamicResource(StyleProperty, meta);", applyLook, StringComparison.Ordinal);
        Assert.Contains("SiteLabel.SetDynamicResource(StyleProperty, meta);", applyLook, StringComparison.Ordinal);
        Assert.Contains("SiteLineBox.SetDynamicResource(StyleProperty, meta);", applyLook, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySixteenIconBesideTextInTheCardTheHeaderTheDetailsTheFieldsAndTheChecklistSitsInALineBoxCell()
    {
        var icons = new[] { JobCardFile, AppHeaderFile, JobDetailsFile, FormFieldFile, CheckboxRowFile, InspectionFile }
            .SelectMany(file => Load(file).Descendants(_controls + "Icon"))
            .Where(icon => icon.Attribute("Size")?.Value == "16")
            .ToList();

        Assert.Equal(11, icons.Count);
        Assert.All(icons, icon =>
        {
            var cell = icon.Parent!;
            Assert.Equal("16", cell.Attribute("WidthRequest")?.Value);
            Assert.Equal("0", cell.Elements(_maui + "Label").Single().Attribute("Opacity")?.Value);
        });
    }

    [Fact]
    public void TheWithdrawnFixedIconMarginsAreGone()
    {
        var metrics = Load("Shared/Theme/Metrics.xaml").Root!.Elements().Select(element => element.Attribute(_xaml + "Key")?.Value).ToList();
        var sources = RepositoryPaths.MobileSources("*.xaml").Concat(RepositoryPaths.MobileSources("*.cs"));

        Assert.All(_withdrawnMargins, key => Assert.DoesNotContain(key, metrics));
        Assert.DoesNotContain(sources, path => _withdrawnMargins.Any(key => File.ReadAllText(path).Contains(key, StringComparison.Ordinal)));
    }

    [Fact]
    public void JobDetailsShowsItsStatusLineThroughTheHeroOfTheHeaderAndThePaneMetaHasNoIcon()
    {
        var view = Load(JobDetailsFile);
        var header = view.Descendants(_controls + "AppHeader").Single();
        var paneMeta = view.Descendants(_maui + "Label").Single(label => label.Attribute("Text")?.Value == "{Binding PaneMeta}");

        Assert.Equal("Pushed", header.Attribute("Variant")?.Value);
        Assert.Equal("{Binding StatusLine}", header.Attribute("StatusLineText")?.Value);
        Assert.Equal("VerticalStackLayout", paneMeta.Parent!.Name.LocalName);
        Assert.DoesNotContain(paneMeta.Parent.Elements(), element => element.Name.LocalName == "Icon");
    }

    private static (XElement Label, XElement Cell, XElement Box, XElement Icon) Row(XDocument document, string text)
    {
        var label = document.Descendants(_maui + "Label").Single(element => element.Attribute("Text")?.Value == text);
        var row = label.Parent!;
        Assert.Equal("Grid", row.Name.LocalName);
        Assert.Equal("Auto,*", row.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("1", label.Attribute("Grid.Column")?.Value);
        var cell = row.Elements().Single(element => element != label);
        Assert.Contains(cell.Attribute("Grid.Column")?.Value, new[] { null, "0" });
        return (label, cell, cell.Elements(_maui + "Label").Single(), cell.Elements(_controls + "Icon").Single());
    }

    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        return source[start..end];
    }

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')])).ReplaceLineEndings("\n");
}
