using UnityEngine;

/// <summary>
/// コントローラーの操作方法。数字は保存（<c>PlayerPrefs</c>）に使うので、変えない。
/// </summary>
public enum ControllerOperationType
{
    /// <summary>今までどおり。左スティックで移動、右スティックで向き。引っ掛けたあとはゲージで投げる。</summary>
    TypeA = 0,

    /// <summary>
    /// 左スティックの方向へ移動して向く（LT を押している間は向きだけ）。右スティックは使わない。
    /// 引っ掛けたあとはゲージを出さず、物理で引き寄せる（<see cref="ThrowController"/>。重い物と ZRワンボタンのときは除く）。
    /// 移動・向きの切り替えは**コントローラーがつながっているときだけ**効く（FishingPlayerController・PlayerAimController）。
    /// </summary>
    TypeB = 1
}

/// <summary>釣りフックの狙いの見せ方（レティクル）。</summary>
public enum AimReticleType
{
    /// <summary>今までどおり。地面の照準マークだけ。</summary>
    TypeA = 0,

    /// <summary>溜めている間、足元から狙う方向へ地面に長方形を伸ばす（<see cref="HookChargeStrip"/>。2026/10/13）。</summary>
    TypeB = 1
}

/// <summary>
/// **フックの投げ方**（宇宙ごみ式＝ThrowController の Style が TwoButtons のときだけ効く。2026/10/15）。
/// </summary>
public enum ThrowOperationType
{
    /// <summary>今までどおり。左（LT）で撃つと引っ張る・右（RT）で撃つと投げる。</summary>
    TwoButtons = 0,

    /// <summary>
    /// **ZR（RT）だけで遊ぶ。** 撃って引っ掛けると前へ投げる動きに入り、途中のスキルチェックで押すと後ろへ引っ張る。
    /// 押さなければそのまま前へ投げる。
    /// </summary>
    OneButton = 1
}

/// <summary>
/// **設定画面で変えられる値**を、どこからでも見られるようにしたもの。
///
/// 音量・マウス感度・コントローラー操作方法・レティクル・投げ方を保存する。
///
/// | 設定 | 効く場所 |
/// | --- | --- |
/// | **音量** | ゲーム全体（`AudioListener.volume`） |
/// | **マウス感度** | 視点を回す速さ。カメラ側がこの値を掛けて使う |
/// | **操作タイプ**（<see cref="ControllerOperation"/>） | コントローラーの移動・向き、フックの引き寄せ方 |
/// | **レティクル**（<see cref="AimReticle"/>） | 溜めている間の狙いの見せ方（HookChargeStrip） |
/// | **投げ方**（<see cref="ThrowOperation"/>） | 宇宙ごみ式のフックの撃ち方・投げ方（HookController・ThrowController） |
/// | **名前・フレームレート・垂直同期・画質** | タイトル画面で変える（下のほうにまとめてある。2026/10/6） |
///
/// 上の5つはポーズ画面（<see cref="PauseMenu"/>）の設定で変える（音量・マウス感度・操作タイプは、タイトル画面でも変えられる）。
///
/// **値は次に遊ぶときも残る**（`PlayerPrefs` に保存している）。
/// 保存の名前（`StarSweepers.〜` のキー）を変えると、今までの保存が読めなくなるので変えないこと。
///
/// ## 読み込みのしかた
///
/// 音量〜投げ方の5つは、**最初に読まれたときに1回だけ** <c>PlayerPrefs</c> から読み、あとは手元の値を返す（<see cref="Load"/>）。
/// 名前〜画質は、毎回 <c>PlayerPrefs</c> を直接読む。
///
/// ## 足したいとき
///
/// ここに項目を1つ足して、<see cref="PauseMenu"/> の設定画面にスライダーを1本足せばよい。
/// **設定を使う側は「この値を掛ける」だけ**にしておくと、増やしても壊れない。
/// </summary>
public static class GameSettings
{
    // 保存の名前（PlayerPrefs のキー）。変えると、今までの保存が読めなくなる
    private const string VolumeKey = "StarSweepers.Volume";
    private const string SensitivityKey = "StarSweepers.MouseSensitivity";
    private const string ControllerOperationKey = "StarSweepers.ControllerOperationType";
    private const string AimReticleKey = "StarSweepers.AimReticleType";
    private const string ThrowOperationKey = "StarSweepers.ThrowOperationType";

