# Power Changer

Windows の電源プランをボタン1つで切り替える、シンプルなアプリです。

<!-- スクリーンショットを載せる場合は、画像をリポジトリに入れて次の行の先頭の「<!--」と末尾を消してください -->
<!-- ![スクリーンショット](screenshot.png) -->

## 主な機能

- この PC に入っている電源プランを自動で読み込み、ボタンとして表示
- 一覧に出てこない Windows 標準プラン（省電力・高パフォーマンス・究極のパフォーマンス）にも直接切り替え可能
  - 最近のノートパソコン（モダンスタンバイ対応機）では、Windows の電源オプションに「バランス」系しか表示されないことがあります。本アプリではそうした PC でも標準プランを使えます
- 負荷の大きいプランは薄赤色で表示し、ノートパソコンでは切り替え前に確認画面を表示

## ダウンロード

[Releases](../../releases) から、お使いの PC に合った zip をダウンロードしてください。

| ファイル | 対象 |
|---|---|
| `PowerChanger-win-x64.zip` | 一般的な Windows PC（Intel / AMD） |
| `PowerChanger-win-arm64.zip` | ARM 版 Windows（Snapdragon など） |

zip を展開して `PowerChangerGUI.exe` を実行します。
初回起動時に「Windows によって PC が保護されました」と表示された場合は、「詳細情報」→「実行」を押してください（署名のない個人製アプリのため表示されます）。

## 動作環境

- Windows 10 / 11（x64・ARM64）

## ノートパソコンでのご使用について

ノートパソコンでは、基本的に「バランス」または「省電力」での運用をおすすめします。
「高パフォーマンス」「究極のパフォーマンス」で長時間動かすと、熱による部品の故障やバッテリーの大幅な消耗につながるおそれがあります。
特に「究極のパフォーマンス」は、ノートパソコンでは使用しないでください。

## ソースからビルドする

[.NET 10 SDK](https://dotnet.microsoft.com/download) が必要です。

```
git clone https://github.com/<Ja8101ts>/PowerChanger.git
cd PowerChanger
dotnet run
```

配布用の exe を作る場合（`win-arm64` の部分を `win-x64` に変えると x64 版になります）:

```
dotnet publish -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 免責事項

本アプリの使用によって生じたいかなる故障・損害についても、作成者は責任を負いかねます。

## ライセンス

[MIT License](LICENSE)

---

Released by South_of_Tsu ／ Made in Japan
