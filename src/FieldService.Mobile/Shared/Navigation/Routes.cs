namespace FieldService.Mobile.Shared.Navigation;

public static class Routes
{
    public const string SignInRoute = "sign-in";
    public const string JobsRoute = "jobs";
    public const string SignIn = "//" + SignInRoute;
    public const string Jobs = "//" + JobsRoute;
    public const string JobDetailsRoute = JobsRoute + "/details";
    public const string JobDetails = "//" + JobDetailsRoute;
    public const string Back = "..";
    public const string JobIdParameter = "jobId";
    public const string NoticeParameter = "notice";
    public const string JobUnassignedNotice = "job-unassigned";
}
