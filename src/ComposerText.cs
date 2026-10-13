using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace VrcPhotoPost;
public record TextInsertion(string Kind);
public static class ComposerText
{
    // Parameters are serialized as data. execCommand inserts plain text atomically after the guards;
    // it emits Chromium's editing/input events instead of overwriting React's DOM with textContent.
    public static async Task<TextInsertion> Insert(CoreWebView2 web, string text, bool append, bool probe = false)
    {
        if (!probe && !XComposer.IsX(web.Source)) return new("unavailable");
        var args = JsonSerializer.Serialize(new { text, append, probe });
        var script = """
          ((args) => {
            if (!args.probe && location.origin !== 'https://x.com') return {kind:'unavailable'};
            const editors=[...document.querySelectorAll('[data-testid="tweetTextarea_0"][contenteditable="true"], [data-testid="tweetTextarea_0"] [contenteditable="true"]')].filter(e=>e.getClientRects().length && getComputedStyle(e).visibility!=='hidden');
            if(editors.length!==1) return {kind:'unavailable'};
            const editor=editors[0];
            const scope=editor.closest('[role="dialog"]') || (location.pathname.startsWith('/compose/') ? (editor.closest('main') || document.body) : null);
            if(!scope || !scope.querySelector('input[type="file"][accept*="image"]') || editor.getAttribute('aria-disabled')==='true') return {kind:'unavailable'};
            const before=editor.innerText.replace(/\r\n?/g,'\n');
            if(!args.append && (editor.textContent.length || before.length || window.__vrcPhotoPostEdited || scope.querySelector('[data-testid="attachments"], [data-testid="removeMedia"], button[aria-label="メディアを削除"], button[aria-label="Remove media"]'))) return {kind:'protected'};
            if(args.append && (args.text==='#VRchat' ? /#vrchat(?![\p{L}\p{N}_])/iu.test(before) : before.includes(args.text))) return {kind:'duplicate'};
            const suffix=(args.append && before.length && !before.endsWith('\n') ? '\n' : '')+args.text;
            editor.focus();
            const selection=window.getSelection(); if(!selection || document.activeElement!==editor) return {kind:'unavailable'};
            const range=document.createRange();range.selectNodeContents(editor);range.collapse(false);selection.removeAllRanges();selection.addRange(range);
            if(!document.execCommand('insertText',false,suffix)) return {kind:'unavailable'};
            const after=editor.innerText.replace(/\r\n?/g,'\n');
            return {kind:after===before+suffix ? 'inserted' : 'unconfirmed'};
          })
          """;
        using var result = JsonDocument.Parse(await web.ExecuteScriptAsync(script + "(" + args + ")"));
        var kind = result.RootElement.GetProperty("kind").GetString()!;
        if (kind == "inserted") {
            // Give the site's controlled editor time to reject/re-render the input.
            await Task.Delay(150);
            var check = """
              ((text) => {const e=[...document.querySelectorAll('[data-testid="tweetTextarea_0"][contenteditable="true"], [data-testid="tweetTextarea_0"] [contenteditable="true"]')].filter(e=>e.getClientRects().length);return e.length===1 && e[0].innerText.replace(/\r\n?/g,'\n').includes(text);})
              """;
            using var confirmed = JsonDocument.Parse(await web.ExecuteScriptAsync(check + "(" + JsonSerializer.Serialize(text) + ")"));
            if (confirmed.RootElement.ValueKind != JsonValueKind.True) kind = "unconfirmed";
        }
        return new(kind);
    }
}
