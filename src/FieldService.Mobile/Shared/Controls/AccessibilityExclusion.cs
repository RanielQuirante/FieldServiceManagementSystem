namespace FieldService.Mobile.Shared.Controls;

public static class AccessibilityExclusion
{
    public static bool RestoresImportance(BindableObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.IsSet(AutomationProperties.ExcludedWithChildrenProperty)
            && AutomationProperties.GetExcludedWithChildren(element) != true
            && AutomationProperties.GetIsInAccessibleTree(element) is null;
    }
}
