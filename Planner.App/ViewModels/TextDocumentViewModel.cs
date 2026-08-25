using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public partial class TextDocumentViewModel : ObservableObject
{
    private readonly DocumentService _docs;
    private readonly DocumentExportService _export;
    private readonly IAppDialogs _dialogs;
    private readonly DispatcherTimer _saveTimer;
    private bool _saving;
    private bool _ready;

    public TextDocumentViewModel(
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
    public Func<string>? CaptureBody { get; set; }
    public Func<string>? CapturePlain { get; set; }
    public Action<string>? ApplyBody { get; set; }

    [ObservableProperty] private string _title = "Adsız belge";
    [ObservableProperty] private string _status = "Hazır";

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
        ApplyBody?.Invoke(doc.Body);
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
            await _docs.SaveAsync(Id, Title, CaptureBody?.Invoke() ?? "");
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
        var path = _dialogs.SaveFile("Word (*.docx)|*.docx", $"{SafeFile(Title)}.docx");
        if (path is null) return;
        _export.ExportWord(path, Title, CapturePlain?.Invoke() ?? "");
        Status = "Word dosyası indirildi";
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        await FlushAsync();
        var path = _dialogs.SaveFile("Excel (*.xlsx)|*.xlsx", $"{SafeFile(Title)}.xlsx");
        if (path is null) return;
        _export.ExportExcelFromText(path, Title, CapturePlain?.Invoke() ?? "");
        Status = "Excel dosyası indirildi";
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        await FlushAsync();
        var path = _dialogs.SaveFile("PDF (*.pdf)|*.pdf", $"{SafeFile(Title)}.pdf");
        if (path is null) return;
        _export.ExportPdf(path, Title, CapturePlain?.Invoke() ?? "");
        Status = "PDF indirildi";
    }

    public static string SafeFile(string title)
    {
        var name = string.IsNullOrWhiteSpace(title) ? "belge" : title.Trim();
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }
}
