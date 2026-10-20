namespace FieldService.Api.Domain;

public sealed class ChecklistTemplate
{
    private readonly List<ChecklistItem> _items = [];

    private ChecklistTemplate()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Trade Trade { get; private set; }

    public IReadOnlyList<ChecklistItem> Items => _items;

    public static ChecklistTemplate Create(Guid id, string name, Trade trade)
    {
        return new ChecklistTemplate
        {
            Id = id,
            Name = name,
            Trade = trade,
        };
    }
}
