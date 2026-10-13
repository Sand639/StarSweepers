# 1台のPCで複数のキーボードを使う

| 項目 | 内容 |
| --- | --- |
| 担当者 | 小野田 喬（実装：Claude Code） |
| 作成日 | 2026年10月6日 |
| 最終更新日 | 2026年10月7日 |
| 状態 | 実装中（読み取り部分は検証用プロジェクトでビルドして確認済み。**本番のシーンでは未確認**） |

---

## この機能は何か

1台のPCにキーボードを2台以上つないだとき、**どのキーボードで押されたキーなのかを分けて読む**仕組み。
ふつうはWindowsもUnityも、つないだキーボードを全部まとめて「1台」として扱うので、
2人が別々のキーボードでWASDを押しても区別できない。これを区別できるようにする。

小野田さんが作っている「マウスを2台別々に動かす仕組み（`MultiMouse`）」のキーボード版。
作り方（Windowsから直接デバイスごとの入力をもらう）も同じにしてある。

主な使い道は**移動の判定**（キーボードごとのWASD・矢印キー）。

---

## 遊ぶ人から見た動き

| 操作 | 結果 |
| --- | --- |
| 1台目のキーボードでどれかキーを押す | そのキーボードが **0番** になる |
| 別のキーボードでどれかキーを押す | そのキーボードが **1番** になる |
| 0番のキーボードで WASD／矢印 | 0番の入力として読める（1番には影響しない） |
| （確認用HUDで）F5 | 番号の割り当てをやり直す |
| ゲームの画面以外（Discordなど）で文字を打つ | 無視される（設定で切り替え可） |

---

## 関係するファイル・シーン

| 種類 | パス |
| --- | --- |
| スクリプト | `Assets/Core/Input/MultiKeyboard/RawInputKeyboard.cs`（Windowsからキーボードごとの入力を受け取る） |
| スクリプト | `Assets/Core/Input/MultiKeyboard/RawKeyMap.cs`（キーの番号を Unity の `Key` に直す表） |
| スクリプト | `Assets/Core/Input/MultiKeyboard/MultiKeyboardDevice.cs`（キーボード1台分の状態） |
| スクリプト | `Assets/Core/Input/MultiKeyboard/MultiKeyboardManager.cs`（シーンに置く管理役） |
| スクリプト | `Assets/Core/Input/MultiKeyboard/MultiKeyboardTestHud.cs`（確認用の画面表示） |
| スクリプト | `Assets/Core/Input/MultiKeyboard/UnityKeyboardBridge.cs`（受け取ったキーを Unity の `Keyboard.current` へ流し直す） |
| スクリプト | `Assets/Core/Input/MultiMouse/UnityMouseBridge.cs`（マウス版。受け取った移動量を `Mouse.current.delta` へ流し直す） |
| シーン | まだ無い。空の GameObject に `MultiKeyboardManager` と `MultiKeyboardTestHud` を付けて再生すれば確認できる |

### 使い方（プログラマー向け）

```csharp
using ProjectEL4S.MultiKeyboard;
using UnityEngine.InputSystem;

var kb = MultiKeyboardManager.Instance.GetKeyboard(0);   // 0番のキーボード（未割り当てでも null ではない）
if (kb.IsAssigned)
{
    Vector2 move = kb.ReadMove(MultiKeyboardMoveKeys.Wasd);   // -1..1。上・右が正。斜めは長さ1
    if (kb.WasPressedThisFrame(Key.Space)) { /* ジャンプ */ }
    if (kb.IsPressed(Key.LeftShift)) { /* ダッシュ */ }
}
```

`Keyboard.current` とほぼ同じ感覚で、`IsPressed` / `WasPressedThisFrame` / `WasReleasedThisFrame` に `Key` を渡す。

---

## 設定できる値（インスペクターの項目）

`MultiKeyboardManager`

| 項目名 | 意味 | 初期値 |
| --- | --- | --- |
| Max Keyboards | 何台まで分けて扱うか。超えた分のキーボードは無視 | 2 |
| Keyboard Colors | 番号ごとの色（確認用HUDの表示に使う） | 水色・橙・緑・黄 |
| Ignore While Unfocused | ゲームの画面が選ばれていない間の入力を無視する | ON |
| Forward To Unity Input | 受け取ったキーを Unity の `Keyboard.current` へ流し直す。切ると、管理役が動いている間は `Keyboard.current` と Input Actions のキーボード操作が効かなくなる | ON |
| Fallback To System Keyboard | 分けて読めない環境では、0番をふつうのキーボードとして動かす | ON |

