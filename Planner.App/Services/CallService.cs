using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.Wave;
using Planner.Core.Models;

namespace Planner.App.Services;

public sealed class CallSignal
{
    public string T { get; set; } = "";
    public string Sid { get; set; } = "";
    public string Ip { get; set; } = "";
    public int A { get; set; } = 47830;
    public int V { get; set; } = 47831;
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "video";
}

public sealed class CallService : IDisposable
{
    public const int AudioPort = 47830;
    public const int VideoPort = 47831;

    private readonly ChatHub _hub;
    private WaveInEvent? _waveIn;
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _buffer;
    private UdpClient? _audioSend;
    private UdpClient? _audioRecv;
    private TcpListener? _videoListen;
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _screenTimer;
    private IPEndPoint? _peerAudio;
    private string? _peerVideoHost;
    private int _peerVideoPort;

    public event Action<CallSignal, ChatMessage>? IncomingInvite;
    public event Action? Ended;
    public event Action<BitmapSource>? RemoteFrame;
    public event Action<string>? StatusChanged;

    public bool IsInCall { get; private set; }
    public string SessionId { get; private set; } = "";
    public ChatPeer? Peer { get; private set; }
    public bool MicOn { get; private set; } = true;
    public bool SharingScreen { get; private set; }
    public bool CameraOn { get; private set; }

    public CallService(ChatHub hub)
    {
        _hub = hub;
        _hub.MessageReceived += OnMessage;
    }

    public async Task StartCallAsync(ChatPeer peer, string mode = "video")
    {
        Peer = peer;
        SessionId = Guid.NewGuid().ToString("N");
        var signal = new CallSignal
        {
            T = "invite",
            Sid = SessionId,
            Ip = LocalIp(),
            A = AudioPort,
            V = VideoPort,
            Name = _hub.DisplayName,
            Mode = mode
        };
        await _hub.SendAsync(peer, CollabPayload.Call + JsonSerializer.Serialize(signal));
        BeginMedia(signal.Ip, AudioPort, VideoPort, listen: true);
        StatusChanged?.Invoke("Aranıyor…");
    }

    public async Task AcceptAsync(CallSignal invite, ChatPeer peer)
    {
        Peer = peer;
        SessionId = invite.Sid;
        var accept = new CallSignal
        {
            T = "accept",
            Sid = invite.Sid,
            Ip = LocalIp(),
            A = AudioPort,
            V = VideoPort,
            Name = _hub.DisplayName,
            Mode = invite.Mode
        };
        await _hub.SendAsync(peer, CollabPayload.Call + JsonSerializer.Serialize(accept));
        BeginMedia(invite.Ip, invite.A, invite.V, listen: true);
        StatusChanged?.Invoke("Bağlandı");
    }

    public async Task HangUpAsync()
    {
        if (Peer is not null && !string.IsNullOrEmpty(SessionId))
        {
            try
            {
                await _hub.SendAsync(Peer, CollabPayload.Call + JsonSerializer.Serialize(new CallSignal { T = "end", Sid = SessionId }));
            }
            catch
            {
                // kopuk
            }
        }

        StopMedia();
        Ended?.Invoke();
    }

    public void ToggleMic()
    {
        MicOn = !MicOn;
        StatusChanged?.Invoke(MicOn ? "Mikrofon açık" : "Mikrofon kapalı");
    }

    public void ToggleScreen()
    {
        SharingScreen = !SharingScreen;
        StatusChanged?.Invoke(SharingScreen ? "Ekran paylaşılıyor" : "Ekran paylaşımı kapandı");
    }

    public void ToggleCamera()
    {
        CameraOn = !CameraOn;
        StatusChanged?.Invoke(CameraOn ? "Kamera açık (önizleme / ekran yedek)" : "Kamera kapalı");
    }

    private void OnMessage(ChatMessage message)
    {
        if (message.IsOutgoing || !CollabPayload.IsCall(message.Body))
        {
            return;
        }

        CallSignal? signal;
        try
        {
            signal = JsonSerializer.Deserialize<CallSignal>(message.Body[CollabPayload.Call.Length..]);
        }
        catch
        {
            return;
        }

        if (signal is null)
        {
            return;
        }

        if (signal.T == "invite")
        {
            Application.Current?.Dispatcher.Invoke(() => IncomingInvite?.Invoke(signal, message));
            return;
        }

        if (signal.T == "accept" && signal.Sid == SessionId)
        {
            _peerAudio = new IPEndPoint(IPAddress.Parse(signal.Ip), signal.A);
            _peerVideoHost = signal.Ip;
            _peerVideoPort = signal.V;
            StatusChanged?.Invoke("Karşı taraf katıldı");
            return;
        }

        if (signal.T == "end")
        {
            StopMedia();
            Application.Current?.Dispatcher.Invoke(() => Ended?.Invoke());
        }
    }

