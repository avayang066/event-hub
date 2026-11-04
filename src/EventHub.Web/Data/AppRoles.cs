namespace EventHub.Web.Data;

public static class AppRoles
{
    /// <summary>平台管理員（看全站訂單、結算撥款）</summary>
    public const string Admin = "Admin";

    /// <summary>活動主辦方（商家）</summary>
    public const string Organizer = "Organizer";

    /// <summary>報名者</summary>
    public const string Attendee = "Attendee";

    public static readonly string[] All = [Admin, Organizer, Attendee];
}
