using Microsoft.AspNetCore.Authentication;

namespace FieldService.Api.Shared.Security;

internal sealed class AuthenticationConfigurationProvider(IConfiguration configuration) : IAuthenticationConfigurationProvider
{
    public const string SectionName = "Authentication";

    public IConfiguration AuthenticationConfiguration => configuration.GetSection(SectionName);
}
