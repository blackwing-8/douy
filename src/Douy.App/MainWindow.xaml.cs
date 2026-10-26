using System.Windows;
using Microsoft.Win32;

namespace Douy.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _model;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _model.ToggleAsync();
        }
        catch (Exception ex)
        {
            _model.Status = ex.Message;
        }
    }

    private void ChooseVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要送到手机的视频",
            Filter = "视频|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            _model.SelectVideo(dialog.FileName);
        }
    }

    private void ClearVideo_Click(object sender, RoutedEventArgs e)
    {
        _model.ClearVideo();
    }

    private void CopyAddress_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string address })
        {
            _model.CopyAddress(address);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            _model.SelectVideo(files[0]);
        }
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        await _model.ShutdownAsync();
    }
}
