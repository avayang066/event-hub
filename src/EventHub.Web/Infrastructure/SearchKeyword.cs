namespace EventHub.Web.Infrastructure;

/// <summary>搜尋關鍵字：整理使用者輸入，轉成 LIKE 的部分符合樣式。</summary>
public static class SearchKeyword
{
    public const int MaxLength = 50;

    /// <summary>LIKE 的跳脫字元（搭配 EF.Functions.Like 的第三個參數）</summary>
    public const string LikeEscapeChar = "\\";

    /// <summary>去頭尾空白、限制長度；空字串視為沒有搜尋。</summary>
    public static string? Normalize(string? keyword)
    {
        keyword = keyword?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            return null;
        }

        return keyword.Length > MaxLength ? keyword[..MaxLength] : keyword;
    }

    /// <summary>「包含」的 LIKE 樣式：%關鍵字%</summary>
    public static string ToContainsPattern(string keyword) => $"%{EscapeLikePattern(keyword)}%";

    /// <summary>
    /// 跳脫 LIKE 的特殊字元，讓使用者輸入的 % 和 _ 被當成一般文字，
    /// 否則搜尋「100%」會變成萬用字元比對。
    /// </summary>
    private static string EscapeLikePattern(string value) =>
        value
            .Replace(LikeEscapeChar, LikeEscapeChar + LikeEscapeChar)
            .Replace("%", LikeEscapeChar + "%")
            .Replace("_", LikeEscapeChar + "_");
}
