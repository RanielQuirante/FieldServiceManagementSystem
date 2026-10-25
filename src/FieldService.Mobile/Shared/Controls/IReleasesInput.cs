namespace FieldService.Mobile.Shared.Controls;

public interface IReleasesInput
{
    bool HoldsInput { get; }

    void ReleaseInput();

    void EndInput();
}
