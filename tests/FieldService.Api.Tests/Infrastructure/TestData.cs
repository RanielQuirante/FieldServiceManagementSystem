using System.Security.Cryptography;
using FieldService.Api.Domain;
using FieldService.Api.Features.Auth;
using FieldService.Api.Persistence;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Tests.Infrastructure;

public sealed class TestData(DatabaseFixture fixture, DateTimeOffset now)
{
    public const string HoldNote = "Waiting for a replacement part.";

    public const string CancellationReason = "The customer cancelled the visit.";

    public static readonly Guid HvacTemplateId = ChecklistTemplateSeed.TemplateId(2);

    public static readonly string Password = $"test-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}";

    private static readonly Lazy<string> _passwordHash = new(() => PasswordVerifier.CreateHasher().HashPassword(
        User.Create("hash-owner@fieldservice.example", "Hash owner", string.Empty, UserRole.Administrator, DateTimeOffset.UnixEpoch),
        Password));

    public DateTimeOffset Now { get; } = now;

    public static string PasswordHash => _passwordHash.Value;

    public static User NewUser(UserRole role, DateTimeOffset now, string? passwordHash = null, string? displayName = null)
    {
        var defaultName = role == UserRole.Administrator ? "Test Administrator" : "Test Technician";
        return User.Create(
            $"user-{Guid.NewGuid():N}@fieldservice.example",
            displayName ?? $"{defaultName} {Marker()}",
            passwordHash ?? PasswordHash,
            role,
            now);
    }

    public static Technician NewTechnician(
        Trade trade,
        DateTimeOffset now,
        bool isOnShift = true,
        bool isActive = true,
        string? displayName = null)
    {
        var user = NewUser(UserRole.Technician, now, displayName: displayName);
        var technician = Technician.Create(
            user,
            trade,
            $"(555) 010-{RandomNumberGenerator.GetInt32(10000):D4}",
            isOnShift);
        if (!isActive)
        {
            user.Deactivate(now);
        }

        return technician;
    }

    public static Job NewJob(Guid createdByUserId, Guid checklistTemplateId, DateTimeOffset now) =>
        Job.Create(NewDetails(checklistTemplateId, now), createdByUserId, now);

    public static JobDetails NewDetails(
        Guid checklistTemplateId,
        DateTimeOffset now,
        string? title = null,
        string? customerName = null,
        string? siteAddress = null,
        JobPriority priority = JobPriority.Normal)
    {
        var marker = Marker();
        return new JobDetails(
            title ?? $"Test job {marker}",
            "Created by the API tests.",
            priority,
            customerName ?? $"Test Customer {marker}",
            siteAddress ?? $"Test Site {marker}",
            SiteAccessNotes: null,
            ContactName: null,
            ContactPhone: null,
            Equipment: null,
            now.AddDays(1),
            now.AddDays(1).AddHours(2),
            checklistTemplateId);
    }

    public static InspectionChecklist ChecklistOf(Guid templateId) =>
        InspectionChecklist.From(templateId, ChecklistTemplateSeed.Items, ChecklistTemplateSeed.ReadingFields);

    public static List<ChecklistAnswer> PassingAnswers(InspectionChecklist checklist, int? count = null) =>
    [
        .. checklist.Items
            .Take(count ?? checklist.Total)
            .Select(item => new ChecklistAnswer(
                item.Id,
                InspectionResult.Pass,
                Note: null,
                [.. item.ReadingFieldIds.Select(fieldId => new ChecklistReading(fieldId, 12.5m))])),
    ];

    public static JobArrangement ArrangeJob(JobStatus status, DateTimeOffset start, bool answerChecklist = false)
    {
        var administrator = NewUser(UserRole.Administrator, start);
        var assignee = NewTechnician(Trade.Hvac, start);
        var otherTechnician = NewTechnician(Trade.Hvac, start);
        var checklist = ChecklistOf(HvacTemplateId);
        var job = NewJob(administrator.Id, checklist.TemplateId, start);
        var arrangement = new JobArrangement(administrator, assignee, otherTechnician, job, null, checklist, start.AddHours(1));
        if (status == JobStatus.Unassigned)
        {
            return arrangement;
        }

        Require(job.AssignTo(assignee, arrangement.AdministratorActor, start.AddMinutes(5)));
        if (status == JobStatus.Cancelled)
        {
            Require(job.Cancel(CancellationReason, arrangement.AdministratorActor, start.AddMinutes(20)));
        }

        if (status is JobStatus.Assigned or JobStatus.Cancelled)
        {
            return arrangement;
        }

        Require(job.ChangeStatus(JobStatus.InProgress, note: null, arrangement.AssigneeActor, start.AddMinutes(10)));
        Inspection? inspection = null;
        if (answerChecklist || status == JobStatus.Completed)
        {
            inspection = Inspection.Start(job, start.AddMinutes(15));
            Require(inspection.SaveAnswers(
                job,
                checklist,
                PassingAnswers(checklist),
                arrangement.AssigneeActor,
                start.AddMinutes(15)));
        }

        if (status == JobStatus.OnHold)
        {
            Require(job.ChangeStatus(JobStatus.OnHold, HoldNote, arrangement.AssigneeActor, start.AddMinutes(20)));
        }

        if (status == JobStatus.Completed)
        {
            Require(job.Submit(inspection, checklist, technicianNotes: null, arrangement.AssigneeActor, start.AddMinutes(20)));
        }

        return arrangement with { Inspection = inspection };
    }

