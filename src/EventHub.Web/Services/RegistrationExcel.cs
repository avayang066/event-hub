using ClosedXML.Excel;
using EventHub.Web.Infrastructure;
using EventHub.Web.Models.Reports;

namespace EventHub.Web.Services;

/// <summary>
/// 報名名單匯出成 Excel（.xlsx）：上方是活動資訊與摘要，下方是名單表格。
/// - 手機以文字儲存，開頭的 0 不會不見
/// - 標題列凍結、可篩選；列印時橫向、寬度縮成一頁，每頁重複標題列
/// - 最後一欄「簽到」留白，可以印出來現場點名
/// 儲存格的值一律當成文字或數字寫入，不會被當成公式執行。
/// </summary>
public static class RegistrationExcel
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string DateTimeFormat = "yyyy/mm/dd hh:mm";
    private const string MoneyFormat = "#,##0";

    private static readonly XLColor HeaderFill = XLColor.FromHtml("#FBF0D9");
    private static readonly XLColor HeaderText = XLColor.FromHtml("#6B5220");
    private static readonly XLColor BorderColor = XLColor.FromHtml("#E9E1D6");
    private static readonly XLColor MutedText = XLColor.FromHtml("#8A7F74");

    /// <summary>一個欄位：標題、欄寬、取值、數字格式</summary>
    private sealed record Column(string Header, double Width, Func<RegistrationRowViewModel, XLCellValue> Value, string? Format = null);

    public static byte[] Build(EventRegistrationsViewModel model, DateTime exportedAt)
    {
        var columns = BuildColumns(model);

        using var workbook = new XLWorkbook();
        workbook.Style.Font.FontName = "Microsoft JhengHei";
        workbook.Style.Font.FontSize = 11;

        var ws = workbook.Worksheets.Add("報名名單");

        // ---------- 上方：活動資訊與摘要 ----------
        var row = 1;
        ws.Cell(row, 1).Value = model.Title;
        ws.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(15);
        row++;

        ws.Cell(row++, 1).Value = $"活動時間：{EventTimeFormatter.FormatRange(model.StartAt, model.EndAt)}";
        ws.Cell(row++, 1).Value = $"地點：{model.Location}";
        if (model.IsAdminView)
        {
            ws.Cell(row++, 1).Value = $"主辦方：{model.OrganizerName}";
        }

        ws.Cell(row++, 1).Value = model.IsFree
            ? $"報名成功 {model.Counts.Paid} 人　名額 {model.ReservedCount} / {model.Capacity}"
            : $"報名成功 {model.Counts.Paid} 人　名額 {model.ReservedCount} / {model.Capacity}　"
              + $"實收 NT$ {model.Amounts.Amount:N0}　平台手續費 NT$ {model.Amounts.PlatformFee:N0}　"
              + $"{(model.IsAdminView ? "應撥主辦方" : "主辦方實拿")} NT$ {model.Amounts.OrganizerAmount:N0}（只計算已付款）";

        ws.Cell(row, 1).Value = $"名單：{model.Filter.ToLabel()}，共 {model.Rows.Count} 筆　匯出時間：{exportedAt:yyyy/MM/dd HH:mm}";
        ws.Cell(row, 1).Style.Font.SetFontColor(MutedText);
        row += 2;

        // ---------- 名單表格 ----------
        var headerRow = row;
        for (var c = 0; c < columns.Count; c++)
        {
            ws.Cell(headerRow, c + 1).Value = columns[c].Header;
            ws.Column(c + 1).Width = columns[c].Width;
        }

        var header = ws.Range(headerRow, 1, headerRow, columns.Count);
        header.Style.Font.SetBold().Font.SetFontColor(HeaderText);
        header.Style.Fill.SetBackgroundColor(HeaderFill);

        for (var i = 0; i < model.Rows.Count; i++)
        {
            var r = model.Rows[i];
            var excelRow = headerRow + 1 + i;
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = ws.Cell(excelRow, c + 1);
                cell.Value = columns[c].Value(r);
                if (columns[c].Format is { } format)
                {
                    cell.Style.NumberFormat.Format = format;
                }
            }
        }

        var lastRow = headerRow + Math.Max(model.Rows.Count, 1);
        var table = ws.Range(headerRow, 1, lastRow, columns.Count);
        table.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(BorderColor);
        table.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorderColor(BorderColor);
        table.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Range(headerRow + 1, 1, lastRow, columns.Count).Style.Alignment.SetWrapText(false);

        if (model.Rows.Count > 0)
        {
            table.SetAutoFilter();
        }

        ws.SheetView.FreezeRows(headerRow);

        // ---------- 列印 ----------
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(headerRow, headerRow);
        ws.PageSetup.Margins.SetLeft(0.4).SetRight(0.4).SetTop(0.5).SetBottom(0.5);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static List<Column> BuildColumns(EventRegistrationsViewModel model)
    {
        var columns = new List<Column>
        {
            new("序號", 6, _ => Blank.Value),
            new("姓名", 14, r => r.AttendeeName),
            new("手機", 14, r => r.AttendeePhone),
            new("Email", 28, r => r.AttendeeEmail),
            new("備註", 24, r => r.Note ?? ""),
            new("狀態", 10, r => r.Status.ToLabel()),
        };

        if (!model.IsFree)
        {
            columns.Add(new("金額", 10, r => r.Amount, MoneyFormat));
            columns.Add(new("平台手續費", 11, r => r.PlatformFee, MoneyFormat));
            columns.Add(new("主辦方實拿", 11, r => r.OrganizerAmount, MoneyFormat));
        }

        columns.Add(new("報名時間", 17, r => r.CreatedAt, DateTimeFormat));
        if (!model.IsFree)
        {
            columns.Add(new("付款時間", 17, r => r.PaidAt is { } paidAt ? paidAt : Blank.Value, DateTimeFormat));
        }

        columns.Add(new("訂單編號", 24, r => r.OrderNo));
        columns.Add(new("簽到", 12, _ => Blank.Value));

        // 序號依列的順序編號
        var rowIndex = model.Rows
            .Select((r, i) => (r.OrderNo, Number: i + 1))
            .ToDictionary(x => x.OrderNo, x => x.Number);
        columns[0] = columns[0] with { Value = r => rowIndex[r.OrderNo] };

        return columns;
    }

    /// <summary>
    /// 下載檔名，例如「報名名單_週末手作課_20261118.xlsx」。
    /// 活動名稱裡不能當檔名的字元換成 _，太長就截斷。
    /// </summary>
    public static string FileName(EventRegistrationsViewModel model, DateTime now)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var title = new string(model.Title.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (title.Length > 40)
        {
            title = title[..40];
        }

        return string.IsNullOrEmpty(title)
            ? $"報名名單_{now:yyyyMMdd}.xlsx"
            : $"報名名單_{title}_{now:yyyyMMdd}.xlsx";
    }
}
