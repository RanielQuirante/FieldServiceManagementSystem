namespace FieldService.Mobile.Shared.Controls;

public interface IClosableSheet
{
    bool IsOpen { get; }

    bool RequestClose();
}
