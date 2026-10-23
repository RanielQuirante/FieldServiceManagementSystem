namespace FieldService.Mobile.Shared.Navigation;

public static class Routes
{
    public const string SignInRoute = "sign-in";
    public const string JobsRoute = "jobs";
    public const string SignIn = "//" + SignInRoute;
    public const string Jobs = "//" + JobsRoute;
    public const string JobDetailsRoute = JobsRoute + "/details";
    public const string JobDetails = "//" + JobDetailsRoute;
    public const string InspectionPage = "inspection";
    public const string ReviewPage = "review";
    public const string InspectionRoute = JobsRoute + "/" + InspectionPage;
    public const string ReviewRoute = JobsRoute + "/" + ReviewPage;
    public const string Back = "..";
    public const string BackTwo = "../..";
    public const string BackToInspection = "../" + InspectionPage;
    public const string JobIdParameter = "jobId";
    public const string NoticeParameter = "notice";
    public const string SourceParameter = "source";
    public const string JobNumberParameter = "jobNumber";
    public const string JobUnassignedNotice = "job-unassigned";
    public const string JobSubmittedNotice = "job-submitted";
    public const string SubmissionIncompleteNotice = "submission-incomplete";
    public const string SubmissionInvalidTransitionNotice = "submission-invalid-transition";
    public const string FromInspection = "inspection";
    public const string FromSheet = "sheet";
}
