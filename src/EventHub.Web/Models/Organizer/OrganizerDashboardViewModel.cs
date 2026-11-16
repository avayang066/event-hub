using EventHub.Web.Data.Entities;
using EventHub.Web.Models.Reports;

namespace EventHub.Web.Models.Organizer;

/// <summary>主辦方「我的活動」：活動列表 + 收款摘要。</summary>
public class OrganizerDashboardViewModel
{
    public IReadOnlyList<Event> Events { get; init; } = [];

    /// <summary>收款摘要；還沒有任何付費報名時為 null</summary>
    public OrganizerPayoutViewModel? Payout { get; init; }
}
