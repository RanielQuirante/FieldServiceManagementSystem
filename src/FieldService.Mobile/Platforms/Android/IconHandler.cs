using FieldService.Mobile.Shared.Controls;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Handlers;

namespace FieldService.Mobile;

public sealed class IconHandler() : ViewHandler<Icon, PlatformGraphicsView>(Mapper, CommandMapper)
{
    public static readonly IPropertyMapper<Icon, IconHandler> Mapper = new PropertyMapper<Icon, IconHandler>(ViewMapper)
    {
        [nameof(IGraphicsView.Drawable)] = MapDrawable,
    };

    public static readonly CommandMapper<Icon, IconHandler> CommandMapper = new(ViewCommandMapper)
    {
        [nameof(IGraphicsView.Invalidate)] = (handler, _, _) => handler.PlatformView.Invalidate(),
    };

    protected override PlatformGraphicsView CreatePlatformView() => new(Context, VirtualView.Drawable);

    private static void MapDrawable(IconHandler handler, Icon icon)
    {
        handler.PlatformView.Drawable = icon.Drawable;
        handler.PlatformView.Invalidate();
    }
}
