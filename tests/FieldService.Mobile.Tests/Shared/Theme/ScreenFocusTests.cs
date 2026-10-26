using System.Xml.Linq;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Controls;

namespace FieldService.Mobile.Tests.Shared.Theme;

public sealed class ScreenFocusTests
{
    private static readonly XNamespace _maui = "http://schemas.microsoft.com/dotnet/2021/maui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace _controls = "clr-namespace:FieldService.Mobile.Shared.Controls";

    [Fact]
    public void TheStartTargetIsTheFirstShownEnabledMarkedControlAndAFieldForwardsToItsInput()
    {
        var hidden = new Button { IsVisible = false };
        var disabled = new Button { IsEnabled = false };
        var unmarked = new Button();
        var avatar = new Button();
        var later = new Button();
        foreach (var marked in new[] { hidden, disabled, avatar, later })
        {
            ScreenFocus.SetIsStart(marked, true);
        }

        _ = new VerticalStackLayout { Children = { hidden, disabled, unmarked, avatar, later } };
        Assert.Same(avatar, ScreenFocus.StartTarget([hidden, disabled, unmarked, avatar, later]));

        var input = new Entry();
        var field = new ForwardingField(input);
        ScreenFocus.SetIsStart(field, true);
        _ = new VerticalStackLayout { Children = { field } };
        Assert.Same(input, ScreenFocus.StartTarget([field]));
        Assert.Null(ScreenFocus.StartTarget([unmarked]));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(null, false)]
    public void FocusMovesOnlyWhenThePlatformSaysKeyboardFocusIsOutsideTheScreen(bool? hasFocusWithin, bool moves) =>
        Assert.Equal(moves, ScreenFocus.NeedsMove(hasFocusWithin));

    [Fact]
    public void EachScreensFirstControlIsMarkedAsItsStart()
    {
        var header = Load("Shared/Controls/AppHeader.xaml");
        string[] starts = [.. header.Descendants(_maui + "Button")
            .Where(button => button.Attribute(_controls + "ScreenFocus.IsStart")?.Value == "True")
            .Select(button => button.Attribute(_xaml + "Name")?.Value ?? string.Empty)];
        Assert.Equal(["ListAvatarButton", "BackButton", "TopBarAvatarButton"], starts);
        Assert.Equal("ListAvatarButton", AppHeader.StartControlName(AppHeaderVariant.List));
        Assert.Equal("TopBarAvatarButton", AppHeader.StartControlName(AppHeaderVariant.DesktopTopBar));
        Assert.Equal("BackButton", AppHeader.StartControlName(AppHeaderVariant.Pushed));
        Assert.Equal(string.Empty, AppHeader.StartControlName(AppHeaderVariant.SignIn));

        var signIn = Load("Features/SignIn/SignInPage.xaml");
        var email = Assert.Single(signIn.Descendants(_controls + "FormField"), field => field.Attribute(_controls + "ScreenFocus.IsStart")?.Value == "True");
        Assert.Equal("Email", email.Attribute("Label")?.Value);
    }

    [Theory]
    [InlineData("Features/Jobs/MyJobsPage.xaml")]
    [InlineData("Features/Jobs/JobDetailsPage.xaml")]
    [InlineData("Features/Inspection/InspectionPage.xaml")]
    [InlineData("Features/Inspection/ReviewAndSubmitPage.xaml")]
    [InlineData("Features/SignIn/SignInPage.xaml")]
    public void EveryScreenChecksWhereKeyboardFocusStartsWhenItOpens(string page) =>
        Assert.Equal("True", Load(page).Root?.Attribute(_controls + "ScreenFocus.Watches")?.Value);

    [Fact]
    public void OnWindowsFocusOnTheWindowsInputSiteOrInACollapsedLayoutCountsAsOutsideTheScreen()
    {
        var setup = Source("Platforms/Windows/WindowsPlatformSetup.cs");
        Assert.Contains("ScreenFocus.KeyboardFocusWithinCommand", setup, StringComparison.Ordinal);
        Assert.Contains("KeyboardFocus.IsWithin(platformView)", setup, StringComparison.Ordinal);

        var focus = Source("Platforms/Windows/KeyboardFocus.cs");
        Assert.Contains("FocusManager.GetFocusedElement(root) is not DependencyObject focused", focus, StringComparison.Ordinal);
        Assert.Contains("Visibility: Microsoft.UI.Xaml.Visibility.Collapsed", focus, StringComparison.Ordinal);
        Assert.DoesNotContain("ScreenFocus.KeyboardFocusWithinCommand", Source("Platforms/Android/AndroidPlatformSetup.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFieldForwardsScreenFocusToItsEntryOrEditor()
    {
        var field = Source("Shared/Controls/FormField.xaml.cs");

        Assert.Contains("IFocusForwarder", field, StringComparison.Ordinal);
        Assert.Contains("public VisualElement? FocusTarget => IsMultiline ? Area : Input;", field, StringComparison.Ordinal);
    }

    private static XDocument Load(string relative) =>
        XDocument.Load(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.MobileApp, .. relative.Split('/')]));

    private sealed class ForwardingField(VisualElement input) : ContentView, IFocusForwarder
    {
        public VisualElement? FocusTarget => input;
    }
}
