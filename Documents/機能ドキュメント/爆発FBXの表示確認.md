# 爆発FBXの表示確認

| 項目 | 内容 |
| --- | --- |
| 担当者 | 長島 颯士（実装補助：Codex） |
| 作成日 | 2026年10月8日 |
| 最終更新日 | 2026年10月8日 |
| 状態 | 共通プレビュー実装済み（Unityでの再生確認待ち） |

---

## この機能は何か

エフェクトのPrefabやFBXをテスト用シーンで表示して、見た目と再生タイミングを確認するための共通機能。現在の `fbxTest` は爆発FBXの例。

## 遊ぶ人から見た動き

| 操作 | 結果 |
| --- | --- |
| シーンを再生 | Play On Start がONなら設定したエフェクトを再生する |
| 画面左上の「Play / Replay」 | エフェクトをもう一度再生する |

プレビュー用オブジェクトを削除した場合は、Unityのメニュー `Tools > StarSweepers > Tests > Configure Explosion FBX Test Scene` を実行すると作り直せる。

## 関係するファイル・シーン

| 種類 | パス |
| --- | --- |
| 共通プレビュー | Assets/Scripts/Effect/EffectPreviewController.cs |
| 爆発FBX用シーン設定 | Assets/Scripts/Effect/ExplosionEffectTestSceneBuilder.cs |
| 確認用シーン | Assets/Scenes/Test/fbxTest.unity（長島さん作成） |
| 爆発FBX（例） | Assets/Art/fbx/Explosion.fbx |
| 再生コントローラー（例） | Assets/Art/fbx/ExplosionPreview.controller |

## 別のエフェクトを試す手順

1. Unityで `fbxTest` を複製し、`Assets/Scenes/Test/` に別名で保存する。
2. 複製したシーンで `ExplosionEffect` を削除する。
3. Hierarchyに空のGameObjectを作り、`EffectPreviewController` を追加する。
4. エフェクトPrefabを `Effect Prefab` に設定する。シーン上のオブジェクトを直接試す場合は `Target Effect` に設定する。
5. Animatorが独自のControllerを必要とする場合だけ `Animator Controller` に設定する。Particle SystemだけのPrefabなら空欄でよい。
6. 必要に応じて `Start Delay`、`Playback Speed`、`Visible Duration` を調整してPlayする。

Animator / Legacy Animation / Particle System を再生する。表示時間が0の場合、アニメーションまたはループしないParticle Systemの長さから自動で決める。ループするParticle Systemは自動では消さないため、消したい場合は `Visible Duration` を設定する。

## 設定できる値（インスペクターの項目）

| 項目名 | 意味 | 実装したときの初期値（目安。調整で変わる） |
| --- | --- | --- |
| Start Delay | 再生を始めるまでの待ち時間 | 0秒 |
| Playback Speed | アニメーションの再生速度 | 1倍 |
| Visible Duration | 再生後に表示を消すまでの時間。0ならアニメーションの長さを使う | 0秒 |
| Play On Start | シーンを再生したときに自動で再生するか | ON |

## 仕組み（分かる人向け）

プレビューはPrefabまたはシーン上のオブジェクトを受け取り、アニメーションとParticle Systemを再生する。爆発FBXの `fbxTest` はシーンを開いたときにFBXをGeneric形式で読み込み、含まれているアニメーションクリップをControllerへ接続する。

## できていないこと・既知の問題

- Unity Editor上でのコンパイル・再生確認は未実施。fbxTestを開くと設定を自動接続するEditorスクリプトを追加したため、次にUnityでスクリプトの読み込みが終わると再生設定と調整用項目がシーンに保存される想定。
- FBXの入手元と利用条件は未確認。チーム内での確認用途に限り、展示・配布に使う前に出典とライセンスを確認する。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/10/8 | 長島 颯士（実装補助：Codex） | 爆発FBXの表示・時間調整を確認する機能ドキュメントを作成 |
| 2026/10/8 | 長島 颯士（実装補助：Codex） | 長島さん作成のfbxTestシーンを確認用シーンとして記載 |
| 2026/10/8 | 長島 颯士（実装補助：Codex） | fbxTestを開いた際の再生設定自動接続と、再生カメラの調整を追加 |
| 2026/10/8 | 長島 颯士（実装補助：Codex） | AnimatorとParticle Systemを使う他のエフェクトにも使える共通プレビューに拡張 |
| 2026/10/8 | 長島 颯士（実装補助：Codex） | Unity上でのFBXの移動先に合わせ、シーン設定ツールが配置済みFBXの場所を見つけるよう変更 |
| 2026/10/8 | 長島 颯士（実装補助：Codex） | 共通プレビュー用スクリプトを `EffectPreviewController.cs` に改名 |
| 2026/10/8 | 長島 颯士（実装補助：Codex） | プレビュー用オブジェクトを削除した場合、Unityメニューで同じオブジェクトを復元できるように修正 |

