using FieldService.Api.Domain;

namespace FieldService.Api.Persistence.Seeding;

public sealed class DemoDataSet
{
    private DemoDataSet(
        IReadOnlyList<User> users,
        IReadOnlyList<Technician> technicians,
        IReadOnlyList<(long Number, Job Job)> jobs,
        IReadOnlyList<Inspection> inspections)
    {
        Users = users;
        Technicians = technicians;
        Jobs = jobs;
        Inspections = inspections;
    }

    public IReadOnlyList<User> Users { get; }

    public IReadOnlyList<Technician> Technicians { get; }

    public IReadOnlyList<(long Number, Job Job)> Jobs { get; }

    public IReadOnlyList<Inspection> Inspections { get; }

    public DemoDataCounts Counts => new(
        Users.Count,
        Technicians.Count,
        Jobs.Count,
        Jobs.Sum(entry => entry.Job.History.Count),
        Inspections.Count,
        Inspections.Sum(inspection => inspection.Answers.Count),
        Inspections.Sum(inspection => inspection.Answers.Sum(answer => answer.Readings.Count)));

    public static DemoDataSet Replay(DemoTimeAnchor anchor, Func<User, string> hashPassword)
    {
        DemoTimeline.EnsureStrictlyIncreasing(DemoData.Jobs, anchor);

        var usersCreatedAt = anchor.Resolve(DemoData.UsersCreatedAt);
        var users = new Dictionary<DemoPerson, User>();
        var technicians = new Dictionary<DemoPerson, Technician>();
        foreach (var demoUser in DemoData.Users)
        {
            var user = User.Create(demoUser.Email, demoUser.DisplayName, string.Empty, demoUser.Role, usersCreatedAt);
            user.ReplacePasswordHash(hashPassword(user), usersCreatedAt);
            users.Add(demoUser.Person, user);
            if (demoUser.Trade is { } trade)
            {
                technicians.Add(
                    demoUser.Person,
                    Technician.Create(user, trade, demoUser.PhoneNumber ?? string.Empty, demoUser.IsOnShift));
            }
        }

        var administrator = JobActor.Administrator(users[DemoPerson.DanaWhitfield].Id);
        var jobs = new List<(long Number, Job Job)>();
        var inspections = new List<Inspection>();
        foreach (var demoJob in DemoData.Jobs)
        {
            var (job, inspection) = ReplayJob(demoJob, anchor, administrator, technicians);
            jobs.Add((demoJob.Number, job));
            if (inspection is not null)
            {
                inspections.Add(inspection);
            }
        }

        return new DemoDataSet([.. users.Values], [.. technicians.Values], jobs, inspections);
    }

    public void AddTo(FieldServiceDbContext db)
    {
        db.Users.AddRange(Users);
        db.Technicians.AddRange(Technicians);
        foreach (var (number, job) in Jobs)
        {
            db.Jobs.Add(job);
            db.Entry(job).Property(entry => entry.Number).CurrentValue = number;
        }

        db.Inspections.AddRange(Inspections);
    }

    private static (Job Job, Inspection? Inspection) ReplayJob(
        DemoJob demoJob,
        DemoTimeAnchor anchor,
        JobActor administrator,
        Dictionary<DemoPerson, Technician> technicians)
    {
        if (demoJob.Steps is not [DemoStep.Created created, ..])
        {
            throw new InvalidOperationException($"The demo history of {JobNumber.Format(demoJob.Number)} must start with Created.");
        }

        var templateId = ChecklistTemplateSeed.TemplateId(demoJob.TemplateNumber);
        var checklist = InspectionChecklist.From(templateId, ChecklistTemplateSeed.Items, ChecklistTemplateSeed.ReadingFields);
        var job = Job.Create(
            new JobDetails(
                demoJob.Title,
                demoJob.Description,
                demoJob.Priority,
                demoJob.CustomerName,
                demoJob.SiteAddress,
                demoJob.SiteAccessNotes,
                demoJob.ContactName,
                demoJob.ContactPhone,
                demoJob.Equipment,
                anchor.Resolve(demoJob.ScheduledStart),
                anchor.Resolve(demoJob.ScheduledEnd),
                templateId),
            administrator.UserId,
            anchor.Resolve(created.At));

        Inspection? inspection = null;
        foreach (var step in demoJob.Steps.Skip(1))
        {
            var at = anchor.Resolve(step.At);
            var assignee = job.AssignedTechnicianId is { } technicianId
                ? JobActor.Technician(technicianId)
                : default;
            var error = step switch
            {
                DemoStep.Assigned assigned => job.AssignTo(technicians[assigned.Technician], administrator, at),
                DemoStep.Started => job.ChangeStatus(JobStatus.InProgress, note: null, assignee, at),
                DemoStep.PutOnHold hold => job.ChangeStatus(JobStatus.OnHold, hold.Note, assignee, at),
                DemoStep.InspectionSaved saved => (inspection ??= Inspection.Start(job, at))
                    .SaveAnswers(job, checklist, ToAnswers(demoJob.TemplateNumber, saved.Answers), assignee, at),
                DemoStep.Completed completed => job.Submit(inspection, checklist, completed.TechnicianNotes, assignee, at),
                DemoStep.Cancelled cancelled => job.Cancel(cancelled.Reason, administrator, at),
                _ => throw new InvalidOperationException($"The demo history of {JobNumber.Format(demoJob.Number)} repeats Created."),
            };
            if (error is not null)
            {
                throw new InvalidOperationException(
                    $"The demo history of {JobNumber.Format(demoJob.Number)} failed at {step.Name} ({step.At}): {error.Code}, {error.Message}");
            }
        }

        return (job, inspection);
    }

    private static List<ChecklistAnswer> ToAnswers(int templateNumber, IReadOnlyList<DemoAnswer> answers) =>
    [
        .. answers.Select(answer => new ChecklistAnswer(
            ChecklistTemplateSeed.ItemId(templateNumber, answer.Item),
            answer.Result,
            answer.Note,
            [
                .. answer.Readings.Select(reading => new ChecklistReading(
                    ChecklistTemplateSeed.ReadingFieldId(templateNumber, answer.Item, reading.Field),
                    reading.Value)),
            ])),
    ];
}
