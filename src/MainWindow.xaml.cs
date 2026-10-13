using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace VrcPhotoPost;
public partial class MainWindow : Window, INotifyPropertyChanged
{
    public ObservableCollection<Photo> Photos { get; } = [];
    public ObservableCollection<string> WorldNames { get; } = [];
    readonly DraftTextSession draftText = new();
    readonly List<string> selectionOrder = [];
    bool restoringSelection, textBusy;
    string generatedText = "#VRchat";
    public string Folder { get; private set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public string CountLabel => $"{all.Count:N0}枚の写真 · 新しい順";
    List<Photo> all = [];
    int limit = 180;
    readonly string[] initial;
    readonly string sessionDirectory = Path.Combine(App.DataDirectory, "attachments", Guid.NewGuid().ToString("N"));
    string[] pending = [];
    bool browserReady, busy, attempted, scanning, opening, navigating;
    Task? browserInitialization;
    int photoGeneration, navigationGeneration;
    DateTime queuedAt;
    string? evidencePhase;
    readonly DispatcherTimer watch = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly DispatcherTimer bridge = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public MainWindow(string[] paths)
    {
        InitializeComponent(); Title = $"VRC Photo Post · v{typeof(App).Assembly.GetName().Version?.ToString(3)}"; DataContext = this; initial = paths;
        try { Folder = JsonSerializer.Deserialize<string>(File.ReadAllText(Path.Combine(App.DataDirectory, "folder.json"))) ?? Folder; } catch { }
        Loaded += async (_, _) => {
            if (App.ProbeDirectory is not null) { await RunProbe(App.ProbeDirectory); return; }
            _ = WarmBrowser();
            if (initial.Length > 0) await OpenPhotos(initial);
            else SetStatus("写真をダブルクリックすると、写真とXの投稿画面を開きます。");
            await Scan(); watch.Tick += async (_, _) => await Scan(); watch.Start();
        };
        bridge.Tick += async (_, _) => await Tick();
        Closed += (_, _) => { watch.Stop(); bridge.Stop(); Browser.Dispose(); try { if (Directory.Exists(sessionDirectory)) Directory.Delete(sessionDirectory, true); } catch { } };
    }
    void SetStatus(string value) => Status.Text = value;
    void SetMetadata(Photo[] photos, string?[] names)
    {
        for (int i = 0; i < photos.Length; i++) photos[i].SetWorldName(names[i]);
        WorldNames.Clear(); foreach (var name in DraftTextSession.Worlds(names)) WorldNames.Add(name);
        generatedText = DraftTextSession.Build(names); DefaultPostText.Text = generatedText;
    }
    void RecordEvidence(string phase, int previews = 0, JsonElement? diagnostics = null)
    {
        var signature = phase + previews + diagnostics?.GetRawText();
        if (signature == evidencePhase) return;
        evidencePhase = signature;
        try {
            // No file names, account identity, page text, URLs, or credentials in this report.
            File.WriteAllText(Path.Combine(App.DataDirectory, "verification.json"), JsonSerializer.Serialize(new {
                phase, selectedCount = pending.Length, previewCount = previews, diagnostics,
                xAttachmentVerified = phase == "x-preview-confirmed",
                checkedAtUtc = DateTime.UtcNow,
                runtime = browserReady ? Browser.CoreWebView2.Environment.BrowserVersionString : null
            }, new JsonSerializerOptions { WriteIndented = true }));
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    async Task Scan()
    {
        if (scanning) return; scanning = true; var folder = Folder;
        try {
            var files = await Task.Run(() => Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Where(Photo.IsImage).Select(p => new Photo(p)).OrderByDescending(p => p.Date).ToList() : []);
            if (folder != Folder) return;
            if (!all.Select(p => (p.Path, p.Date)).SequenceEqual(files.Select(p => (p.Path, p.Date)))) {
                var old = all.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
                all = files.Select(p => old.TryGetValue(p.Path, out var existing) && existing.Date == p.Date ? existing : p).ToList();
                ApplyFilter(); Notify(nameof(CountLabel));
            }
            if (!Directory.Exists(folder)) SetStatus("写真フォルダが見つかりません。左側から保存先を設定してください。");
        } catch (Exception e) { SetStatus("写真の読み込みに失敗しました：" + e.Message); }
        finally { scanning = false; }
    }
    void ApplyFilter()
    {
        var selection = selectionOrder.ToHashSet(StringComparer.OrdinalIgnoreCase);
        restoringSelection = true;
        try {
            Photos.Clear();
            foreach (var photo in all.Where(p => p.Name.Contains(Search.Text, StringComparison.OrdinalIgnoreCase)).Take(limit)) { Photos.Add(photo); if (selection.Contains(photo.Path)) Tiles.SelectedItems.Add(photo); }
            selectionOrder.RemoveAll(path => !Photos.Any(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));
        } finally { restoringSelection = false; }
    }
    void SearchChanged(object sender, TextChangedEventArgs e) { if (Tiles is null) return; limit = 180; ApplyFilter(); }
    void LoadMore(object sender, RoutedEventArgs e) { limit += 180; ApplyFilter(); }
    void ShowLibrary(object sender, RoutedEventArgs e) { Library.Visibility = Visibility.Visible; Composer.Visibility = Visibility.Collapsed; }
    void RegisterDefault(object sender, RoutedEventArgs e)
    {
        try { Association.Register(); SetStatus("Windowsの「既定のアプリ」でVRC Photo Postを選んでください。写真の右クリック→プログラムから開くでも利用できます。"); }
        catch (Exception error) { SetStatus("登録できませんでした：" + error.Message); }
    }
    async void ChangeFolder(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "一覧に表示する写真フォルダ" };
        if (picker.ShowDialog(this) != true) return;
        Folder = picker.FolderName; Notify(nameof(Folder));
        try { File.WriteAllText(Path.Combine(App.DataDirectory, "folder.json"), JsonSerializer.Serialize(Folder)); }
        catch (Exception error) { SetStatus("設定を保存できません：" + error.Message); }
        await Scan();
    }
    async void OpenFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "写真|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff" };
        if (dialog.ShowDialog(this) == true) await OpenPhotos(dialog.FileNames);
    }
    async void TileOpened(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(Tiles, source) is not ListBoxItem { DataContext: Photo photo }) return;
        e.Handled = true;
        await OpenPhotos([photo.Path]);
    }
    void PhotosSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (restoringSelection) return;
        foreach (Photo photo in e.RemovedItems) selectionOrder.Remove(photo.Path);
        foreach (Photo photo in e.AddedItems) if (!selectionOrder.Contains(photo.Path)) selectionOrder.Add(photo.Path);
    }
    async void ComposeSelection(object sender, RoutedEventArgs e) => await OpenPhotos(selectionOrder.ToArray());
    async void FilesDropped(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) await OpenPhotos(files.Where(Photo.IsImage).ToArray()); }
    async void FilmstripChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Filmstrip.SelectedItem is not Photo photo) return;
        var generation = ++photoGeneration;
        PhotoLoading.Text = "写真を読み込んでいます…";
        PhotoCaption.Text = photo.Name + "\n" + photo.DateLabel;
        LargePhoto.Source = photo.Thumbnail;
        PhotoLoading.Visibility = LargePhoto.Source is null ? Visibility.Visible : Visibility.Collapsed;
        try {
            var image = await Task.Run(() => Photo.ReadBitmap(photo.Path, 1600));
            if (generation != photoGeneration) return;
            LargePhoto.Source = image; PhotoLoading.Visibility = Visibility.Collapsed;
        } catch (Exception error) {
            if (generation != photoGeneration) return;
            PhotoLoading.Text = "写真を読み込めませんでした"; PhotoLoading.Visibility = Visibility.Visible;
            SetStatus("写真を表示できません：" + error.Message);
        }
    }
    public async Task OpenPhotos(string[] paths)
    {
        if (busy || opening || textBusy) { SetStatus("写真の準備・添付処理中です。完了してから別の写真を開いてください。"); return; }
        if (paths.Length is < 1 or > 4) { SetStatus("写真を1〜4枚選んでください。"); return; }
        opening = true;
        try {
            var photos = paths.Select(Path.GetFullPath).Select(p => new Photo(p)).ToArray();
            Filmstrip.ItemsSource = photos; Filmstrip.SelectedIndex = 0;
            Library.Visibility = Visibility.Collapsed; Composer.Visibility = Visibility.Visible;
            SetStatus("選んだ写真を開きました。Xの投稿画面を準備しています…");
            var names = await Task.Run(() => photos.Select(p => PhotoMetadata.ReadWorldName(p.Path)).ToArray());
            SetMetadata(photos, names);
            TextStatus.Text = draftText.Consumed ? "投稿文は保持します。必要な情報はチップで追加できます。" : "空の新規投稿欄に、一度だけ初期入力します。";
            if (browserReady && XComposer.IsX(Browser.CoreWebView2.Source)) {
                var current = await XComposer.ReadState(Browser.CoreWebView2);
                if (!navigating && !Browser.CoreWebView2.Source.Contains("/compose/") && current.Kind == "waiting" && current.Diagnostics is { } diagnostics && diagnostics.TryGetProperty("editorWrappers", out var editors) && editors.GetInt32() > 0) {
                    draftText.Protect(); pending = photos.Select(p => p.Path).ToArray(); attempted = true;
                    TextStatus.Text = "投稿欄を確認できません。「投稿文をコピー」を使ってください。";
                    SetStatus("右側に投稿欄がありますが、その状態を確認できません。画面・文章・添付を保持しています。"); return;
                }
                if (current.Existing) { draftText.Protect(); TextStatus.Text = "既存の投稿文・下書きを保持しています。"; pending = photos.Select(p => p.Path).ToArray(); attempted = true; SetStatus("Xに既存の下書きがあります。内容を確認・整理してから「新しい下書き」を押してください。左の写真はまだ添付していません。"); return; }
            }
            pending = photos.Select(p => p.Path).ToArray(); attempted = false; queuedAt = DateTime.UtcNow;
            RecordEvidence("photo-opened");
            await InitializeBrowser();
            if (!navigating && (!XComposer.IsX(Browser.CoreWebView2.Source) || !Browser.CoreWebView2.Source.Contains("/compose/"))) Browser.CoreWebView2.Navigate("https://x.com/compose/post");
            bridge.Start(); await Tick();
        } catch (Exception error) { SetStatus("開けませんでした：" + error.Message); }
        finally { opening = false; }
    }
    async Task WarmBrowser()
    {
        try { await InitializeBrowser(); }
        catch { /* Opening a photo reports initialization errors with a retry. */ }
    }
    Task InitializeBrowser() => browserReady ? Task.CompletedTask : browserInitialization ??= InitializeBrowserCore();
    async Task InitializeBrowserCore()
    {
        try {
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(App.DataDirectory, "browser"));
        await Browser.EnsureCoreWebView2Async(env);
        Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
        Browser.CoreWebView2.Settings.AreDevToolsEnabled = App.ProbeDirectory is not null;
        Browser.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https") Browser.CoreWebView2.Navigate(e.Uri); };
        Browser.CoreWebView2.NavigationStarting += (_, e) => {
            if (App.ProbeDirectory is null && Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme is not ("https" or "about")) e.Cancel = true;
            if (!e.Cancel) { navigating = true; navigationGeneration++; }
        };
        Browser.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        Browser.CoreWebView2.NavigationCompleted += async (_, e) => {
            navigating = false;
            if (!e.IsSuccess) SetStatus("X画面の読み込みに失敗しました：" + e.WebErrorStatus + "。新しい下書きから再試行できます。");
            else await Tick();
        };
        Browser.CoreWebView2.WebMessageReceived += async (_, e) => {
            if (!XComposer.IsX(e.Source) || !XComposer.IsX(Browser.CoreWebView2.Source)) return;
            try {
                var message = e.TryGetWebMessageAsString();
                if (message == "vrc-photo-post:composer-edited") draftText.Protect();
                if (message == "vrc-photo-post:composer-changed") await Tick();
            }
            catch (ArgumentException) { }
        };
        if (App.ProbeDirectory is null) await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(XComposer.ReadinessObserverScript);
        browserReady = true;
        if (App.ProbeDirectory is null) Browser.CoreWebView2.Navigate("https://x.com/compose/post");
        } catch { browserInitialization = null; throw; }
    }
    async void NewDraft(object sender, RoutedEventArgs e)
    {
        if (busy || opening || textBusy || pending.Length == 0) return;
        try {
            await InitializeBrowser();
            if (!XComposer.IsX(Browser.CoreWebView2.Source)) { SetStatus("右側のXで投稿欄を開いてから、新しい下書きを作成してください。"); return; }
            var state = await XComposer.ReadState(Browser.CoreWebView2);
            if (state.Kind != "ready") { SetStatus("投稿欄の状態を確認できないため、画面を切り替えません。右側のXを確認してください。"); return; }
            if (state.Existing) { SetStatus("右側のXで下書きを保存または破棄してください。既存の文章・添付画像は自動で消しません。"); return; }
            draftText.BeginNewDraft();
            attempted = false; queuedAt = DateTime.UtcNow; Browser.CoreWebView2.Navigate("https://x.com/compose/post"); bridge.Start();
            SetStatus("Xの投稿画面を準備しています…");
        } catch (Exception error) { SetStatus(error.Message); }
    }
    async Task Tick()
    {
        if (busy || textBusy || attempted || pending.Length == 0 || !browserReady || navigating) return;
        busy = true;
        var navigation = navigationGeneration;
        try {
            if (!XComposer.IsX(Browser.CoreWebView2.Source)) return;
            var state = await XComposer.ReadState(Browser.CoreWebView2);
            if (state.Kind == "login") { queuedAt = DateTime.UtcNow; RecordEvidence("waiting-for-login"); SetStatus("右側のXでログインしてください。ログイン後、選んだ写真の添付を自動で開始します。"); return; }
            if (state.Kind != "ready") { RecordEvidence("waiting-for-composer", diagnostics: state.Diagnostics); if (DateTime.UtcNow - queuedAt > TimeSpan.FromSeconds(90)) { attempted = true; draftText.Protect(); TextStatus.Text = "投稿欄を確認できません。「投稿文をコピー」を使ってください。"; RecordEvidence("composer-unrecognized", diagnostics: state.Diagnostics); SetStatus("Xの投稿画面を認識できません。ログイン状態・通信・画面変更を確認し、「新しい下書き」で再試行してください。"); } return; }
            if (state.Existing) { draftText.Protect(); TextStatus.Text = "既存の投稿文・下書きを保持しています。"; attempted = true; RecordEvidence("existing-draft", state.Previews, state.Diagnostics); SetStatus("Xに既存の下書きがあります。右側で整理してから「新しい下書き」を押してください。"); return; }
            SetStatus("写真を添付する準備をしています…");
            var copies = await XComposer.Snapshot(pending, sessionDirectory);
            // The default must describe the exact stable image bytes being attached.
            var snapshotNames = await Task.Run(() => copies.Select(PhotoMetadata.ReadWorldName).ToArray());
            if (Filmstrip.ItemsSource is Photo[] selected) SetMetadata(selected, snapshotNames);
            attempted = true; // Never retry an ambiguous attachment automatically.
            if (navigating || navigation != navigationGeneration || !XComposer.IsX(Browser.CoreWebView2.Source)) { draftText.Protect(); SetStatus("画面が変更されたため、初期入力と添付を止めました。"); return; }
            state = await XComposer.ReadState(Browser.CoreWebView2);
            if (state.Kind != "ready" || state.Existing) { draftText.Protect(); TextStatus.Text = "投稿欄の状態が変わったため、初期入力は行いません。"; SetStatus("右側の文章・添付を保持して、添付を止めました。"); return; }
            if (draftText.TryConsume()) {
                try {
                    var result = await ComposerText.Insert(Browser.CoreWebView2, generatedText, append: false);
                    TextStatus.Text = result.Kind switch {
                        "inserted" => "投稿文を初期入力しました。右側で自由に編集・削除できます。",
                        "protected" => "入力済みの投稿文を保持しました。初期入力は行いません。",
                        _ => "初期入力を確認できません。必要なら「投稿文をコピー」を使ってください。"
                    };
                    if (result.Kind == "protected") { RecordEvidence("text-protected", diagnostics: result.Diagnostics); SetStatus("入力済みの文章・添付を保護して写真の添付を止めました。右側を確認してください。"); return; }
                } catch { TextStatus.Text = "初期入力を確認できません。「投稿文をコピー」を使ってください。"; }
            }
            if (navigating || navigation != navigationGeneration) { SetStatus("画面が変更されたため、写真の添付を止めました。"); return; }
            RecordEvidence("attaching");
            await XComposer.SetFiles(Browser.CoreWebView2, copies);
            SetStatus("写真を添付しています。投稿ボタンは押さず、画像プレビューを確認します。");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(54);
            while (DateTime.UtcNow < deadline) {
                await Task.Delay(150);
                if (!XComposer.IsX(Browser.CoreWebView2.Source)) break;
                state = await XComposer.ReadState(Browser.CoreWebView2);
                if (state.Kind == "ready" && state.Previews == copies.Length) { RecordEvidence("x-preview-confirmed", state.Previews); SetStatus($"{copies.Length}枚の画像プレビューを確認しました。右側で文章を入力し、内容を確認して投稿してください。"); return; }
            }
            RecordEvidence("preview-unconfirmed", state.Previews, state.Diagnostics); SetStatus("画像プレビューを確認できませんでした。右側で添付状況を確認してください。自動再送はしません。");
        } catch (Exception error) { attempted = true; RecordEvidence("attachment-failed"); SetStatus("自動添付に失敗しました：" + error.Message + "。右側のXを確認してください。"); }
        finally { busy = false; if (attempted) bridge.Stop(); }
    }
    void CopyPostText(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(generatedText); TextStatus.Text = "コピーしました。投稿欄に貼り付けてください"; }
        catch { TextStatus.Text = "コピーできませんでした。生成文を選択してコピーしてください。"; }
    }
    async void AddWorldChip(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string world }) await AppendTextChip(world);
    }
    async void AddHashtagChip(object sender, RoutedEventArgs e) => await AppendTextChip("#VRchat");
    async Task AppendTextChip(string value)
    {
        if (busy || opening || textBusy || navigating || !browserReady) { TextStatus.Text = "写真と投稿欄の準備が終わってから追加してください。"; return; }
        textBusy = true; draftText.Protect();
        try {
            var result = await ComposerText.Insert(Browser.CoreWebView2, value, append: true);
            TextStatus.Text = result.Kind switch { "inserted" => "投稿文の末尾に追加しました。", "duplicate" => "投稿文に含まれているため、追加しませんでした。", _ => "追加を確認できません。右側の投稿欄を確認してください。" };
        } catch { TextStatus.Text = "追加を確認できません。右側の投稿欄を確認してください。"; }
        finally { textBusy = false; }
    }
}