    public static void Require(DomainError? error)
    {
        if (error is not null)
        {
            throw new InvalidOperationException($"The arrangement failed with {error.Code}: {error.Message}");
        }
    }

    public async Task<User> CreateAdministratorAsync(
        CancellationToken cancellationToken,
        string? passwordHash = null,
        bool isActive = true)
    {
        var user = NewUser(UserRole.Administrator, Now, passwordHash);
        if (!isActive)
        {
            user.Deactivate(Now);
        }

        await using var context = fixture.CreateAppDbContext();
        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<Technician> CreateTechnicianAsync(
        Trade trade,
        CancellationToken cancellationToken,
        bool isOnShift = true,
        bool isActive = true,
        string? displayName = null)
    {
        var technician = NewTechnician(trade, Now, isOnShift, isActive, displayName);
        await using var context = fixture.CreateAppDbContext();
        context.Users.Add(technician.User);
        context.Technicians.Add(technician);
        await context.SaveChangesAsync(cancellationToken);
        return technician;
    }

    public async Task<Job> CreateJobAsync(
        Guid createdByUserId,
        Guid checklistTemplateId,
        CancellationToken cancellationToken,
        long? number = null)
    {
        var job = NewJob(createdByUserId, checklistTemplateId, Now);
        await using var context = fixture.CreateAppDbContext();
        context.Jobs.Add(job);
        if (number is { } explicitNumber)
        {
            context.Entry(job).Property(entry => entry.Number).CurrentValue = explicitNumber;
        }

        await context.SaveChangesAsync(cancellationToken);
        return job;
    }

    public async Task<Job> CreateJobAsync(
        Guid createdByUserId,
        JobDetails details,
        CancellationToken cancellationToken,
        long? number = null)
    {
        var job = Job.Create(details, createdByUserId, Now);
        await using var context = fixture.CreateAppDbContext();
        context.Jobs.Add(job);
        if (number is { } explicitNumber)
        {
            context.Entry(job).Property(entry => entry.Number).CurrentValue = explicitNumber;
        }

        await context.SaveChangesAsync(cancellationToken);
        return job;
    }

    public async Task<Job> MoveToAsync(
        Job job,
        Technician technician,
        JobStatus status,
        CancellationToken cancellationToken)
    {
        if (status == JobStatus.Unassigned)
        {
            return job;
        }

        if (status == JobStatus.Completed)
        {
            await CreateInspectionAsync(job, technician, submitted: true, cancellationToken);
            return await LoadJobAsync(job.Id, cancellationToken);
        }

        var current = await AssignJobAsync(job, technician, cancellationToken);
        if (status == JobStatus.Cancelled)
        {
            return await CancelJobAsync(current, cancellationToken);
        }

        if (status == JobStatus.Assigned)
        {
            return current;
        }

        current = await ChangeStatusAsync(current, JobStatus.InProgress, cancellationToken);
        return status == JobStatus.OnHold
            ? await ChangeStatusAsync(current, JobStatus.OnHold, cancellationToken, HoldNote)
            : current;
    }

    public Task<Job> AssignJobAsync(Job job, Technician technician, CancellationToken cancellationToken) =>
        ChangeJobAsync(
            job.Id,
            (current, _) => current.AssignTo(technician, JobActor.Administrator(current.CreatedByUserId), Now),
            cancellationToken);

    public Task<Job> ChangeStatusAsync(
        Job job,
        JobStatus target,
        CancellationToken cancellationToken,
        string? note = null) =>
        ChangeJobAsync(
            job.Id,
            (current, _) => current.ChangeStatus(target, note, AssigneeOf(current), Now),
            cancellationToken);

    public Task<Job> CancelJobAsync(Job job, CancellationToken cancellationToken, string reason = CancellationReason) =>
        ChangeJobAsync(
            job.Id,
            (current, _) => current.Cancel(reason, JobActor.Administrator(current.CreatedByUserId), Now),
            cancellationToken);

    public async Task<Inspection> SaveInspectionAsync(
        Job job,
        IReadOnlyList<ChecklistAnswer> answers,
        CancellationToken cancellationToken)
    {
        Inspection? saved = null;
        await ChangeJobAsync(
            job.Id,
            (current, context) =>
            {
                var existing = LoadInspection(context, current.Id);
                saved = existing ?? Inspection.Start(current, Now);
                var error = saved.SaveAnswers(current, ChecklistOf(current.ChecklistTemplateId), answers, AssigneeOf(current), Now);
                if (error is null && existing is null)
                {
                    context.Inspections.Add(saved);
                }

                return error;
            },
            cancellationToken);
        return saved!;
    }

    public Task<Job> SubmitJobAsync(Job job, CancellationToken cancellationToken, string? technicianNotes = null) =>
        ChangeJobAsync(
            job.Id,
            (current, context) => current.Submit(
                LoadInspection(context, current.Id),
                ChecklistOf(current.ChecklistTemplateId),
                technicianNotes,
                AssigneeOf(current),
                Now),
            cancellationToken);

    public async Task<Inspection> CreateInspectionAsync(
        Job job,
        Technician technician,
        bool submitted,
        CancellationToken cancellationToken)
    {
        var current = await LoadJobAsync(job.Id, cancellationToken);
        if (current.Status == JobStatus.Unassigned)
        {
            current = await AssignJobAsync(current, technician, cancellationToken);
        }

        if (current.Status == JobStatus.Assigned)
        {
            current = await ChangeStatusAsync(current, JobStatus.InProgress, cancellationToken);
        }

        var inspection = await SaveInspectionAsync(
            current,
            PassingAnswers(ChecklistOf(current.ChecklistTemplateId)),
            cancellationToken);
        if (!submitted)
        {
            return inspection;
        }

        await SubmitJobAsync(current, cancellationToken);
        await using var context = fixture.CreateAppDbContext();
        return await context.Inspections.AsNoTracking().SingleAsync(entry => entry.Id == inspection.Id, cancellationToken);
    }

    public async Task<SignedInSession> SignInAsync(
        ApiFactory factory,
        SessionClient sessionClient,
        CancellationToken cancellationToken)
    {
        var user = sessionClient == SessionClient.Web
            ? await CreateAdministratorAsync(cancellationToken)
            : (await CreateTechnicianAsync(Trade.Hvac, cancellationToken)).User;
        var client = factory.CreateApiClient();
        using var response = await client.SignInAsync(user.Email, Password, sessionClient);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            client.Dispose();
            throw new InvalidOperationException($"The test sign-in returned {(int)response.StatusCode}.");
        }

        var session = await ApiClient.ReadSessionAsync(response);
        var refreshToken = sessionClient == SessionClient.Web
            ? ApiClient.RefreshCookieOf(response)!.Value.ToString()
            : session.RefreshToken!;
        return new SignedInSession(client, user, sessionClient, refreshToken, session);
    }

