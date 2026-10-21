using System.Globalization;

namespace FieldService.Api.Domain;

public static class JobNumber
{
    public const long First = 1001;

    public const string Prefix = "JOB-";

    public static string Format(long number) => Prefix + number.ToString(CultureInfo.InvariantCulture);
}
