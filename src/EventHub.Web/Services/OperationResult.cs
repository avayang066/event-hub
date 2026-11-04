namespace EventHub.Web.Services;

/// <summary>
/// 商業邏輯的執行結果。失敗時帶有錯誤訊息；Field 對應表單欄位名稱（空字串 = 整體錯誤）。
/// </summary>
public class OperationResult
{
    private readonly List<(string Field, string Message)> _errors = [];

    public IReadOnlyList<(string Field, string Message)> Errors => _errors;
    public bool Succeeded => _errors.Count == 0;

    public OperationResult AddError(string field, string message)
    {
        _errors.Add((field, message));
        return this;
    }

    public static OperationResult Success() => new();
    public static OperationResult Fail(string message) => new OperationResult().AddError("", message);
}
