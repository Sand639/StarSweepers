using UnityEngine;

/// <summary>コントローラーの操作方法。</summary>
public enum ControllerOperationType
{
    TypeA = 0,
    TypeB = 1
}

/// <summary>
/// **設定画面で変えられる値**を、どこからでも見られるようにしたもの。
///
/// 音量・マウス感度・コントローラー操作方法を保存する。
///
/// | 設定 | 効く場所 |
/// | **音量** | ゲーム全体（`AudioListener.volume`） |
/// | **マウス感度** | 視点を回す速さ。カメラ側がこの値を掛けて使う |
///
/// **値は次に遊ぶときも残る**（`PlayerPrefs` に保存している）。
///
/// ## 足したいとき
///
/// ここに項目を1つ足して、<see cref="PauseMenu"/> の設定画面にスライダーを1本足せばよい。
/// **設定を使う側は「この値を掛ける」だけ**にしておくと、増やしても壊れない。
/// </summary>
public static class GameSettings
{
    private const string VolumeKey = "StarSweepers.Volume";
    private const string SensitivityKey = "StarSweepers.MouseSensitivity";
    private const string ControllerOperationKey = "StarSweepers.ControllerOperationType";

    private static float volume = 1f;
    private static float mouseSensitivity = 1f;
    private static bool loaded;
    private static ControllerOperationType controllerOperationType = ControllerOperationType.TypeA;

    /// <summary>コントローラーの操作方法。タイプAが従来の操作。</summary>
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

    /// <summary>音の大きさ（0で無音、1でそのまま）。</summary>
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

    /// <summary>保存された値を読み込む。**最初に使われたときに1回だけ**動く。</summary>
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

        AudioListener.volume = volume;
    }

    /// <summary>再生を始めるたびに読み直す（値がひとつしかないため）。</summary>
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

    /// <summary>フレームレートの上限（fps）。垂直同期が ON のときは、画面の更新に合わせるので効かない。</summary>
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
