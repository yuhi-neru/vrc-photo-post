using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace VrcPhotoPost;
public sealed class Photo(string path) : INotifyPropertyChanged
{
    static readonly SemaphoreSlim decodeSlots = new(6);
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public DateTime Date { get; } = File.GetLastWriteTime(path);
    public string DateLabel => Date.ToString("yyyy年M月d日  HH:mm");
    public string? WorldName { get; init; }
    BitmapSource? thumbnail;
    bool requested;
    public BitmapSource? Thumbnail {
        get {
            if (!requested) { requested = true; _ = Load(); }
            return thumbnail;
        }
    }
    async Task Load()
    {
        await decodeSlots.WaitAsync();
        try { var image = await Task.Run(() => ReadBitmap(Path, 360)); await Application.Current.Dispatcher.InvokeAsync(() => { thumbnail = image; Changed(nameof(Thumbnail)); }); }
        catch (IOException) { } catch (NotSupportedException) { } catch (FileFormatException) { }
        finally { decodeSlots.Release(); }
    }
    public static BitmapSource ReadBitmap(string path, int width = 0)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (width > 0) bitmap.DecodePixelWidth = width;
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
    public static bool IsImage(string path) => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());
    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
