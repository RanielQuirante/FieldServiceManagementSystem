namespace FieldService.Api.Persistence.Configurations;

public static class EnumCheck
{
    public static string In<TEnum>(string column)
        where TEnum : struct, Enum
    {
        var values = Enum.GetNames<TEnum>().Select(name => $"'{name}'");
        return $"{column} IN ({string.Join(", ", values)})";
    }
}