    private static JobActor AssigneeOf(Job job) =>
        JobActor.Technician(job.AssignedTechnicianId
            ?? throw new InvalidOperationException("The job has no assigned technician."));

    private static Inspection? LoadInspection(FieldServiceDbContext context, Guid jobId) =>
        context.Inspections
            .Include(inspection => inspection.Answers)
            .ThenInclude(answer => answer.Readings)
            .SingleOrDefault(inspection => inspection.JobId == jobId);

    private static string Marker()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(2));
    }

    private async Task<Job> LoadJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAppDbContext();
        return await context.Jobs.AsNoTracking().SingleAsync(entry => entry.Id == jobId, cancellationToken);
    }

    private async Task<Job> ChangeJobAsync(
        Guid jobId,
        Func<Job, FieldServiceDbContext, DomainError?> change,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAppDbContext();
        var job = await context.Jobs.SingleAsync(entry => entry.Id == jobId, cancellationToken);
        Require(change(job, context));
        await context.SaveChangesAsync(cancellationToken);
        return job;
    }

    public sealed record JobArrangement(
        User Administrator,
        Technician Assignee,
        Technician OtherTechnician,
        Job Job,
        Inspection? Inspection,
        InspectionChecklist Checklist,
        DateTimeOffset Later)
    {
        public JobActor AdministratorActor => JobActor.Administrator(Administrator.Id);

        public JobActor AssigneeActor => JobActor.Technician(Assignee.UserId);

        public JobActor OtherTechnicianActor => JobActor.Technician(OtherTechnician.UserId);
    }

    public sealed class SignedInSession(
        ApiClient client,
        User user,
        SessionClient sessionClient,
        string refreshToken,
        SessionResponse session) : IDisposable
    {
        public ApiClient Client { get; } = client;

        public User User { get; } = user;

        public SessionClient SessionClient { get; } = sessionClient;

        public string RefreshToken { get; } = refreshToken;

        public SessionResponse Session { get; } = session;

        public Task<HttpResponseMessage> RefreshAsync(string? token = null)
        {
            return SessionClient == SessionClient.Web
                ? Client.RefreshWithCookieAsync(token ?? RefreshToken)
                : Client.RefreshWithBodyAsync(token ?? RefreshToken);
        }

        public Task<HttpResponseMessage> SignOutAsync(string? token = null)
        {
            return SessionClient == SessionClient.Web
                ? Client.SignOutWithCookieAsync(token ?? RefreshToken)
                : Client.SignOutWithBodyAsync(token ?? RefreshToken);
        }

        public async Task<string> NextTokenAsync(HttpResponseMessage response)
        {
            return SessionClient == SessionClient.Web
                ? ApiClient.RefreshCookieOf(response)!.Value.ToString()
                : (await ApiClient.ReadSessionAsync(response)).RefreshToken!;
        }

        public void Dispose()
        {
            Client.Dispose();
        }
    }
}
