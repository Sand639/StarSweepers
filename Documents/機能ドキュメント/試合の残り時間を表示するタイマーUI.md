# 試合の残り時間を表示するタイマーUI

| 項目 | 内容 |
| --- | --- |
| 担当者 | 鈴木 翔太郎（実装補助：Codex） |
| 作成日 | 2026年10月8日 |
| 最終更新日 | 2026年10月8日 |
| 状態 | 実装済み・未確認 |

---

## この機能は何か

試合の残り時間を、画面左上に円形のゲージと「分:秒」の文字で表示する。残り時間が減ると、ゲージの色が青緑、黄・オレンジ、赤・茶の順に変わる。

## 遊ぶ人から見た動き

| 操作 | 結果 |
| --- | --- |
| シーンを再生する | 2分からカウントダウンが始まり、ゲージと文字が残り時間に合わせて更新される |

## 関係するファイル・シーン

| 種類 | パス |
| --- | --- |
| スクリプト | `Assets/Scripts/UI/GameTimerUI.cs` |
| シーン作成ツール | `Assets/Scripts/UI/Editor/GameTimerUISetup.cs` |
| 検証シーン | `Assets/Scenes/Test/GameTimerUITest.unity` |
| プレハブ | `Assets/Resources/GameTimerUI.prefab` |
| 画像 | `Assets/Scenes/Test/shotaro/UI_Timer_Back.png`・`UI_Timer_IMG.png` |

## 設定できる値（インスペクターの項目）

| 項目名 | 意味 | 実装したときの初期値（目安。調整で変わる） |
| --- | --- | --- |
| Max Time Seconds | タイマーの最大時間 | 120秒 |
| Run On Start | 再生時に自動で始めるか | オン |
| Middle Phase Threshold / Final Phase Threshold | 色が変わる残り時間の割合 | 0.66 / 0.33 |
| Initial / Middle / Final Phase Color | 3段階の色 | 青緑 / 黄・オレンジ / 赤・茶 |
| Backdrop Image / Gauge Image / Time Text | 背景・ゲージ・文字の表示部品 | プレハブで割り当て済み |
| On Phase Changed | 色段階の変更時に呼ぶ Unity イベント。値は初期=0、中盤=1、終盤=2 | 未接続 |

## 仕組み（分かる人向け）

`SetRemainingTime` が残り時間と割合を更新し、段階が変わった瞬間に `Action<int>` と `UnityEvent<int>` の両方を呼ぶ。`UpdateVisuals(float progress)` はゲージ、文字、シェーダー用の `_Progress` と `_TimerColor` をまとめて更新する。ゲージは Radial 360、Top 起点、時計回り。

## できていないこと・既知の問題

- Unity上の再生確認はまだ行っていない。
- 仮の2分カウントダウンであり、試合設定・通信との接続はしていない。
- 3枚の画像素材の制作者と利用条件は未確認。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/10/8 | 鈴木 翔太郎（実装補助：Codex） | 2分の仮タイマー、3段階の色、段階変更イベント、検証シーンとプレハブを追加 |
