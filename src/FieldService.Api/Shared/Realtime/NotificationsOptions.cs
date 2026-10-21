namespace FieldService.Api.Shared.Realtime;

public sealed class NotificationsOptions
{
    public const string SectionName = "Notifications";

    public const string BaseUrlName = "Notifications:BaseUrl";

    public const string WebhookSecretName = "Notifications:WebhookSecret";

    public string? BaseUrl { get; set; }

    public string? WebhookSecret { get; set; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(BaseUrl);
}