`MultiMouseManager` にも同じ意味の `Ignore While Unfocused`（ON）と `Forward To Unity Input`（ON。こちらはマウスの移動量 `Mouse.current.delta` を流し直す）を足した。

`MultiKeyboardTestHud`

| 項目名 | 意味 | 初期値 |
| --- | --- | --- |
| Box Size | 動かす四角の大きさ | 60 |
| Move Speed | 四角の速さ（ピクセル/秒） | 400 |
| Move Keys | 移動に使うキー（WASD／矢印／両方） | 両方 |

---

## 仕組み（分かる人向け）

- `RawInputKeyboard` が専用スレッドに見えないウィンドウを作り、Windows の Raw Input（`WM_INPUT`）でキーボードを登録して、
  届いた「どのデバイスの・どのキーが・押された／離された」を順番に溜める。作りは `RawInputMouse` と同じで、
  エディタが落ちないための注意（クラス名を毎回変える／再コンパイル前に必ず止める／例外をWindows側へ漏らさない）もそのまま守っている。
- マウスと違い、1フレームの中で「押して離す」が起きるので、合計ではなく**イベントの列**として溜める。
- キーは**スキャンコード（キーの物理的な位置）**から Unity の `Key` に直している。`Key` はUS配列での位置で決まっているので、
  日本語配列のキーボードでも W/A/S/D の位置はずれない。
- `MultiKeyboardManager` は毎フレーム溜まったイベントを取り出し、**最初にキーを押した順**にデバイスを 0番・1番…に割り当てる。
  `SetFixedDevices` で番号ごとの機器（デバイスの名前）を渡してあれば、押した順に関係なくその番号になる（固定登録）。今つながっている登録済みの機器は、押す前から割り当てる。登録の画面と保存は `席ごとの入力と機器の固定登録.md` を参照。
  離したイベントでは割り当てない（押しっぱなしで割り当てられるのを防ぐため）。
- ゲームの画面が選ばれていない間は、届いたイベントを捨て、押されていたキーを全部離す。
- **見張り役**：`RawInputKeyboard` のスレッドが0.2秒ごとに「キーボードの登録がまだこちらにあるか」を確かめ、取られていたら取り戻す。
  エディタでは、Game ビューにフォーカスが戻るたびに Unity が登録を取り返すため（下の「Unity との取り合い」）。
  取り戻したときは、取られていた間の「離した」が届いていないので、管理役が全部のキーを離した扱いにする。
- 止めるときは、登録を消すのではなく、**元の持ち主（Unity）へ返す**。
- **橋渡し**：こちらが登録している間は Unity のキーと移動量が止まる（下の表）ので、受け取った入力を Unity へ流し直す。
  - 全部のキーボードを合わせて「どのキーが押されているか」を `Keyboard.current` へ書き込む（2台で同じキーを押して片方だけ離しても、押したまま）。マウスは全部の移動量を足して `Mouse.current.delta` へ書き込む。
  - 書き込むのは、Input System が入力を処理する直前（`InputSystem.onBeforeUpdate`）。だから**同じフレームのうちに反映される**。管理役の処理もここで行うので、管理役が分かるフレームと `Keyboard.current` が分かるフレームは同じになる。
  - 止まらない入力（クリック・カーソル位置・ホイール・文字入力・IMGUI）は流さない。流すと二重に届く。
  - Unity 側の状態とずれていたら書き直す。エディタで Unity に登録を取られていた一瞬に、Unity 側だけキーが押されたまま残ることがあるため。
  - 管理役を消すときは、全部のキーを離した状態を書き込んでから止める。

### Unity との取り合い（2026/10/6 に検証）

Windows では、1つのプログラムが「キーボード（マウス）の生の入力」を受け取れる場所は1か所だけ。
検証用の Unity プロジェクト（6000.3.14f1）で、キー入力とマウス移動を送って次のことを確かめた。