    private void BeginMedia(string peerIp, int audioPort, int videoPort, bool listen)
    {
        StopMedia();
        IsInCall = true;
        _cts = new CancellationTokenSource();
        try
        {
            _peerAudio = IPAddress.TryParse(peerIp, out var ip) ? new IPEndPoint(ip, audioPort) : null;
            _peerVideoHost = peerIp;
            _peerVideoPort = videoPort;
            _audioRecv = new UdpClient(new IPEndPoint(IPAddress.Any, AudioPort));
            _audioSend = new UdpClient();
            _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 40 };
            _buffer = new BufferedWaveProvider(_waveIn.WaveFormat);
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_buffer);
            _waveOut.Play();
            _waveIn.DataAvailable += OnAudio;
            _waveIn.StartRecording();
            _ = Task.Run(() => RecvAudio(_cts.Token));
            if (listen)
            {
                _videoListen = new TcpListener(IPAddress.Any, VideoPort);
                _videoListen.Start();
                _ = Task.Run(() => RecvVideo(_cts.Token));
            }

            _screenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _screenTimer.Tick += (_, _) => CaptureAndSend();
            _screenTimer.Start();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("Medya açılamadı: " + ex.Message);
        }
    }

    private void OnAudio(object? sender, WaveInEventArgs e)
    {
        if (!MicOn || _peerAudio is null || _audioSend is null)
        {
            return;
        }

        try { _audioSend.Send(e.Buffer, e.BytesRecorded, _peerAudio); }
        catch { /* ağ */ }
    }

    private async Task RecvAudio(CancellationToken ct)
    {
        if (_audioRecv is null)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var got = await _audioRecv.ReceiveAsync(ct);
                _buffer?.AddSamples(got.Buffer, 0, got.Buffer.Length);
            }
            catch
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private void CaptureAndSend()
    {
        if ((!SharingScreen && !CameraOn) || string.IsNullOrWhiteSpace(_peerVideoHost))
        {
            return;
        }

        try
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1280, 720);
            using var bmp = new System.Drawing.Bitmap(Math.Min(960, bounds.Width), Math.Min(540, bounds.Height));
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bmp.Size, System.Drawing.CopyPixelOperation.SourceCopy);
            }

            using var ms = new MemoryStream();
            var enc = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
            var ep = new System.Drawing.Imaging.EncoderParameters(1);
            ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 35L);
            bmp.Save(ms, enc, ep);
            var bytes = ms.ToArray();
            _ = SendVideoAsync(bytes);
        }
        catch
        {
            // paylaşım kesilebilir
        }
    }

    private async Task SendVideoAsync(byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(_peerVideoHost))
        {
            return;
        }

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_peerVideoHost, _peerVideoPort);
            await using var stream = client.GetStream();
            var len = BitConverter.GetBytes(bytes.Length);
            await stream.WriteAsync(len);
            await stream.WriteAsync(bytes);
        }
        catch
        {
            // karşı taraf dinlemiyor
        }
    }

    private async Task RecvVideo(CancellationToken ct)
    {
        if (_videoListen is null)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var client = await _videoListen.AcceptTcpClientAsync(ct);
                await using var stream = client.GetStream();
                var header = new byte[4];
                if (await stream.ReadAsync(header.AsMemory(0, 4), ct) < 4)
                {
                    continue;
                }

                var len = BitConverter.ToInt32(header);
                if (len is <= 0 or > 2_000_000)
                {
                    continue;
                }

                var data = new byte[len];
                var read = 0;
                while (read < len)
                {
                    var n = await stream.ReadAsync(data.AsMemory(read, len - read), ct);
                    if (n <= 0)
                    {
                        break;
                    }

                    read += n;
                }

                var bmp = new BitmapImage();
                using var ms = new MemoryStream(data, 0, read);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                Application.Current?.Dispatcher.Invoke(() => RemoteFrame?.Invoke(bmp));
            }
            catch
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private void StopMedia()
    {
        IsInCall = false;
        SharingScreen = false;
        CameraOn = false;
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _screenTimer?.Stop(); } catch { /* ignore */ }
        _screenTimer = null;
        try { _waveIn?.StopRecording(); } catch { /* ignore */ }
        _waveIn?.Dispose();
        _waveIn = null;
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _buffer = null;
        _audioSend?.Dispose();
        _audioSend = null;
        _audioRecv?.Dispose();
        _audioRecv = null;
        try { _videoListen?.Stop(); } catch { /* ignore */ }
        _videoListen = null;
        _cts?.Dispose();
        _cts = null;
    }

    private static string LocalIp()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                ?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    public void Dispose()
    {
        _hub.MessageReceived -= OnMessage;
        StopMedia();
    }
}
