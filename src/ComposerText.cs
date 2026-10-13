using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace VrcPhotoPost;
public record TextInsertion(string Kind, JsonElement? Diagnostics = null);
public static class ComposerText
{
    // Pass plain text to the site's paste handler. This never reads/writes the OS clipboard.
    // A controlled editor must retain the entire expected text before we report success.
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
            const readText=e=>{
              const blocks=[...e.querySelectorAll('[data-block="true"]')];
              if(blocks.length) return blocks.map(b=>b.textContent.length ? b.innerText.replace(/\r\n?/g,'\n').replace(/\n$/,'') : '').join('\n');
              return e.textContent.length ? e.innerText.replace(/\r\n?/g,'\n') : '';
            };
            const before=readText(editor);
            const visible=e=>e.getClientRects().length && getComputedStyle(e).visibility!=='hidden';
            const controls=[...scope.querySelectorAll('[data-testid="removeMedia"],button[aria-label="メディアを削除"],button[aria-label="Remove media"]')].filter(visible).length;
            const images=[...scope.querySelectorAll('img')].filter(i=>visible(i) && (i.closest('[data-testid="attachments"]') || /^blob:/.test(i.currentSrc||i.src) || /^https:\/\/pbs\.twimg\.com\/media\//.test(i.currentSrc||i.src))).length;
            const files=scope.querySelector('input[type="file"][accept*="image"]')?.files?.length || 0;
            if(!args.append && (editor.textContent.length || before.length || window.__vrcPhotoPostEdited || controls || images || files)) return {kind:'protected',diagnostics:{textLength:editor.textContent.length,renderedTextLength:before.length,edited:!!window.__vrcPhotoPostEdited,controls,images,files}};
            if(args.append && (args.text==='#VRchat' ? /#vrchat(?![\p{L}\p{N}_])/iu.test(before) : before.includes(args.text))) return {kind:'duplicate'};
            const suffix=(args.append && before.length && !before.endsWith('\n') ? '\n' : '')+args.text;
            editor.focus();
            const selection=window.getSelection(); if(!selection || document.activeElement!==editor) return {kind:'unavailable'};
            const range=document.createRange();range.selectNodeContents(editor);range.collapse(false);selection.removeAllRanges();selection.addRange(range);
            const data=new DataTransfer();data.setData('text/plain',suffix);
            editor.dispatchEvent(new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true}));
            return {kind:'pending',expected:before+suffix};
          })
          """;
        using var result = JsonDocument.Parse(await web.ExecuteScriptAsync(script + "(" + args + ")"));
        var kind = result.RootElement.GetProperty("kind").GetString()!;
        if (kind == "pending") {
            // Give the site's controlled editor time to reject/re-render the input.
            await Task.Delay(150);
            var check = """
              ((expected) => {
                const editors=[...document.querySelectorAll('[data-testid="tweetTextarea_0"][contenteditable="true"], [data-testid="tweetTextarea_0"] [contenteditable="true"]')].filter(e=>e.getClientRects().length && getComputedStyle(e).visibility!=='hidden');
                if(editors.length!==1)return false;
                const e=editors[0],blocks=[...e.querySelectorAll('[data-block="true"]')];
                const value=blocks.length ? blocks.map(b=>b.textContent.length ? b.innerText.replace(/\r\n?/g,'\n').replace(/\n$/,'') : '').join('\n') : (e.textContent.length ? e.innerText.replace(/\r\n?/g,'\n') : '');
                return value===expected;
              })
              """;
            if (!probe && !XComposer.IsX(web.Source)) return new("unconfirmed");
            using var confirmed = JsonDocument.Parse(await web.ExecuteScriptAsync(check + "(" + JsonSerializer.Serialize(result.RootElement.GetProperty("expected").GetString()) + ")"));
            kind = confirmed.RootElement.ValueKind == JsonValueKind.True ? "inserted" : "unconfirmed";
        }
        return new(kind, result.RootElement.TryGetProperty("diagnostics", out var diagnostics) ? diagnostics.Clone() : null);
    }
}
