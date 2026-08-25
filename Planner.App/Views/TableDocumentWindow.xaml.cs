using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Planner.App.Services;
using Planner.App.ViewModels;
using Planner.Core.Services;

namespace Planner.App.Views;

public partial class TableDocumentWindow : Window
{
    private readonly TableDocumentViewModel _vm;
    private readonly IAppDialogs _dialogs;
    private readonly DataTable _table = new();
    private TableDocument _book = TableDocument.Empty();
    private bool _loaded;
    private bool _allowClose;
    private bool _suspend;
    private int _selRow;
    private int _selCol;
    private bool _formulaCommitting;

    public TableDocumentWindow(TableDocumentViewModel vm, IAppDialogs dialogs)
    {
        InitializeComponent();
        _vm = vm;
        _dialogs = dialogs;
        DataContext = vm;
        vm.CaptureTable = CaptureTable;
        vm.ApplyTable = ApplyTable;
        Grid.ItemsSource = _table.DefaultView;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        try
        {
            await _vm.FlushAsync();
        }
        catch
        {
            // kayıt olmasa da pencere kapanabilsin
        }

        _allowClose = true;
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _vm.InitializeAsync();
    }

    private void ApplyTable(TableDocument document)
    {
        _book = document;
        if (_book.Sheets.Count == 0)
        {
            _book = TableDocument.Empty();
        }

        _book.ActiveIndex = Math.Clamp(_book.ActiveIndex, 0, _book.Sheets.Count - 1);
        RebuildGrid();
    }

    private TableDocument CaptureTable()
    {
        _book.SyncLegacy();
        return _book;
    }

    private TableSheet Sheet => _book.Sheets[Math.Clamp(_book.ActiveIndex, 0, Math.Max(0, _book.Sheets.Count - 1))];

    private void RebuildGrid()
    {
        _suspend = true;
        Grid.Columns.Clear();
        _table.Constraints.Clear();
        _table.Rows.Clear();
        _table.Columns.Clear();
        var sheet = Sheet;
        for (var i = 0; i < sheet.Headers.Count; i++)
        {
            var name = TableDocument.ColumnName(i);
            _table.Columns.Add(name, typeof(string));
            Grid.Columns.Add(new DataGridTextColumn
            {
                Header = name,
                Binding = new Binding(name) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
                Width = new DataGridLength(96)
            });
        }

        RefreshDisplay();
        Grid.ItemsSource = null;
        Grid.ItemsSource = _table.DefaultView;
        _suspend = false;
        RebuildTabs();
        SyncFormulaBar();
    }

