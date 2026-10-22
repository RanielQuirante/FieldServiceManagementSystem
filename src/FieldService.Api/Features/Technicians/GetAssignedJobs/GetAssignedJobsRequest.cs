using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace FieldService.Api.Features.Technicians.GetAssignedJobs;

public sealed record GetAssignedJobsRequest
{
    public const string ViewParameter = "view";

    public const string ActiveView = "active";

    public const string CompletedView = "completed";

    public const string DefaultView = ActiveView;

    public const string ViewMessage = "Choose the active or completed view.";

    [FromQuery(Name = ViewParameter)]
    [RegularExpression("^(" + ActiveView + "|" + CompletedView + ")$", ErrorMessage = ViewMessage)]
    [DefaultValue(DefaultView)]
    public string? View { get; init; }
}
