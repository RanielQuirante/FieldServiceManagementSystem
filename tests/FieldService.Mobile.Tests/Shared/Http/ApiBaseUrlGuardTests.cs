using System.Reflection;
using FieldService.Mobile.Shared.Http;

namespace FieldService.Mobile.Tests.Shared.Http;

public sealed class ApiBaseUrlGuardTests
{
    [Theory]
    [InlineData("http://10.0.2.2:5080")]
    [InlineData("http://localhost:5080")]
    [InlineData("http://fieldservice.example")]
    public void ReleaseRejectsAnHttpBaseUrl(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ApiBaseUrl.Create(value, "Release"));

        Assert.Equal("ApiBaseUrl must use https outside Debug builds.", exception.Message);
        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseAcceptsAnHttpsBaseUrl()
    {
        var apiBaseUrl = ApiBaseUrl.Create("https://fieldservice.example", "Release");

        Assert.Equal(new Uri("https://fieldservice.example"), apiBaseUrl.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Staging")]
    [InlineData("release")]
    [InlineData("debug")]
    public void EveryConfigurationOtherThanDebugRequiresHttps(string? configuration)
    {
        Assert.Throws<InvalidOperationException>(() => ApiBaseUrl.Create("http://fieldservice.example", configuration));
    }

    [Theory]
    [InlineData("http://10.0.2.2:5080")]
    [InlineData("http://localhost:5080")]
    [InlineData("https://fieldservice.example")]
    public void DebugAcceptsTheDevelopmentHttpAddressesAndHttps(string value)
    {
        var apiBaseUrl = ApiBaseUrl.Create(value, "Debug");

        Assert.Equal(new Uri(value), apiBaseUrl.Value);
    }

    [Theory]
    [InlineData(null, "Debug")]
    [InlineData("", "Release")]
    [InlineData("   ", "Debug")]
    [InlineData("fieldservice.example", "Release")]
    [InlineData("api/v1", "Debug")]
    public void AMissingOrRelativeBaseUrlIsRejected(string? value, string configuration)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ApiBaseUrl.Create(value, configuration));

        Assert.Equal("ApiBaseUrl is missing or is not an absolute URL.", exception.Message);
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void ARootedPathIsRejectedOnEveryHost(string configuration)
    {
        Assert.Throws<InvalidOperationException>(() => ApiBaseUrl.Create("/api", configuration));
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void ASchemeOtherThanHttpOrHttpsIsRejected(string configuration)
    {
        Assert.Throws<InvalidOperationException>(() => ApiBaseUrl.Create("ftp://fieldservice.example", configuration));
    }

    [Fact]
    public void AReleaseAssemblyStampedWithAnHttpBaseUrlFailsAtStartup()
    {
        var assembly = new StubAssembly("http://10.0.2.2:5080", "Release");

        Assert.Throws<InvalidOperationException>(() => ApiBaseUrl.FromAssembly(assembly));
    }

    [Fact]
    public void AReleaseAssemblyStampedWithAnHttpsBaseUrlStarts()
    {
        var assembly = new StubAssembly("https://fieldservice.example", "Release");

        var apiBaseUrl = ApiBaseUrl.FromAssembly(assembly);

        Assert.Equal(new Uri("https://fieldservice.example"), apiBaseUrl.Value);
    }

    [Fact]
    public void AnAssemblyWithoutTheApiBaseUrlMetadataFailsAtStartup()
    {
        var assembly = new StubAssembly(null, "Debug");

        Assert.Throws<InvalidOperationException>(() => ApiBaseUrl.FromAssembly(assembly));
    }

    private sealed class StubAssembly(string? apiBaseUrl, string configuration) : Assembly
    {
        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            if (attributeType == typeof(AssemblyMetadataAttribute))
            {
                var unrelated = new AssemblyMetadataAttribute("RepositoryUrl", "https://example.invalid");
                return apiBaseUrl is null
                    ? new AssemblyMetadataAttribute[] { unrelated }
                    : new AssemblyMetadataAttribute[] { unrelated, new(ApiBaseUrl.MetadataKey, apiBaseUrl) };
            }

            if (attributeType == typeof(AssemblyConfigurationAttribute))
            {
                return new AssemblyConfigurationAttribute[] { new(configuration) };
            }

            return (object[])Array.CreateInstance(attributeType, 0);
        }
    }
}
