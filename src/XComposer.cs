using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace VrcPhotoPost;
public record ComposerState(string Kind, int Previews, bool Existing, JsonElement? Diagnostics = null);
public static class XComposer
{
    public const string StateScript = """
        (() => {
          const diagnostics = {
            editorWrappers: document.querySelectorAll('[data-testid="tweetTextarea_0"]').length,
            editableElements: document.querySelectorAll('[contenteditable="true"]').length,
            imageInputs: document.querySelectorAll('input[type="file"][accept*="image"]').length,
            fileInputs: document.querySelectorAll('input[type="file"]').length,
            dialogs: document.querySelectorAll('[role="dialog"]').length,
            composeRoute: location.pathname.startsWith('/compose/')
          };
          if (/\/login|\/i\/flow|\/i\/jf\/onboarding/.test(location.pathname)) return {kind:'login',previews:0,existing:false};
          const editors = [...document.querySelectorAll('[data-testid="tweetTextarea_0"][contenteditable="true"], [data-testid="tweetTextarea_0"] [contenteditable="true"]')].filter(e=>e.getClientRects().length);
          if (editors.length !== 1) return {kind:'waiting',previews:0,existing:false,diagnostics};
          const editor = editors[0];
          const scope = editor.closest('[role="dialog"]') || (location.pathname.startsWith('/compose/') ? (editor.closest('main') || document.body) : null);
          if (!scope || !scope.querySelector('input[type="file"][accept*="image"]')) return {kind:'waiting',previews:0,existing:false,diagnostics};
          const visible = e => e.getClientRects().length && getComputedStyle(e).visibility !== 'hidden';
          const removals = [...scope.querySelectorAll('[data-testid="removeMedia"], button[aria-label="メディアを削除"], button[aria-label="Remove media"]')].filter(visible);
          const imgs = [...scope.querySelectorAll('img')].filter(i=>visible(i) && i.complete && i.naturalWidth>0 &&
            (i.closest('[data-testid="attachments"]') || /^blob:/.test(i.currentSrc || i.src) || /^https:\/\/pbs\.twimg\.com\/media\//.test(i.currentSrc || i.src)));
          const remove = removals.length;
          diagnostics.attachmentContainers = scope.querySelectorAll('[data-testid="attachments"]').length;
          diagnostics.attachmentImages = imgs.length;
          diagnostics.imageElements = scope.querySelectorAll('img').length;
          diagnostics.roleImages = scope.querySelectorAll('[role="img"]').length;
          diagnostics.removalControls = scope.querySelectorAll('[data-testid="removeMedia"]').length;
          diagnostics.localizedRemovalControls = scope.querySelectorAll('button[aria-label="メディアを削除"],button[aria-label="Remove media"]').length;
          const existing = !!editor.textContent.trim() || removals.length > 0 || imgs.length > 0;
          // X's persistent character-count meter also has role=progressbar.
          // Only media-area progress can block the attachment confirmation.
          const uploading = existing && [...scope.querySelectorAll('[data-testid="attachments"] [role="progressbar"]')].some(visible);
          return {kind:uploading?'uploading':'ready',previews:Math.min(imgs.length,remove),existing,diagnostics};
        })()
        """;
    public static async Task<ComposerState> ReadState(CoreWebView2 web)
    {
        using var doc = JsonDocument.Parse(await web.ExecuteScriptAsync(StateScript));
        var root = doc.RootElement;
        return new(root.GetProperty("kind").GetString()!, root.GetProperty("previews").GetInt32(), root.GetProperty("existing").GetBoolean(), root.TryGetProperty("diagnostics", out var diagnostics) ? diagnostics.Clone() : null);
    }
    public static bool IsX(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "x.com";
    public static async Task SetFiles(CoreWebView2 web, string[] paths, bool probe = false)
    {
        if (!probe && !IsX(web.Source)) throw new InvalidOperationException("X以外の画面には写真を渡しません。");
        var expression = probe ? "document.querySelector('input[type=file]')" : """
          (() => {
            const editors = [...document.querySelectorAll('[data-testid="tweetTextarea_0"][contenteditable="true"], [data-testid="tweetTextarea_0"] [contenteditable="true"]')].filter(e=>e.getClientRects().length);
            if (editors.length !== 1) return null;
            const editor = editors[0];
            const scope = editor.closest('[role="dialog"]') || (location.pathname.startsWith('/compose/') ? (editor.closest('main') || document.body) : null);
            return scope?.querySelector('input[type="file"][accept*="image"]');
          })()
          """;
        using var result = JsonDocument.Parse(await web.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression, returnByValue = false })));
        var objectId = result.RootElement.GetProperty("result").GetProperty("objectId").GetString();
        try {
            using var node = JsonDocument.Parse(await web.CallDevToolsProtocolMethodAsync("DOM.describeNode", JsonSerializer.Serialize(new { objectId })));
            var backendNodeId = node.RootElement.GetProperty("node").GetProperty("backendNodeId").GetInt32();
            if (!probe && !IsX(web.Source)) throw new InvalidOperationException("画面が変更されたため添付を止めました。");
            await web.CallDevToolsProtocolMethodAsync("DOM.setFileInputFiles", JsonSerializer.Serialize(new { files = paths, backendNodeId }));
        } finally { await web.CallDevToolsProtocolMethodAsync("Runtime.releaseObject", JsonSerializer.Serialize(new { objectId })); }
    }
    public static async Task<string[]> Snapshot(string[] originals, string directory)
    {
        if (originals.Length is < 1 or > 4) throw new InvalidOperationException("写真は1〜4枚選んでください。");
        return await Task.Run(() => {
            Directory.CreateDirectory(directory); var copies = new List<string>();
            try {
                foreach (var path in originals) {
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension is not (".png" or ".jpg" or ".jpeg")) throw new InvalidOperationException("Xへの添付はPNG／JPEGに対応しています。");
                    var before = new FileInfo(path); var size = before.Length; var modified = before.LastWriteTimeUtc;
                    Thread.Sleep(300); before.Refresh();
                    if (size == 0 || before.Length != size || before.LastWriteTimeUtc != modified) throw new IOException("写真が保存中です。保存完了後に開き直してください。");
                    if (size > 5_000_000) throw new IOException("5MBを超える写真は添付できません。元画像は変更しません。");
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var target = Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);
                    copies.Add(target);
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                    Photo.ReadBitmap(target, 32); // Decode before passing a stable snapshot to X.
                }
                return copies.ToArray();
            } catch { foreach (var copy in copies) File.Delete(copy); throw; }
        });
    }
}
