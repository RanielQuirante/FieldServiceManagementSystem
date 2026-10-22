namespace FieldService.Mobile.Shared.Session;

public readonly record struct SessionCredential(long Epoch, Guid? UserId, string? AccessToken)
{
    public override string ToString() => $"{nameof(SessionCredential)} {Epoch}";
}
