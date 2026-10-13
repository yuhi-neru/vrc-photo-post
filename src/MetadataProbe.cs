using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace VrcPhotoPost;
public partial class MainWindow
{
    async Task ProbeMetadataAndText(string directory, string original, List<object> report, string fixtureHtml)
    {
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); report.Add(new { test = name, passed = true }); }
        var source = await File.ReadAllBytesAsync(original);
        var metaDirectory = Path.Combine(directory, "metadata-fixtures"); Directory.CreateDirectory(metaDirectory);
        string Png(string filename, string packet, bool compressed = false, bool invalidCrc = false) {
            var data = Encoding.UTF8.GetBytes(packet);
            if (compressed) { using var buffer = new MemoryStream(); using (var zip = new ZLibStream(buffer, CompressionLevel.Optimal, true)) zip.Write(data); data = buffer.ToArray(); }
            using var payload = new MemoryStream(); payload.Write(Encoding.ASCII.GetBytes("XML:com.adobe.xmp\0")); payload.WriteByte(compressed ? (byte)1 : (byte)0); payload.Write(new byte[] {0,0,0}); payload.Write(data);
            var bytes = payload.ToArray(); var type = Encoding.ASCII.GetBytes("iTXt");
            var path = Path.Combine(metaDirectory, filename); using var file = File.Create(path);
            file.Write(source.AsSpan(0, source.Length - 12));
            var header = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(header, (uint)bytes.Length); file.Write(header); file.Write(type); file.Write(bytes);
            BinaryPrimitives.WriteUInt32BigEndian(header, PhotoMetadata.Crc32(type, bytes) ^ (invalidCrc ? 1u : 0)); file.Write(header); file.Write(source.AsSpan(source.Length - 12));
            return path;
        }
        string Packet(string name, string prefix = "vrc", bool attribute = false, string? ns = null) {
            var escaped = new XText(name).ToString();
            return attribute ? $"<rdf:Description xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:{prefix}=\"{ns ?? PhotoMetadata.WorldNamespace}\" {prefix}:WorldDisplayName=\"{name.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;")}\"/>" : $"<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" xmlns:{prefix}=\"{ns ?? PhotoMetadata.WorldNamespace}\"><{prefix}:WorldDisplayName>{escaped}</{prefix}:WorldDisplayName></x:xmpmeta>";
        }
        var unusual = "日本語の世界 \"引用\" 'single' <tag> & \\ );window.__injected=1;//";
        var one = Png("namespace-alias.png", Packet(unusual, "other"));
        var two = Png("compressed-attribute.png", Packet("喫茶 雨音", "alias", attribute:true), compressed:true);
        Check(PhotoMetadata.ReadWorldName(one) == unusual, "PNG XMP namespace aliases, Japanese, quotes and code-like strings remain data");
        Check(PhotoMetadata.ReadWorldName(two) == "喫茶 雨音", "compressed PNG XMP and RDF attribute form");
        Check(PhotoMetadata.ReadWorldName(original) is null && DraftTextSession.Build([]) == "#VRchat", "missing metadata defaults to exact #VRchat");
        var broken = Png("broken.png", "<xmp><broken>");
        var foreign = Png("foreign-namespace.png", Packet("wrong", ns:"https://attacker.invalid/"));
        var crc = Png("bad-crc.png", Packet("bad"), invalidCrc:true);
        var dtd = Png("dtd.png", "<!DOCTYPE x [<!ENTITY unsafe SYSTEM 'file:///not-allowed'>]><x>&unsafe;</x>");
        Check(new[] {broken,foreign,crc,dtd}.All(p=>PhotoMetadata.ReadWorldName(p) is null), "malformed XML, foreign namespace, CRC corruption and external entities are rejected safely");
        var bomb = Png("oversized-xmp.png", new string('x', 1_048_577), compressed:true);
        Check(PhotoMetadata.ReadWorldName(bomb) is null, "compressed metadata expansion is bounded");
        var ordered = DraftTextSession.Build(new[] { PhotoMetadata.ReadWorldName(two), PhotoMetadata.ReadWorldName(one), PhotoMetadata.ReadWorldName(two), null });
        Check(ordered == "喫茶 雨音\n"+unusual+"\n#VRchat", "four selected photos deduplicate worlds in selection order with one final hashtag");
        var originalAfter = await File.ReadAllBytesAsync(original);
        Check(source.SequenceEqual(originalAfter), "metadata fixtures and reading preserve original PNG bytes");
        string? realDefault = null;
        var realPath = Environment.GetEnvironmentVariable("VRC_PHOTO_POST_REAL_PHOTO");
        if (!string.IsNullOrEmpty(realPath)) {
            var before = SHA256.HashData(await File.ReadAllBytesAsync(realPath));
            var name = PhotoMetadata.ReadWorldName(realPath);
            var realAfter = SHA256.HashData(await File.ReadAllBytesAsync(realPath));
            Check(!string.IsNullOrEmpty(name) && before.SequenceEqual(realAfter), "real VRChat original PNG supplies world name without modifying source");
            Check(DraftTextSession.Build([name]) == name+"\n#VRchat", "real VRChat world produces exact default post text");
            realDefault = DraftTextSession.Build([name]);
        }
        var session = new DraftTextSession();
        Check(session.TryConsume() && !session.TryConsume(), "default initialization is consumed once before insertion");
        // Navigation, photo selection and user editing do not create another session.
        session.Protect(); Check(!session.TryConsume(), "reload, edited or deleted text and changed photos cannot reinitialize a draft");
        session.BeginNewDraft(); Check(session.TryConsume(), "only explicit new draft rearms initial text");

        var attached = await XComposer.ReadState(Browser.CoreWebView2);
        Check((await ComposerText.Insert(Browser.CoreWebView2, ordered, false, probe:true)).Kind == "protected" && (await XComposer.ReadState(Browser.CoreWebView2)).Previews == attached.Previews, "existing attachments protect the entire draft");
        var loaded = new TaskCompletionSource();
        void Completed(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e) { if(e.IsSuccess)loaded.TrySetResult();else loaded.TrySetException(new Exception("Text fixture navigation failed")); }
        Browser.CoreWebView2.NavigationCompleted += Completed;
        Browser.CoreWebView2.NavigateToString(fixtureHtml);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20)); Browser.CoreWebView2.NavigationCompleted -= Completed;
        if (realDefault is not null) {
            Check((await ComposerText.Insert(Browser.CoreWebView2, realDefault, false, probe:true)).Kind == "inserted", "real PNG world and newline hashtag inserted into empty local WebView2 composer");
            DefaultPostText.Text = realDefault;
            WorldNames.Clear(); foreach (var name in DraftTextSession.Worlds([PhotoMetadata.ReadWorldName(realPath!)])) WorldNames.Add(name);
            Filmstrip.ItemsSource = new[] {new Photo(realPath!)}; Filmstrip.SelectedIndex = 0;
            TextStatus.Text = "元PNGのワールド名と初期投稿文をローカル検証しました。実Xの文章入力は未検証です。";
            using (var image = File.Create(Path.Combine(directory, "metadata-text-preview.png"))) await Browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
            await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[contenteditable]').textContent='';");
        }
        await Browser.CoreWebView2.ExecuteScriptAsync("window.editEvents=0;document.querySelector('[contenteditable]').addEventListener('input',()=>window.editEvents++);");
        Check((await ComposerText.Insert(Browser.CoreWebView2, ordered, false, probe:true)).Kind == "inserted", "real WebView2 plain-text input into empty composer");
        using (var proof = JsonDocument.Parse(await Browser.CoreWebView2.ExecuteScriptAsync("({text:document.querySelector('[contenteditable]').innerText,events:window.editEvents,injected:!!window.__injected})"))) {
            Check(proof.RootElement.GetProperty("text").GetString() == ordered && proof.RootElement.GetProperty("events").GetInt32() > 0 && !proof.RootElement.GetProperty("injected").GetBoolean(), "editing events fired and untrusted metadata was not executed");
        }
        Check((await ComposerText.Insert(Browser.CoreWebView2, "overwrite", false, probe:true)).Kind == "protected", "existing post text is never overwritten by default initialization");
        Check((await ComposerText.Insert(Browser.CoreWebView2, unusual, true, probe:true)).Kind == "duplicate" && (await ComposerText.Insert(Browser.CoreWebView2, "#VRchat", true, probe:true)).Kind == "duplicate", "repeated world and hashtag chip clicks do not duplicate existing content");
        Check((await ComposerText.Insert(Browser.CoreWebView2, "次のワールド", true, probe:true)).Kind == "inserted", "explicit chip appends at end while preserving existing text");
        await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[contenteditable]').textContent='';window.__vrcPhotoPostEdited=true;");
        Check((await ComposerText.Insert(Browser.CoreWebView2, ordered, false, probe:true)).Kind == "protected", "previous user editing protects even an empty editor");
        await Browser.CoreWebView2.ExecuteScriptAsync("delete window.__vrcPhotoPostEdited;");
        Check((await ComposerText.Insert(Browser.CoreWebView2, "#VRchat", false, probe:true)).Kind == "inserted", "metadata-free photo initializes hashtag only");
        await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[contenteditable]').textContent='#VRChat';");
        Check((await ComposerText.Insert(Browser.CoreWebView2, "#VRchat", true, probe:true)).Kind == "duplicate", "hashtag chip deduplication ignores hashtag case");
        await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[contenteditable]').textContent=' ';const another=document.createElement('div');another.contentEditable='true';another.dataset.testid='tweetTextarea_0';document.body.append(another);");
        Check((await ComposerText.Insert(Browser.CoreWebView2, ordered, false, probe:true)).Kind == "unavailable", "multiple visible editors refuse automatic input");
        await Browser.CoreWebView2.ExecuteScriptAsync("document.body.lastElementChild.remove();");
        Check((await ComposerText.Insert(Browser.CoreWebView2, ordered, false, probe:true)).Kind == "protected", "even whitespace-only user text is protected");
        await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[contenteditable]').remove();");
        Check((await ComposerText.Insert(Browser.CoreWebView2, ordered, false, probe:true)).Kind == "unavailable", "unknown editor state refuses automatic input");
        Check((await ComposerText.Insert(Browser.CoreWebView2, ordered, false)).Kind == "unavailable", "text input refuses non-X production origins");
    }
}
