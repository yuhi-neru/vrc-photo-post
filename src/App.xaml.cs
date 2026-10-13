using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;

namespace VrcPhotoPost;

public partial class App : Application
{
    Mutex? mutex;
    readonly CancellationTokenSource stop = new();
    internal static string DataDirectory { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VrcPhotoPostV2");
    internal static string? ProbeDirectory { get; private set; }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var arguments = e.Args.ToList();
        if (arguments.Count >= 2 && arguments[0] == "--probe") {
            ProbeDirectory = Path.GetFullPath(arguments[1]); DataDirectory = Path.Combine(ProbeDirectory, "profile"); arguments.Clear();
        }
        mutex = new Mutex(true, ProbeDirectory is null ? "Local\\VrcPhotoPostV2" : "Local\\VrcPhotoPostV2.Probe", out bool first);
        if (!first) {
            try {
                using var client = new NamedPipeClientStream(".", "VrcPhotoPostV2.Open", PipeDirection.Out, PipeOptions.Asynchronous);
                await client.ConnectAsync(3000);
                using var writer = new StreamWriter(client, new UTF8Encoding(false));
                await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(arguments));
            } catch { MessageBox.Show("起動済みのアプリに接続できません。アプリを閉じて、もう一度写真を開いてください。"); }
            Shutdown(); return;
        }
        Directory.CreateDirectory(DataDirectory);
        var window = new MainWindow(arguments.Where(Photo.IsImage).Select(Path.GetFullPath).ToArray());
        MainWindow = window; window.Show();
        if (ProbeDirectory is null) _ = ReceiveFiles(window);
    }
    async Task ReceiveFiles(MainWindow window)
    {
        while (!stop.IsCancellationRequested) {
            try {
                using var pipe = new NamedPipeServerStream("VrcPhotoPostV2.Open", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(pipe);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(3000);
                var line = await reader.ReadLineAsync(timeout.Token);
                if (line is null || line.Length > 32768) continue;
                var paths = System.Text.Json.JsonSerializer.Deserialize<string[]>(line) ?? [];
                await Dispatcher.InvokeAsync(async () => { window.Show(); window.Activate(); if (paths.Length > 0) await window.OpenPhotos(paths.Where(Photo.IsImage).ToArray()); });
            } catch (OperationCanceledException) { } catch (IOException) { } catch (System.Text.Json.JsonException) { }
        }
    }
    protected override void OnExit(ExitEventArgs e) { stop.Cancel(); mutex?.Dispose(); base.OnExit(e); }
}
