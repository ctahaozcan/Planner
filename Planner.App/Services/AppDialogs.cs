using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Planner.App.ViewModels;
using Planner.App.Views;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.Services;

public interface IAppDialogs
{
    Task<bool> EditTaskAsync(Guid? taskId, DateOnly? presetDate, DateOnly? occurrenceDate = null, TimeOnly? presetTime = null);
    Task<bool> EditContactAsync(ContactRecord? contact);
    Task<LeaveRecord?> EditLeaveAsync(LeaveRecord? existing, LeaveEntryKind? presetKind = null);
    bool Confirm(string message, string title = "Onay");
    bool? ConfirmSeries(string message, string title = "Yineleyen kayıt");
    void Info(string message, string title = "Yaver");
    string? PromptPassword(string title, string message);
    string? SaveFile(string filter, string defaultName);
    string? OpenFile(string filter);
    string? OpenAnyFile();
}

public sealed class AppDialogs : IAppDialogs
{
    private readonly IServiceProvider _services;
    private readonly LeaveService _leaves;

    public AppDialogs(IServiceProvider services, LeaveService leaves)
    {
        _services = services;
        _leaves = leaves;
    }

    public async Task<bool> EditTaskAsync(Guid? taskId, DateOnly? presetDate, DateOnly? occurrenceDate = null, TimeOnly? presetTime = null)
    {
        var vm = _services.GetRequiredService<TaskEditorViewModel>();
        await vm.LoadAsync(taskId, presetDate, occurrenceDate, presetTime);
        var window = new TaskEditorWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };
        vm.CloseRequested += result =>
        {
            try
            {
                window.DialogResult = result;
            }
            catch
            {
                window.Close();
            }
        };
        return window.ShowDialog() == true;
    }

    public async Task<bool> EditContactAsync(ContactRecord? contact)
    {
        var vm = new ContactEditorViewModel(contact);
        await Task.CompletedTask;
        var window = new ContactEditorWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };
        vm.CloseRequested += result =>
        {
            try
            {
                window.DialogResult = result;
            }
            catch
            {
                window.Close();
            }
        };
        return window.ShowDialog() == true && vm.Result is not null;
    }

    public async Task<LeaveRecord?> EditLeaveAsync(LeaveRecord? existing, LeaveEntryKind? presetKind = null)
    {
        var types = await _leaves.GetTypesAsync();
        if (types.Count == 0)
        {
            Info("İzin türleri yüklenemedi. Uygulamayı yeniden açmayı dene.");
            return null;
        }

        var ctx = await _leaves.GetCountContextAsync();
        var vm = new LeaveEditorViewModel(types, existing, ctx, presetKind);
        var window = new LeaveEditorWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };
        vm.CloseRequested += result =>
        {
            try
            {
                window.DialogResult = result;
            }
            catch
            {
                window.Close();
            }
        };
        return window.ShowDialog() == true ? vm.Result : null;
    }

    public bool Confirm(string message, string title = "Onay")
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool? ConfirmSeries(string message, string title = "Yineleyen kayıt")
    {
        var result = MessageBox.Show(message + "\n\nEvet = tüm seri · Hayır = yalnızca bu oluşum · İptal", title,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null
        };
    }

    public void Info(string message, string title = "Yaver")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public string? PromptPassword(string title, string message)
    {
        var window = new PasswordPromptWindow(title, message)
        {
            Owner = Application.Current.MainWindow
        };
        return window.ShowDialog() == true ? window.Password : null;
    }

    public string? SaveFile(string filter, string defaultName)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            FileName = defaultName
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? OpenFile(string filter)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? OpenAnyFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Tüm dosyalar|*.*" };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