| 場面 | 結果 |
| --- | --- |
| 起動直後 | Unity がキーボードとマウスの両方を自分のウィンドウに登録している |
| こちらが登録した後 | **Unity の `Keyboard.current` のキーと `Mouse.current.delta` は反応しなくなる**（エディタ・ビルドとも）。クリック・カーソル位置・ホイール・文字入力・IMGUI は動き続ける |
| 橋渡しを入れた後 | `Keyboard.current` のキーと `Mouse.current.delta` が、管理役なしのときと同じように動く（同じフレームで反映。文字入力・IMGUI・クリックは二重にならない）。フォーカスを外して戻した後も同じ |
| エディタで Scene ビューやインスペクターを触ってから Game ビューに戻る | Unity が登録を取り返す → こちらに届かなくなる（**見張り役で取り戻すようにした**） |
| ビルドで別のウィンドウ・別のアプリへ切り替えて戻る | Unity は取り返さなかった |
| こちらを止めた後 | 登録が Unity に戻り、`Keyboard.current` / `Mouse.current` がまた効く |

---

## できていないこと・既知の問題

- **本番のシーン（StarSweepers）ではまだ動かしていない。** 読み取り部分は検証用プロジェクトでコンパイル・ビルドして確かめた（上の表）。管理役と確認用HUDは、実際に再生して確かめること。
- 管理役が動いている間に Unity のキーと移動量が止まる問題は、**橋渡しで直した**（上の表）。ただし本番のシーン（ポーズ画面・プレイヤー操作など）と組み合わせての確認はまだ。→ `リスクリスト.md`
- 橋渡しで流すのは全部のキーボードを合わせた入力なので、`Keyboard.current` で読むと「どのキーボードか」は分からない（ふつうのキーボードと同じ）。台ごとに分けたいときは `MultiKeyboardManager` から読むこと。
- 1台のキーボードが Windows から**複数のデバイスに見える**ことがある（ゲーミングキーボードの音量ボタンなど）。その場合、同じキーボードが2つの番号を取ることがある。
- ソフトウェアキーボードや、リモートデスクトップ越しの入力は、デバイスが「不明（0）」として届く。
- 変換・無変換・かな・￥・ろ のキーは Unity の `Key` に無いので読めない。
- DualDeviceInput（左右2本の入力）とはまだつないでいない（まず単体で作る、という小野田さんの判断）。
- キーボード＋マウスとコントローラを同じ書き方で読みたいときは、`InputSeatManager`（`席ごとの入力と機器の固定登録.md`）を使う。
- `RawInputKeyboard.cs` は `RawInputMouse.cs` とWindowsとのやり取りの部分がほぼ同じ。直すときは両方直すこと。将来1つにまとめてもよい。

---

## オンライン（LAN）で使うときの注意

いまの通信（`NetworkPlayer` / `FishingNetPlayer` など）は、**「1台のPC＝1人」で、動かすのは本人のPC**（`NetworkTransform` で位置を配る）という作り。

- **入力そのものは送らなくてよい。** キーボードやマウスを読めるのはつないでいるPCだけなので、そのPCがキャラクターを動かし、位置を配る形がいまの作りに合う。
- **1台のPCで2人を動かすと「1台のPC＝1人」の前提が崩れる。** チーム・席・色・出てくる場所・答え合わせが、通信の接続番号（`OwnerClientId`）を「人」の代わりに使っている（`SpaceJunkSession` / `SpaceJunkPlayerSetup` / `SpaceJunkLocalStateCheck` / `FishingNetPlayer` など）。
  1台に2人いると同じ番号になるので、「接続番号＋そのPCの中の何人目か」で人を区別するように直す必要がある。
- 2人目のキャラクターは自動では出てこない（Netcode が自動で出すのは1接続につき1人）。ホストが `SpawnWithOwnership` で出す仕組みが要る。
- 仮想カーソルの位置は画面の座標なので、PCごとに解像度が違うとずれる。送るなら、ゲームの世界の座標に直してから送る。
- 「押した瞬間」の操作（つかむ・投げるなど）をホストに頼むときは、毎フレームの状態ではなく1回ずつ知らせる（取りこぼさないように）。
- 1台のPCに2人いると、そのPCの画面に2人分のカメラ（画面分割など）が要る。

---

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/10/6 | Claude Code | 新規作成（小野田さんの依頼） |
| 2026/10/6 | Claude Code | Unity との取り合いを検証した結果、見張り役、止めるときに Unity へ返す処理、オンラインで使うときの注意を追加 |
| 2026/10/6 | Claude Code | Unity の入力へ流し直す橋渡しを追加（小野田さんの依頼）。止まるのはキーと移動量の2つだけだった検証結果を反映 |
| 2026/10/7 | Claude Code | 固定登録（`SetFixedDevices`）を追加したことと、席ごとの入力（`席ごとの入力と機器の固定登録.md`）への案内を追記 |
