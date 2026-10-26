using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Controls;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class PrimaryButtonIconTests
{
    private const string Pause = "M8 6v12";
    private const string Play = "M9 7l8 5";

    [Fact]
    public void AnIconKeyFollowsItsResourceAndANullKeyClearsTheIconForGood()
    {
        var host = new IconHost { Resources = new ResourceDictionary { ["FsIconPauseCircle"] = Pause, ["FsIconPlayCircle"] = Play } };

        PrimaryButton.ApplyIconKey(host, IconHost.DataProperty, "FsIconPauseCircle");
        Assert.Equal(Pause, host.Data);

        PrimaryButton.ApplyIconKey(host, IconHost.DataProperty, null);
        Assert.Null(host.Data);

        host.Resources["FsIconPauseCircle"] = Play;
        Assert.Null(host.Data);

        PrimaryButton.ApplyIconKey(host, IconHost.DataProperty, "FsIconPlayCircle");
        Assert.Equal(Play, host.Data);

        PrimaryButton.ApplyIconKey(host, IconHost.DataProperty, string.Empty);
        Assert.Null(host.Data);
    }

    [Fact]
    public void PrimaryButtonsIconKeyGoesThroughTheClearingRuleAndHasIconFollowsTheIcon()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Shared", "Controls", "PrimaryButton.xaml.cs"));

        Assert.Contains("propertyChanged: (bindable, _, newValue) => ApplyIconKey((PrimaryButton)bindable, IconDataProperty, newValue as string));", source, StringComparison.Ordinal);
        Assert.Contains("element.RemoveDynamicResource(property);", source, StringComparison.Ordinal);
        Assert.Contains("element.SetValue(property, null);", source, StringComparison.Ordinal);
        Assert.Contains("element.ClearValue(property);", source, StringComparison.Ordinal);
        Assert.Contains("HasIcon = !string.IsNullOrWhiteSpace(IconData);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("newValue is string { Length: > 0 } key", source, StringComparison.Ordinal);
    }

    private sealed class IconHost : View
    {
        public static readonly BindableProperty DataProperty = BindableProperty.Create(nameof(Data), typeof(string), typeof(IconHost));

        public string? Data
        {
            get => (string?)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }
    }
}
