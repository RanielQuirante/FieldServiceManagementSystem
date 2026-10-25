using FieldService.Mobile.Shared.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FieldService.Mobile.WinUI;

public static class SheetKeys
{
    public static void Attach(UIElement panel, BottomSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(sheet);

        panel.TabFocusNavigation = KeyboardNavigationMode.Cycle;
        panel.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        if (panel.KeyboardAccelerators.Any(accelerator => accelerator.Key == VirtualKey.Escape))
        {
            return;
        }

        var escape = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, args) =>
        {
            if (sheet.IsOpen)
            {
                sheet.RequestClose();
                args.Handled = true;
            }
        };
        panel.KeyboardAccelerators.Add(escape);
    }
}