    private void RefreshDisplay()
    {
        var sheet = Sheet;
        var eval = SheetFormula.EvaluateGrid(sheet.Headers, sheet.Rows);
        var cols = _table.Columns.Count;
        while (_table.Rows.Count < eval.Count)
        {
            _table.Rows.Add(_table.NewRow());
        }

        while (_table.Rows.Count > eval.Count)
        {
            _table.Rows.RemoveAt(_table.Rows.Count - 1);
        }

        for (var r = 0; r < eval.Count; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var value = c < eval[r].Count ? eval[r][c] : "";
                _table.Rows[r][c] = value;
            }
        }
    }

    private void RebuildTabs()
    {
        SheetBar.Children.Clear();
        for (var i = 0; i < _book.Sheets.Count; i++)
        {
            var index = i;
            var active = i == _book.ActiveIndex;
            var btn = new Button
            {
                Content = _book.Sheets[i].Name,
                Tag = index,
                Style = (Style)FindResource(active ? "SheetTabActive" : "SheetTab")
            };
            btn.Click += (_, _) => SelectSheet(index);
            btn.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                ShowSheetMenu(index, btn);
            };
            SheetBar.Children.Add(btn);
        }
    }

    private void SelectSheet(int index)
    {
        if (index == _book.ActiveIndex || index < 0 || index >= _book.Sheets.Count)
        {
            return;
        }

        _book.ActiveIndex = index;
        RebuildGrid();
        _vm.ScheduleSave();
    }

    private void ShowSheetMenu(int index, Button target)
    {
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "Yeniden adlandır" };
        rename.Click += (_, _) => RenameSheet(index);
        var del = new MenuItem { Header = "Sil", IsEnabled = _book.Sheets.Count > 1 };
        del.Click += (_, _) => DeleteSheet(index);
        menu.Items.Add(rename);
        menu.Items.Add(del);
        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private void RenameSheet(int index)
    {
        var name = _dialogs.Prompt("Sayfa adı", "Sayfanın adı", _book.Sheets[index].Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _book.Sheets[index].Name = name.Trim();
        RebuildTabs();
        _vm.ScheduleSave();
    }

    private void DeleteSheet(int index)
    {
        if (_book.Sheets.Count <= 1)
        {
            return;
        }

        if (!_dialogs.Confirm($"«{_book.Sheets[index].Name}» silinsin mi?", "Sayfayı sil"))
        {
            return;
        }

        _book.Sheets.RemoveAt(index);
        _book.ActiveIndex = Math.Clamp(_book.ActiveIndex, 0, _book.Sheets.Count - 1);
        if (_book.ActiveIndex >= _book.Sheets.Count)
        {
            _book.ActiveIndex = _book.Sheets.Count - 1;
        }

        RebuildGrid();
        _vm.ScheduleSave();
    }

    private void OnAddSheet(object sender, RoutedEventArgs e)
    {
        if (_book.Sheets.Count >= DocumentService.MaxSheets)
        {
            _dialogs.Info($"En fazla {DocumentService.MaxSheets} sayfa eklenebilir.");
            return;
        }

        var name = TableDocument.UniqueSheetName(_book.Sheets.Select(s => s.Name));
        _book.Sheets.Add(TableDocument.EmptySheet(name));
        _book.ActiveIndex = _book.Sheets.Count - 1;
        RebuildGrid();
        _vm.ScheduleSave();
    }

    private string GetRaw(int row, int col)
    {
        var sheet = Sheet;
        if (row < 0 || col < 0 || row >= sheet.Rows.Count || col >= sheet.Headers.Count)
        {
            return "";
        }

        var line = sheet.Rows[row];
        return col < line.Count ? line[col] ?? "" : "";
    }

    private void SetRaw(int row, int col, string value)
    {
        var sheet = Sheet;
        while (sheet.Rows.Count <= row)
        {
            sheet.Rows.Add(Enumerable.Repeat("", sheet.Headers.Count).ToList());
        }

        var line = sheet.Rows[row];
        while (line.Count <= col)
        {
            line.Add("");
        }

        line[col] = value ?? "";
    }

    private void OnLoadingRow(object sender, DataGridRowEventArgs e)
        => e.Row.Header = (e.Row.GetIndex() + 1).ToString();

    private void OnCurrentCellChanged(object? sender, EventArgs e) => SyncFormulaBar();

    private void SyncFormulaBar()
    {
        if (_formulaCommitting || FormulaBar.IsKeyboardFocusWithin)
        {
            return;
        }

        if (Grid.CurrentCell.Item is DataRowView drv && Grid.CurrentCell.Column is not null)
        {
            _selRow = _table.Rows.IndexOf(drv.Row);
            _selCol = Grid.Columns.IndexOf(Grid.CurrentCell.Column);
            if (_selRow >= 0 && _selCol >= 0)
            {
                _vm.CellRef = $"{TableDocument.ColumnName(_selCol)}{_selRow + 1}";
                FormulaBar.Text = GetRaw(_selRow, _selCol);
                return;
            }
        }

        _vm.CellRef = "A1";
    }

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is DataRowView drv)
        {
            _selRow = _table.Rows.IndexOf(drv.Row);
            _selCol = Grid.Columns.IndexOf(e.Column);
        }
    }

    private void OnPreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is not TextBox tb)
        {
            return;
        }

        if (e.Row.Item is DataRowView drv)
        {
            _selRow = _table.Rows.IndexOf(drv.Row);
            _selCol = Grid.Columns.IndexOf(e.Column);
        }

        tb.Text = GetRaw(_selRow, _selCol);
        tb.SelectAll();
        FormulaBar.Text = tb.Text;
    }

    private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_suspend || e.EditAction != DataGridEditAction.Commit)
        {
            return;
        }

        var text = e.EditingElement is TextBox tb ? tb.Text : "";
        if (e.Row.Item is DataRowView drv)
        {
            _selRow = _table.Rows.IndexOf(drv.Row);
            _selCol = Grid.Columns.IndexOf(e.Column);
        }

        SetRaw(_selRow, _selCol, text);
        Dispatcher.BeginInvoke(() =>
        {
            _suspend = true;
            RefreshDisplay();
            _suspend = false;
            SyncFormulaBar();
            _vm.ScheduleSave();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnFormulaKey(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter and not Key.Return)
        {
            return;
        }

        e.Handled = true;
        CommitFormulaBar();
        if (_selCol >= 0 && _selRow >= 0 && _selRow + 1 < _table.Rows.Count && _selCol < Grid.Columns.Count)
        {
            Grid.CurrentCell = new DataGridCellInfo(_table.DefaultView[_selRow + 1], Grid.Columns[_selCol]);
        }

        Grid.Focus();
    }

    private void CommitFormulaBar()
    {
        if (_suspend || _selRow < 0 || _selCol < 0)
        {
            return;
        }

        _formulaCommitting = true;
        SetRaw(_selRow, _selCol, FormulaBar.Text ?? "");
        _suspend = true;
        RefreshDisplay();
        _suspend = false;
        _formulaCommitting = false;
        _vm.ScheduleSave();
    }

    private void OnAddRow(object sender, RoutedEventArgs e)
    {
        var sheet = Sheet;
        if (sheet.Rows.Count >= DocumentService.MaxTableRows)
        {
            _dialogs.Info($"En fazla {DocumentService.MaxTableRows} satır eklenebilir.");
            return;
        }

        sheet.Rows.Add(Enumerable.Repeat("", sheet.Headers.Count).ToList());
        RebuildGrid();
        _vm.ScheduleSave();
    }

    private void OnAddColumn(object sender, RoutedEventArgs e)
    {
        var sheet = Sheet;
        if (sheet.Headers.Count >= DocumentService.MaxTableCols)
        {
            _dialogs.Info($"En fazla {DocumentService.MaxTableCols} sütun eklenebilir.");
            return;
        }

        sheet.Headers.Add(TableDocument.ColumnName(sheet.Headers.Count));
        foreach (var row in sheet.Rows)
        {
            row.Add("");
        }

        RebuildGrid();
        _vm.ScheduleSave();
    }

    private void OnDeleteRow(object sender, RoutedEventArgs e)
    {
        var sheet = Sheet;
        if (sheet.Rows.Count <= 1)
        {
            _dialogs.Info("En az bir satır kalmalı.");
            return;
        }

        var row = Math.Clamp(_selRow, 0, sheet.Rows.Count - 1);
        sheet.Rows.RemoveAt(row);
        _selRow = Math.Min(row, sheet.Rows.Count - 1);
        RebuildGrid();
        _vm.ScheduleSave();
    }

    private void OnDeleteColumn(object sender, RoutedEventArgs e)
    {
        var sheet = Sheet;
        if (sheet.Headers.Count <= 1)
        {
            _dialogs.Info("En az bir sütun kalmalı.");
            return;
        }

        var col = Math.Clamp(_selCol, 0, sheet.Headers.Count - 1);
        sheet.Headers.RemoveAt(col);
        foreach (var row in sheet.Rows)
        {
            if (col < row.Count)
            {
                row.RemoveAt(col);
            }
        }

        sheet.Headers = sheet.Headers.Select((_, i) => TableDocument.ColumnName(i)).ToList();
        _selCol = Math.Min(col, sheet.Headers.Count - 1);
        RebuildGrid();
        _vm.ScheduleSave();
    }
}
