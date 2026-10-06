# Steamのフレンドのサーバー

| 項目 | 内容 |
| --- | --- |
| 担当者 | Claude Code（依頼：大槻 海斗） |
| 作成日 | 2026年10月6日（火） |
| 最終更新日 | 2026年10月6日（火） |
| 状態 | 実装済み・**未確認**（コンパイルも、Steam アカウント2つでの確認もまだ） |
| 参考 | スプラトゥーンのフレンドから入る部屋 |

---

## この機能は何か

**Steam のフレンドが入っている部屋を、タイトル画面に並べて、そこから入れる機能。** 参加コードを打たなくてよい。

通信そのものは今までどおり Unity の部屋（インターネット）を使う。**Steam は「フレンドの一覧」と「フレンドがどの部屋にいるか」を知るためだけ**に使う。

---

## 遊ぶ人から見た動き

| 操作 | 結果 |
| --- | --- |
| タイトル → サーバーを探す → **フレンドのサーバーを探す** | このゲームで部屋に入っている Steam のフレンドが並ぶ（「○○ の部屋」・部屋の名前・人数）。「参加する」で入る |
| 「フレンドのサーバーを更新」 | 探し直す |
| Steam のフレンド一覧（Shift+Tab の画面）で、フレンドの「ゲームに参加」 | タイトル画面にいれば、その部屋に入る（ゲームを起動していなければ、起動して入る） |
| パスワード付きの部屋 | 一覧に「（パスワードあり）」と出る。左の欄にパスワードを入れてから「参加する」 |

- **Steam を起動していないときは**「Steam を起動していないので、フレンドのサーバーは出せません」と出る。**ほかの機能は今までどおり動く**（展示会場の LAN も同じ）
- 部屋に入っている間、Steam にはフレンド向けに参加コードなどを出している（リッチプレゼンス）。部屋から出ると消える
- LAN の部屋は出ない（インターネットの部屋だけ）

---

## App ID について（大事）

**まだこのゲーム専用の App ID が無いので、Valve のテスト用の 480（Spacewar）で動かしている。**

- `StarSweepers/steam_appid.txt` に `480` と書いてある（Unity のエディタ用）。検証用のビルドを作ると、exe の隣にもコピーされる
- このため、Steam のフレンド一覧には「**Spacewar をプレイ中**」と出る。フレンドが見えるのも「同じ 480 で遊んでいる人」だけ
- **Steam で配ることが決まって App ID をもらったら**、`steam_appid.txt` の数字を変えて Unity を開き直す。製品版では `steam_appid.txt` は配らない（Steam から起動すれば要らない）

---

## 確かめ方

Steam のアカウントが2つ（フレンド同士）と、PC が2台あると確実。

1. 両方の PC で Steam を起動してから、ゲーム（ビルド）を起動する
2. 1台目で、インターネットの部屋を作る
3. 2台目で「サーバーを探す → フレンドのサーバーを探す」。1台目の人の部屋が出れば成功

---

## 関係するファイル

| 種類 | パス |
| --- | --- |
| スクリプト | `Assets/Scripts/Network/SteamFriendsService.cs`（Steam につなぐ・リッチプレゼンス・フレンドの部屋を探す。新規。シーンに置かなくても動く） |
| 変更 | `Assets/Scripts/Network/InternetConnection.cs`（部屋の名前・人数を外から読めるようにした） |
| 変更 | `Assets/Scripts/UI/TitleScreen.cs`・`TitleMenuButton.cs`・`TitlePart.cs`・`Editor/TitleScreenPrefabBuilder.cs`（「フレンドのサーバーを探す」の画面。プレハブは作り直さずに足す＝版 4） |
| 変更 | `Assets/Scripts/SpaceJunk/Editor/SpaceJunkBuilder.cs`（ビルドに `steam_appid.txt` を入れる） |
| 部品 | `Packages/manifest.json` に Steamworks.NET（2025.164.1）を追加（`使用素材とライセンス.md`） |
| 設定 | `StarSweepers/steam_appid.txt`（480） |

---

## 仕組み（分かる人向け）

- `SteamFriendsService`：`RuntimeInitializeOnLoadMethod` で自動で1つ作る。`SteamAPI.Init()` に失敗したら（Steam が起動していない など）`IsAvailable = false` で何もしない
- 部屋に入っている間、2秒ごとにリッチプレゼンスを更新：`ss_code`（参加コード）・`ss_room`・`ss_players`・`ss_password`・`connect`（`+join 参加コード`）
- 一覧：`SteamFriends.GetFriendCount(Immediate)` から、`GetFriendGamePlayed` が同じ App ID の人を選び、`RequestFriendRichPresence` で取り寄せて 1 秒待ってから `GetFriendRichPresence` で読む
- 「ゲームに参加」：`GameRichPresenceJoinRequested_t` と、起動時のコマンドライン `+join コード` を受け取り、タイトル画面が `InternetConnection.JoinGame` で入る
- Steamworks.NET が使えない環境（Windows/Mac/Linux 以外・DISABLESTEAMWORKS）では、中身の無い版がコンパイルされる

---

## できていないこと・既知の問題

- 2アカウントでの確認はまだ
- フレンド一覧の「Spacewar をプレイ中」の表示は、App ID をもらうまで変えられない
- 「ゲームに参加」は、ロビーや試合の途中で押されたときは、タイトルに戻ったときに入ろうとする（そのときまで覚えている）

---

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/10/6 | Claude Code | 新規作成（大槻さんの依頼。Steam のフレンドの部屋を出して、そこから入れるようにした） |
