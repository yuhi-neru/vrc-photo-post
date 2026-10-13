# 類似アプリ調査

調査日：2026-10-13（日本時間）。販売者の商品説明、公開ストア、開発者リポジトリを調査。購入・インストール・実動作確認は実施していない。価格・提供状態は公開ページの表記であり変更される可能性がある。

## 比較

| 製品 | 公開ページの機能・方式 | 今回の構想との差 |
|---|---|---|
| [VRC SnapHub](https://booth.pm/ja/items/7945303) | Windows写真管理。写真自動検出、OCR、QRコード解析。Full版960円、Lite版320円。Full版のX投稿補助は画像をクリップボードへコピーしてX画面を開く方式で、利用者が貼り付ける | PC写真ビューア＋X投稿補助として近いが、手動貼り付けが残る。アプリ内X画面・自動添付は確認できない |
| [VRC-Photos](https://booth.pm/ja/items/8140506) | 無料Windows写真管理。お気に入り、ワールド情報、Xへの直接投稿。販売者はX APIのFreeプラン利用と1日の投稿数制限を説明 | 写真閲覧とX投稿は重なるが、今回のAPIを使わないWeb画面方式とは異なる。Freeプランの現行条件や実際の稼働は独立検証していない |
| [UnaPhoto](https://booth.pm/ja/items/4556372) | VRChatワールド内で写真閲覧・文章付きツイートを行う外部クライアント＋ワールドアセット＋サービス。販売者管理クラウドへ直近写真を動画化して送信。商品ページにTwitter API有償化によるサービス停止を記載 | VRChat内の操作という体験は近い。独自クラウドを使わない・X APIを使わないという今回の条件と異なる。現行の利用候補として扱わない |
| [ぶいちゃフォトビュー](https://booth.pm/ja/items/8183859) | 800円。SteamVRオーバーレイでVRChat写真を閲覧。左グリップ2回でパネル展開、右トリガーで写真選択。PCVR向け | VR内の写真パネルとして近い。公開説明からはX投稿機能を確認できない。EACや公認についての販売者の説明を独立確認したものではない |
| [VRC Photo Card Maker](https://booth.pm/ja/items/8711213) | 無料Lite、Pro500円、開発支援版700円。ブラウザでローカル写真から投稿用の画像レイアウトと投稿文を生成。XMP情報・クレジット整形など | 投稿用素材の制作に近い。Xの投稿画面への画像自動添付を明記した製品ではない |
| [VRChatALT](https://chromewebstore.google.com/detail/vrchatalt/onndeoieedgmdhgjdakabdakcnmlklek) | XにVRChat写真を投稿する際に、ワールド情報を画像のALTへ追加するChrome拡張。自動リプライの設定も説明あり | 写真のワールド情報を投稿へ追加する将来機能と重なる。写真を既定ビューアで開く機能や同一ウィンドウへの自動添付は確認できない。通信方式は調査していない |

## 周辺製品

- [PICPRE for VRC](https://booth.pm/ja/items/3316228)：撮影写真をVRで確認するツール。Twitterへの直接投稿は公開説明で不確定な将来候補として記載されているため、実装済み機能として比較しない。
- [VRCPhotoAlbum](https://github.com/gatosyocora/VRCPhotoAlbum)：Windows向けの写真閲覧・日時やメタ情報による検索。2023-08-06にアーカイブされた公開リポジトリ。写真中ユーザーのTwitterリンク表示は投稿機能とは別。
- [WorldShot Log](https://github.com/noma-nomoa/vrchat-world-photo-manager)：Windows向けの写真整理、ワールド情報、画像編集など。閲覧したREADMEからは今回のX自動添付方式を確認できない。
- [VRCToolBox](https://booth.pm/ja/items/4340204)：写真管理等のツール。更新履歴に2023-04-12のTwitter投稿機能削除を記載。
- [VRCPost](https://vrcpost.com/)：[Kipcha](https://apps.apple.com/jp/app/%E3%82%AD%E3%83%97%E3%83%81%E3%83%A3/id6740402544)と同様、VRChat写真の共有という目的で隣接するが、今回のローカルWindowsビューア＋X投稿画面一体型とは異なる。

## 調査からの判断

「VRChat写真を見る」「Xへ投稿する」という機能だけでは既存製品と重なる。今回の構想を次の組み合わせに絞ることで、公開説明上は違いを出せる可能性がある。

1. エクスプローラーで画像を開く入口。Windowsの既定の画像ビューアとして使える。
2. 左に写真、右にXの実際のWeb投稿画面を常時表示。
3. 開いた写真を自動で添付し、貼り付け・別ブラウザへの切り替え・「Xへ送る」操作を省く。
4. 利用者は文章を入力し、Xの投稿ボタンを本人が押す。
5. Xの開発者APIや独自の写真クラウドを使わない。

これらすべてを備える製品は今回の検索では確認できなかった。ただし市場全体に存在しないという断定ではない。既存製品の実操作やソース解析は行っておらず、説明にない機能がある可能性は残る。

WebView2を採用しても、Xでのログイン、ローカル画像の自動添付、画像プレビューまでの反映が動作することは別途検証が必要。既存製品の存在は、この方式の成功を保証しない。

## 開発状態

利用者の「調査を先に」の指示により再構築は中断した。旧ソースと文書の削除は実施済みだが、旧ビルド済みアプリの一部はファイルロック／アクセス拒否で残っている。新実装はまだ作成していない。旧実行ファイルを新しいWebView2版として扱わない。

## VRChat以外の一般向けツール

同日追加調査。公式製品資料・開発者資料を基準とし、実動作は試していない。

| ツール | 類似点 | 現行性と今回との差 |
|---|---|---|
| [ShareX](https://getsharex.com/docs/image-viewer) | Windowsの画像閲覧、スクリーンショット、画像編集、アップロードの連続したワークフロー | [公式変更履歴](https://getsharex.com/changelog.html)にTwitter投稿先の削除を記載。現在のX投稿ツールとして紹介しない。画像を開くと同一ウィンドウのXへ添付する方式は確認できない |
| [Screenpresso](https://en.screenpresso.com/features/) | 撮影・画像編集・共有をまとめたWindowsツール | [2.2.8の公式リリースノート](https://www.screenpresso.com/releases/screenpresso-2-2-8/)にTwitter連携削除を記載。古いマニュアルにはTwitterが載るため、マニュアルだけで現行対応と判断しない |
| [TweetDuck](https://tweetduck.chylex.com/) | TweetDeckのWeb画面をWindowsアプリ化。画像のクリップボード添付などブラウザとデスクトップの連携 | 公式サイトに保守終了を記載。[開発者リポジトリ](https://github.com/chylex/TweetDuck)はWinForms＋CefSharpによる実装を説明。ブラウザ埋め込み方式の歴史的な参考であり、現行の動作保証や今回の写真ビューアの完成例ではない |
| [Buffer](https://support.buffer.com/articles/using-xtwitter-with-buffer-nA84XnWtuU) | 写真を添付し文章を作成、Xへ投稿・予約投稿 | [公式添付ガイド](https://support.buffer.com/en-us/articles/attaching-images-videos-and-other-media-to-your-posts-eudySt0TnS)でPC画像のアップロードを説明。独自サービスの投稿編集画面から投稿する構成で、ローカル写真ビューアにX.comの画面を埋め込む方式とは異なる |
| [Typefully](https://typefully.com/changelog/reorder-media-in-drafts-132) | 画像付きのX等の投稿・下書き作成 | [公式API資料](https://typefully.com/docs/api)でサービスのS3領域へのメディアアップロードを説明。写真の既定アプリとして使い、X.comのWeb画面へローカルで受け渡す構成とは異なる |

一般向けにも画像とSNS投稿を組み合わせた製品は存在する。写真ビューア＋X.comの実画面＋開いた画像の自動添付＋Windowsの既定アプリという一式は、この追加調査でも確認できなかった。不存在の証明ではない。