    // 読み込んだ値の控え（Load のあとはこちらを返す）
    private static float volume = 1f;
    private static float mouseSensitivity = 1f;

    /// <summary>もう <see cref="Load"/> で読み込んだか。</summary>
    private static bool loaded;
    private static ControllerOperationType controllerOperationType = ControllerOperationType.TypeA;
    private static AimReticleType aimReticleType = AimReticleType.TypeA;
    private static ThrowOperationType throwOperationType = ThrowOperationType.TwoButtons;

    /// <summary>フックの投げ方。TwoButtons が今までの操作。</summary>
    public static ThrowOperationType ThrowOperation
    {
        get
        {
            Load();
            return throwOperationType;
        }
        set
        {
            Load();

            // 知らない数字が来ても、2つのどちらかに丸めて保存する（ほかの切り替えの設定も同じ）
            throwOperationType = value == ThrowOperationType.OneButton
                ? ThrowOperationType.OneButton
                : ThrowOperationType.TwoButtons;
            PlayerPrefs.SetInt(ThrowOperationKey, (int)throwOperationType);
            PlayerPrefs.Save();
        }
    }

    /// <summary>コントローラーの操作方法。タイプAが従来の操作。変えたらすぐ保存する（<c>PlayerPrefs.Save</c>）。</summary>
    public static ControllerOperationType ControllerOperation
    {
        get
        {
            Load();
            return controllerOperationType;
        }
        set
        {
            Load();
            controllerOperationType = value == ControllerOperationType.TypeB
                ? ControllerOperationType.TypeB
                : ControllerOperationType.TypeA;
            PlayerPrefs.SetInt(ControllerOperationKey, (int)controllerOperationType);
            PlayerPrefs.Save();
        }
    }

