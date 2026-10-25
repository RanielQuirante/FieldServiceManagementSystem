using System.Runtime.CompilerServices;

namespace FieldService.Mobile.Shared.Controls;

public static class AccessibilityExclusion
{
    private static readonly ConditionalWeakTable<BindableObject, object> _seenIncluded = new();
    private static readonly object _seen = new();

    public static bool RestoresImportance(BindableObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.IsSet(AutomationProperties.ExcludedWithChildrenProperty)
            && AutomationProperties.GetExcludedWithChildren(element) != true
            && AutomationProperties.GetIsInAccessibleTree(element) is null;
    }

    public static bool CoversContent(BindableObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (AutomationProperties.GetExcludedWithChildren(element) != true)
        {
            _seenIncluded.AddOrUpdate(element, _seen);
            return false;
        }

        return _seenIncluded.TryGetValue(element, out _);
    }
}
