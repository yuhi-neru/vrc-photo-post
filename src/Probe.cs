using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace VrcPhotoPost;
public partial class MainWindow
{
    async Task RunProbe(string directory)
    {
        Directory.CreateDirectory(directory);
        var report = new List<object>();
        try {
            var original = Path.Combine(directory, "検証 写真.png");
            var pixels = new byte[64 * 64 * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 80; pixels[i + 1] = 160; pixels[i + 2] = 240; pixels[i + 3] = 255; }
            var bitmap = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(original)) encoder.Save(file);
            var bytes = await File.ReadAllBytesAsync(original);
            var copies = await XComposer.Snapshot([original], sessionDirectory);
            var unchanged = await File.ReadAllBytesAsync(original);
            var snapshotBytes = await File.ReadAllBytesAsync(copies[0]);
            if (!bytes.SequenceEqual(unchanged) || !bytes.SequenceEqual(snapshotBytes)) throw new Exception("Original preservation failed");
            report.Add(new { test = "read-only original and snapshot", passed = true });
            try { await XComposer.Snapshot(Enumerable.Repeat(original, 5).ToArray(), sessionDirectory); throw new Exception("Fifth image accepted"); }
            catch (InvalidOperationException) { report.Add(new { test = "five images rejected", passed = true }); }
            var oversized = Path.Combine(directory, "oversized.png");
            using (var file = File.Create(oversized)) file.SetLength(5_000_001);
            try { await XComposer.Snapshot([oversized], sessionDirectory); throw new Exception("Oversized image accepted"); }
            catch (IOException) { report.Add(new { test = "oversized image rejected", passed = true }); }
            if (!XComposer.IsX("https://x.com/compose/post") || XComposer.IsX("https://x.com.attacker.invalid/") || XComposer.IsX("http://x.com/")) throw new Exception("Origin guard failed");
            report.Add(new { test = "exact HTTPS X origin guard", passed = true });
            Library.Visibility = Visibility.Collapsed; Composer.Visibility = Visibility.Visible;
            Filmstrip.ItemsSource = new[] { new Photo(original) }; Filmstrip.SelectedIndex = 0;
            await Task.WhenAll(InitializeBrowser(), InitializeBrowser());
            var loaded = new TaskCompletionSource();
            void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e) { if (e.IsSuccess) loaded.TrySetResult(); else loaded.TrySetException(new Exception(e.WebErrorStatus.ToString())); }
            Browser.CoreWebView2.NavigationCompleted += Completed;
            var fixtureHtml = """
              <!doctype html><html lang="ja"><meta charset="utf-8"><style>body{font:18px sans-serif;padding:28px;color:#18243a}img{width:220px;height:220px;image-rendering:pixelated}pre{white-space:pre-wrap}</style>
              <h1>ファイル自動添付の実機検証</h1><p>WebView2のファイル入力に合成PNGを渡します。Xへの送信はありません。</p>
              <div role="dialog"><div data-testid="tweetTextarea_0" contenteditable="true"></div><input type="file" accept="image/*" multiple hidden><div role="progressbar" aria-label="文字数メーター">0</div></div><pre id="result">検証中</pre>
              <script>window.proof={change:false,count:0,width:0};document.querySelector('input').onchange=async e=>{proof={change:true,count:e.target.files.length,width:0};document.querySelector('[data-testid="attachments"]')?.remove();const attachments=document.createElement('div');attachments.dataset.testid='attachments';document.querySelector('[role=dialog]').append(attachments);for(const file of e.target.files){const img=document.createElement('img');img.alt='画像プレビュー';img.src=URL.createObjectURL(file);attachments.append(img);const remove=document.createElement('button');remove.dataset.testid='removeMedia';remove.textContent='削除（検証用）';attachments.append(remove);await img.decode();proof.width=img.naturalWidth;}document.querySelector('#result').textContent=JSON.stringify(proof,null,2);};</script></html>
              """;
            Browser.CoreWebView2.NavigateToString(fixtureHtml);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Browser.CoreWebView2.NavigationCompleted -= Completed;
            var beforeState = await XComposer.ReadState(Browser.CoreWebView2);
            if (beforeState.Kind != "ready" || beforeState.Existing || beforeState.Previews != 0) throw new Exception("Empty composer fixture not recognized");
            await XComposer.SetFiles(Browser.CoreWebView2, copies, probe: true);
            JsonElement proof = default;
            for (int i = 0; i < 30; i++) {
                await Task.Delay(200); using var parsed = JsonDocument.Parse(await Browser.CoreWebView2.ExecuteScriptAsync("window.proof")); proof = parsed.RootElement.Clone();
                if (proof.GetProperty("width").GetInt32() == 64) break;
            }
            if (!proof.GetProperty("change").GetBoolean() || proof.GetProperty("count").GetInt32() != 1 || proof.GetProperty("width").GetInt32() != 64) throw new Exception("WebView2 preview not confirmed");
            report.Add(new { test = "real WebView2 DOM.setFileInputFiles triggers change and decodes PNG", passed = true, proof });
            var state = await XComposer.ReadState(Browser.CoreWebView2);
            if (!state.Existing || state.Previews != 1 || state.Kind != "ready") throw new Exception("Preview fixture not recognized");
            report.Add(new { test = "composer state detects existing attachment with loaded preview", passed = true });
            await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-testid=removeMedia]').remove()");
            if ((await XComposer.ReadState(Browser.CoreWebView2)).Previews != 0) throw new Exception("Image without media control treated as confirmed");
            report.Add(new { test = "image without matching media control is not confirmed", passed = true });
            await XComposer.SetFiles(Browser.CoreWebView2, Enumerable.Repeat(copies[0], 4).ToArray(), probe: true);
            for (int i = 0; i < 30; i++) { await Task.Delay(200); state = await XComposer.ReadState(Browser.CoreWebView2); if (state.Previews == 4) break; }
            if (state.Previews != 4) throw new Exception("Four-image browser fixture failed");
            report.Add(new { test = "four files produce four loaded previews in real WebView2 fixture", passed = true });
            var fixtureDirectory = Path.Combine(directory, "fixture");
            Directory.CreateDirectory(Path.Combine(fixtureDirectory, "compose"));
            File.WriteAllText(Path.Combine(fixtureDirectory, "compose", "post.html"), fixtureHtml.Replace("role=\"dialog\"", "role=\"main\"").Replace("[role=dialog]", "[role=main]"));
            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping("vrc-probe.example", fixtureDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
            var readiness = new TaskCompletionSource();
            void Ready(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
                if (e.Source.StartsWith("https://vrc-probe.example/", StringComparison.Ordinal)) {
                    try { if (e.TryGetWebMessageAsString() == "vrc-photo-post:composer-changed") readiness.TrySetResult(); } catch (ArgumentException) { }
                }
            }
            Browser.CoreWebView2.WebMessageReceived += Ready;
            var observerId = await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(XComposer.ReadinessObserverScript.Replace("https://x.com", "https://vrc-probe.example"));
            var fullPageLoaded = new TaskCompletionSource();
            void FullPageCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) { if (e.IsSuccess) fullPageLoaded.TrySetResult(); else fullPageLoaded.TrySetException(new Exception(e.WebErrorStatus.ToString())); }
            Browser.CoreWebView2.NavigationCompleted += FullPageCompleted;
            Browser.CoreWebView2.Navigate("https://vrc-probe.example/compose/post.html");
            await fullPageLoaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Browser.CoreWebView2.NavigationCompleted -= FullPageCompleted;
            await readiness.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Browser.CoreWebView2.WebMessageReceived -= Ready;
            Browser.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(observerId);
            report.Add(new { test = "composer readiness notification arrives without polling", passed = true });
            var pageState = await XComposer.ReadState(Browser.CoreWebView2);
            if (pageState.Kind != "ready" || pageState.Existing) throw new Exception("Full-page composer fixture not recognized");
            await XComposer.SetFiles(Browser.CoreWebView2, copies, probe: true);
            for (int i = 0; i < 30; i++) { await Task.Delay(200); pageState = await XComposer.ReadState(Browser.CoreWebView2); if (pageState.Previews == 1) break; }
            if (pageState.Previews != 1 || !pageState.Existing) throw new Exception("Full-page composer preview missing");
            report.Add(new { test = "narrow full-page composer supports automatic attachment and preview", passed = true });
            await ProbeMetadataAndText(directory, original, report, fixtureHtml);
            using (var screenshot = File.Create(Path.Combine(directory, "webview-file-preview.png"))) await Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(new { passed = true, runtime = Browser.CoreWebView2.Environment.BrowserVersionString, tests = report, xAttachmentVerified = false, xTextVerified = false }, new JsonSerializerOptions { WriteIndented = true }));
            SetStatus("基礎検証に成功：WebView2でファイル変更と画像プレビューを確認。Xの添付は未検証です。");
        } catch (Exception error) {
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(new { passed = false, error = error.ToString(), tests = report }, new JsonSerializerOptions { WriteIndented = true })); SetStatus("基礎検証失敗：" + error.Message);
        }
        if (Environment.GetEnvironmentVariable("VRC_PHOTO_POST_PROBE_EXIT") == "1") Close();
    }
}
