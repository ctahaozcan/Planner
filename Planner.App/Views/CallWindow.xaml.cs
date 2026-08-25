using System.Windows;
using System.Windows.Media.Imaging;
using Planner.App.Services;

namespace Planner.App.Views;

public partial class CallWindow : Window
{
    private readonly CallService _calls;

    public CallWindow(CallService calls)
    {
        InitializeComponent();
        _calls = calls;
        PeerLabel.Text = calls.Peer?.Name ?? "Arama";
        StatusLabel.Text = "Bağlanıyor…";
        calls.StatusChanged += OnStatus;
        calls.RemoteFrame += OnFrame;
        calls.Ended += OnEnded;
        Closed += (_, _) =>
        {
            calls.StatusChanged -= OnStatus;
            calls.RemoteFrame -= OnFrame;
            calls.Ended -= OnEnded;
            if (calls.IsInCall)
            {
                _ = calls.HangUpAsync();
            }
        };
    }

    private void OnStatus(string text) => Dispatcher.Invoke(() => StatusLabel.Text = text);

    private void OnFrame(BitmapSource frame) => Dispatcher.Invoke(() =>
    {
        RemoteImage.Source = frame;
        Placeholder.Visibility = Visibility.Collapsed;
    });

    private void OnEnded() => Dispatcher.Invoke(() =>
    {
        if (IsVisible)
        {
            Close();
        }
    });

    private void OnMic(object sender, RoutedEventArgs e)
    {
        _calls.ToggleMic();
        MicButton.Content = _calls.MicOn ? "Mikrofon" : "Mikrofon kapalı";
    }

    private void OnCamera(object sender, RoutedEventArgs e)
    {
        _calls.ToggleCamera();
        CamButton.Content = _calls.CameraOn ? "Kamera açık" : "Kamera";
    }

    private void OnShare(object sender, RoutedEventArgs e)
    {
        _calls.ToggleScreen();
        ShareButton.Content = _calls.SharingScreen ? "Paylaşım açık" : "Ekran paylaş";
    }

    private void OnHangUp(object sender, RoutedEventArgs e) => _ = _calls.HangUpAsync();
}
