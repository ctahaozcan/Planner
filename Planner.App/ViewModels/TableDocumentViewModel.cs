using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public partial class TableDocumentViewModel : ObservableObject
{
    private readonly DocumentService _docs;
    private readonly DocumentExportService _export;
    private readonly IAppDialogs _dialogs;
    private readonly DispatcherTimer _saveTimer;
    private bool _saving;
    private bool _ready;

    public TableDocumentViewModel(
        Guid id,
        DocumentService docs,
        DocumentExportService export,
        IAppDialogs dialogs)
    {
        Id = id;
        _docs = docs;
        _export = export;
        _dialogs = dialogs;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _saveTimer.Tick += async (_, _) =>
        {
            _saveTimer.Stop();
            await SaveQuietAsync();
        };
    }

    public Guid Id { get; }
    public Func<TableDocument>? CaptureTable { get; set; }
    public Action<TableDocument>? ApplyTable { get; set; }

    [ObservableProperty] private string _title = "Adsız e-tablo";
    [ObservableProperty] private string _status = "Hazır";
    [ObservableProperty] private string _cellRef = "A1";
    [ObservableProperty] private string _formulaText = "";
    [ObservableProperty] private string _limits = $"En fazla {DocumentService.MaxTableRows} satır × {DocumentService.MaxTableCols} sütun · formül: =SUM(A1:A10)";

    partial void OnTitleChanged(string value)
    {
        if (_ready)
        {
            ScheduleSave();
        }
    }

    public async Task InitializeAsync()
    {
        var doc = await _docs.GetAsync(Id);
        if (doc is null)
        {
            return;
        }

        Title = doc.Title;
        ApplyTable?.Invoke(TableDocument.Parse(doc.Body));
        Status = "Tüm değişiklikler kaydedildi";
        _ready = true;
    }

    public void ScheduleSave()
    {
        if (!_ready)
        {
            return;
        }
        Status = "Kaydediliyor…";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public async Task FlushAsync()
    {
        _saveTimer.Stop();
        await SaveQuietAsync();
    }

    private async Task SaveQuietAsync()
    {
        if (_saving)
        {
            return;
        }

        _saving = true;
        try
        {
            var table = CaptureTable?.Invoke() ?? TableDocument.Empty();
            await _docs.SaveAsync(Id, Title, table.ToJson());
            Status = "Tüm değişiklikler kaydedildi";
        }
        finally
        {
            _saving = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync() => await FlushAsync();

    [RelayCommand]
    private async Task ExportWordAsync()
    {
        await FlushAsync();
        var path = _dialogs.SaveFile("Word (*.docx)|*.docx", $"{TextDocumentViewModel.SafeFile(Title)}.docx");
        if (path is null) return;
        _export.ExportWordTable(path, Title, CaptureTable?.Invoke() ?? TableDocument.Empty());
        Status = "Word dosyası indirildi";
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        await FlushAsync();
        var path = _dialogs.SaveFile("Excel (*.xlsx)|*.xlsx", $"{TextDocumentViewModel.SafeFile(Title)}.xlsx");
        if (path is null) return;
        _export.ExportExcel(path, Title, CaptureTable?.Invoke() ?? TableDocument.Empty());
        Status = "Excel dosyası indirildi";
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        await FlushAsync();
        var path = _dialogs.SaveFile("PDF (*.pdf)|*.pdf", $"{TextDocumentViewModel.SafeFile(Title)}.pdf");
        if (path is null) return;
        _export.ExportPdfTable(path, Title, CaptureTable?.Invoke() ?? TableDocument.Empty());
        Status = "PDF indirildi";
    }
}