    /// <summary>釣りフックのレティクル。タイプAが従来の見せ方。</summary>
    public static AimReticleType AimReticle
    {
        get
        {
            Load();
            return aimReticleType;
        }
        set
        {
            Load();
            aimReticleType = value == AimReticleType.TypeB ? AimReticleType.TypeB : AimReticleType.TypeA;
            PlayerPrefs.SetInt(AimReticleKey, (int)aimReticleType);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// 音の大きさ（0で無音、1でそのまま）。変えたらすぐ <c>AudioListener.volume</c> にも入れる。
    /// ほかの設定と違い、ここでは <c>PlayerPrefs.Save</c> を呼ばない（ファイルへの書き込みは、Unity が終了するときに行われる）。
    /// </summary>
    public static float Volume
    {
        get
        {
            Load();
            return volume;
        }

        set
        {
            Load();
            volume = Mathf.Clamp01(value);
            AudioListener.volume = volume;
            PlayerPrefs.SetFloat(VolumeKey, volume);
        }
    }

    /// <summary>
    /// マウスで視点を回す速さの倍率（1でそのまま、2で2倍）。
    /// **カメラ側が、自分の感度にこの値を掛けて使う。**
    /// 0.1〜3倍の間に収める。音量と同じく、ここでは <c>PlayerPrefs.Save</c> を呼ばない。
    /// </summary>
    public static float MouseSensitivity
    {
        get
        {
            Load();
            return mouseSensitivity;
        }

        set
        {
            Load();
            mouseSensitivity = Mathf.Clamp(value, 0.1f, 3f);
            PlayerPrefs.SetFloat(SensitivityKey, mouseSensitivity);
        }
    }

    /// <summary>
    /// 保存された値を読み込む。**最初に使われたときに1回だけ**動く。
    /// 保存が無いときは初期値（音量 1・感度 1・どれもタイプA／左右で使い分け）になる。
    /// 切り替えの設定は「1 ならもう片方、それ以外は今までどおり」として読む。
    /// </summary>
    private static void Load()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;

        volume = PlayerPrefs.GetFloat(VolumeKey, 1f);
        mouseSensitivity = PlayerPrefs.GetFloat(SensitivityKey, 1f);
        controllerOperationType = PlayerPrefs.GetInt(ControllerOperationKey, 0) == 1
            ? ControllerOperationType.TypeB
            : ControllerOperationType.TypeA;
        aimReticleType = PlayerPrefs.GetInt(AimReticleKey, 0) == 1
            ? AimReticleType.TypeB
            : AimReticleType.TypeA;
        throwOperationType = PlayerPrefs.GetInt(ThrowOperationKey, 0) == 1
            ? ThrowOperationType.OneButton
            : ThrowOperationType.TwoButtons;

        AudioListener.volume = volume;
    }

    /// <summary>
    /// 再生を始めるたびに読み直す（値がひとつしかないため）。
    /// エディタの設定によっては、前の再生で読んだ値（static の変数）が次の再生まで残るので、
    /// 「読み込み済み」の印を毎回消してから読み直す。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnStart()
    {
        loaded = false;
        Load();
    }

    // ------------------------------------------------------------
    // タイトル画面で変える設定（2026/10/6）
    // ------------------------------------------------------------

    private const string PlayerNameKey = "StarSweepers.PlayerName";
    private const string FrameRateKey = "StarSweepers.FrameRateLimit";
    private const string VSyncKey = "StarSweepers.VSync";
    private const string QualityKey = "StarSweepers.QualityLevel";

    /// <summary>名前の最大の文字数。</summary>
    public const int PlayerNameMaxLength = 16;

    /// <summary>
    /// **ゲーム内で表示する名前。** タイトル画面で入れる。空ならこれまでどおり「プレイヤー1」などになる。
    /// </summary>
    public static string PlayerName
    {
        get => PlayerPrefs.GetString(PlayerNameKey, string.Empty);
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length > PlayerNameMaxLength)
            {
                trimmed = trimmed.Substring(0, PlayerNameMaxLength);
            }
            PlayerPrefs.SetString(PlayerNameKey, trimmed);
            PlayerPrefs.Save();
        }
    }

    /// <summary>フレームレートの上限（fps。30〜240、保存が無ければ 60）。垂直同期が ON のときは、画面の更新に合わせるので効かない。</summary>
    public static int FrameRateLimit
    {
        get => PlayerPrefs.GetInt(FrameRateKey, 60);
        set
        {
            int clamped = Mathf.Clamp(value, 30, 240);
            PlayerPrefs.SetInt(FrameRateKey, clamped);
            PlayerPrefs.Save();
            ApplyGraphics();
        }
    }

    /// <summary>垂直同期（画面の更新に合わせて描く。カクつきが減る）。</summary>
    public static bool VSync
    {
        get => PlayerPrefs.GetInt(VSyncKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0);
            PlayerPrefs.Save();
            ApplyGraphics();
        }
    }

    /// <summary>画質（Project Settings > Quality の段階の番号）。-1 ならプロジェクトの初期値のまま。</summary>
    public static int QualityLevel
    {
        get => PlayerPrefs.GetInt(QualityKey, -1);
        set
        {
            int clamped = Mathf.Clamp(value, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
            PlayerPrefs.SetInt(QualityKey, clamped);
            PlayerPrefs.Save();
            ApplyGraphics();
        }
    }

    /// <summary>画質・垂直同期・フレームレート上限を、いまの設定に合わせる。</summary>
    public static void ApplyGraphics()
    {
        int quality = PlayerPrefs.GetInt(QualityKey, -1);
        if (quality >= 0 && quality < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != quality)
        {
            QualitySettings.SetQualityLevel(quality, true);
        }

        // 画質を変えると垂直同期も変わることがあるので、そのあとで合わせる
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        Application.targetFrameRate = FrameRateLimit;
    }

    /// <summary>起動したときに、保存してある画質などを当てる。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyGraphicsOnStart()
    {
        ApplyGraphics();
    }
}
