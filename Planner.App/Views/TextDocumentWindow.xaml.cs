using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Planner.App.ViewModels;

namespace Planner.App.Views;

public partial class TextDocumentWindow : Window
{
    private readonly TextDocumentViewModel _vm;
    private bool _loaded;
    private bool _allowClose;
    private bool _applying;

    public TextDocumentWindow(TextDocumentViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.CaptureBody = CaptureBody;
        vm.CapturePlain = CapturePlain;
        vm.ApplyBody = ApplyBody;
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

    private void ApplyBody(string body)
    {
        _applying = true;
        var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        try
        {
            if (!string.IsNullOrEmpty(body) && body.StartsWith("pkg:", StringComparison.Ordinal))
            {
                var bytes = Convert.FromBase64String(body[4..]);
                using var stream = new MemoryStream(bytes);
                range.Load(stream, System.Windows.DataFormats.XamlPackage);
                return;
            }

            if (!string.IsNullOrWhiteSpace(body) && body.TrimStart().StartsWith('<'))
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
                range.Load(stream, System.Windows.DataFormats.Xaml);
                return;
            }

            range.Text = body ?? "";
        }
        catch
        {
            range.Text = body ?? "";
        }
        finally
        {
            _applying = false;
        }
    }

    private string CaptureBody()
    {
        var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        using var stream = new MemoryStream();
        range.Save(stream, System.Windows.DataFormats.XamlPackage);
        return "pkg:" + Convert.ToBase64String(stream.ToArray());
    }

    private string CapturePlain()
    {
        var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        return range.Text ?? "";
    }

    private void OnEditorChanged(object sender, TextChangedEventArgs e)
    {
        if (!_applying)
        {
            _vm.ScheduleSave();
        }
    }

    private void OnStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        var label = (StyleBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Normal";
        var para = Editor.Selection.Start.Paragraph;
        if (para is null)
        {
            return;
        }

        switch (label)
        {
            case "Başlık":
                para.FontSize = 26;
                para.FontWeight = FontWeights.SemiBold;
                para.Margin = new Thickness(0, 0, 0, 12);
                break;
            case "Başlık 1":
                para.FontSize = 20;
                para.FontWeight = FontWeights.SemiBold;
                para.Margin = new Thickness(0, 14, 0, 8);
                break;
            case "Başlık 2":
                para.FontSize = 16;
                para.FontWeight = FontWeights.SemiBold;
                para.Margin = new Thickness(0, 12, 0, 6);
                break;
            default:
                para.FontSize = 12;
                para.FontWeight = FontWeights.Normal;
                para.Margin = new Thickness(0, 0, 0, 8);
                break;
        }

        Editor.Focus();
    }

    private void OnFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        var name = (FontBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (!string.IsNullOrWhiteSpace(name))
        {
            Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(name));
        }
    }

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        var text = (SizeBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (double.TryParse(text, out var size))
        {
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        }
    }

    private void OnStrike(object sender, RoutedEventArgs e)
    {
        var value = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        var on = value is TextDecorationCollection d && d == TextDecorations.Strikethrough;
        Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, on ? null : TextDecorations.Strikethrough);
        Editor.Focus();
    }

    private void OnTextColor(object sender, RoutedEventArgs e)
    {
        if (PickColor() is { } color)
        {
            Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(color));
        }

        Editor.Focus();
    }

    private void OnHighlight(object sender, RoutedEventArgs e)
    {
        if (PickColor() is { } color)
        {
            Editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(color));
        }

        Editor.Focus();
    }

    private static Color? PickColor()
    {
        var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return null;
        }

        return Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
    }

    private void OnInsertImage(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Görseller|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|Tüm dosyalar|*.*"
        };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(dlg.FileName);
            bmp.EndInit();
            bmp.Freeze();
            var image = new Image
            {
                Source = bmp,
                MaxWidth = 624,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 8, 0, 8)
            };
            var container = new BlockUIContainer(image);
            var para = Editor.CaretPosition.Paragraph;
            if (para is null)
            {
                Editor.Document.Blocks.Add(container);
            }
            else
            {
                Editor.Document.Blocks.InsertAfter(para, container);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Görsel eklenemedi: " + ex.Message, "Belge");
        }
    }

    private void OnInsertTable(object sender, RoutedEventArgs e)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 8, 0, 12) };
        for (var i = 0; i < 3; i++)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(180) });
        }

        var group = new TableRowGroup();
        var border = TryFindResource("CardBorderBrush") as Brush ?? Brushes.Gray;
        for (var r = 0; r < 3; r++)
        {
            var row = new TableRow();
            for (var c = 0; c < 3; c++)
            {
                row.Cells.Add(new TableCell(new Paragraph(new Run("")))
                {
                    BorderBrush = border,
                    BorderThickness = new Thickness(0.8),
                    Padding = new Thickness(6)
                });
            }

            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        var para = Editor.CaretPosition.Paragraph;
        if (para is null)
        {
            Editor.Document.Blocks.Add(table);
        }
        else
        {
            Editor.Document.Blocks.InsertAfter(para, table);
        }
    }
}
