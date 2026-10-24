using System.Globalization;
using System.Reflection;
using FieldService.Api.Shared.Http;
using Microsoft.AspNetCore.Mvc;

namespace FieldService.Api.Shared.Errors;

public sealed class QueryParameterRule
{
    public const string RepeatedValueMessage = "Send this parameter at most once.";

    public const string GuidMessage = "The value must be a UUID.";

    public const string IntegerMessage = "The value must be a whole number.";

    public const string EmptyValueMessage = "Enter a value, or leave the parameter out.";

    public const string InstantMessage =
        "The value must be a date and time with Z or a numeric offset, such as 2026-10-19T04:00:00.000Z.";

    private const DateTimeStyles BinderInstantStyles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces;

    private readonly Func<string, string?> _check;

    private QueryParameterRule(string name, Type valueType, Func<string, string?> check)
    {
        Name = name;
        ValueType = valueType;
        _check = check;
    }

    public string Name { get; }

    public Type ValueType { get; }

    public string? Check(string? value) => value is null ? null : _check(value);

    public static IReadOnlyList<QueryParameterRule> ListFor(MethodInfo handler)
    {
        var rules = new List<QueryParameterRule>();
        foreach (var parameter in handler.GetParameters())
        {
            if (parameter.GetCustomAttribute<AsParametersAttribute>() is not null)
            {
                foreach (var property in parameter.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetCustomAttribute<FromQueryAttribute>() is { } fromProperty)
                    {
                        rules.Add(For(fromProperty.Name ?? property.Name, property.PropertyType));
                    }
                }
            }
            else if (parameter.GetCustomAttribute<FromQueryAttribute>() is { } fromParameter)
            {
                rules.Add(For(fromParameter.Name ?? parameter.Name!, parameter.ParameterType));
            }
        }

        return rules;
    }

    public static QueryParameterRule For(string name, Type type)
    {
        var valueType = Nullable.GetUnderlyingType(type) ?? type;
        if (valueType == typeof(string))
        {
            return new QueryParameterRule(name, valueType, static _ => null);
        }

        if (valueType.IsEnum)
        {
            var names = Enum.GetNames(valueType);
            var message = $"The value must be one of {string.Join(", ", names[..^1])} or {names[^1]}.";
            return new QueryParameterRule(
                name,
                valueType,
                value => value.Length == 0 ? EmptyValueMessage
                    : Array.IndexOf(names, value) >= 0 ? null
                    : message);
        }

        if (valueType == typeof(Guid))
        {
            return new QueryParameterRule(
                name,
                valueType,
                static value => value.Length == 0 ? EmptyValueMessage
                    : Guid.TryParse(value, out _) ? null
                    : GuidMessage);
        }

        if (valueType == typeof(int))
        {
            return new QueryParameterRule(
                name,
                valueType,
                static value => value.Length == 0 ? EmptyValueMessage
                    : int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) ? null
                    : IntegerMessage);
        }

        if (valueType == typeof(DateTimeOffset))
        {
            return new QueryParameterRule(
                name,
                valueType,
                static value => value.Length == 0 ? EmptyValueMessage
                    : IsInstant(value) ? null
                    : InstantMessage);
        }

        throw new InvalidOperationException(
            $"The query parameter {name} has the type {valueType.Name}, which has no validation rule.");
    }

    private static bool IsInstant(string value) =>
        UtcInstantJsonConverter.TryParse(value, out _)
        && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, BinderInstantStyles, out _);
}
