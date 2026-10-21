using System.Globalization;
using FieldService.Api.Domain;

namespace FieldService.Api.Persistence.Seeding;

public static class DemoTimeline
{
    public static IReadOnlyList<DemoTimelineStep> Resolve(DemoJob job, DemoTimeAnchor anchor) =>
        [.. job.Steps.Select(step => new DemoTimelineStep(step.Name, anchor.Resolve(step.At)))];

    public static void EnsureStrictlyIncreasing(IEnumerable<DemoJob> jobs, DemoTimeAnchor anchor)
    {
        foreach (var job in jobs)
        {
            EnsureStrictlyIncreasing(job.Number, Resolve(job, anchor));
        }
    }

    public static void EnsureStrictlyIncreasing(long jobNumber, IReadOnlyList<DemoTimelineStep> steps)
    {
        for (var index = 1; index < steps.Count; index++)
        {
            var previous = steps[index - 1];
            var current = steps[index];
            if (current.At <= previous.At)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Demo data was not seeded: the timeline of {JobNumber.Format(jobNumber)} must increase strictly, but step {index + 1} ({current.Name}, {current.At:yyyy-MM-dd'T'HH:mm:ss'Z'}) is not after step {index} ({previous.Name}, {previous.At:yyyy-MM-dd'T'HH:mm:ss'Z'}). Seed at another time of day, or reset the database and start the API again later."));
            }
        }
    }
}
