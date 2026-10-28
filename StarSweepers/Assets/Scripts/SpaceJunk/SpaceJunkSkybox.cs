using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **宇宙ごみのマップ（ゲームのシーン）の背景を、星空にする。**（2026/10/28・大槻さん）
///
/// 星空は `Assets/Art/Sprites/Skybox_StarrySky.png`（横 2：縦 1 の星空の画像）を、
/// 空全体にぐるっと貼る素材（`Assets/Resources/SpaceJunkSkybox.mat`。Skybox/Panoramic）にしたもの。
///
/// **ラウンドの進行役（<see cref="SpaceJunkRound"/>）がいるシーンが表に出たら、そのシーンの空（RenderSettings.skybox）をこの素材に替える。**
/// マップのシーンのファイルは書き換えない（13 個のマップを1つずつ直さずに済み、ほかの人のシーンの作業ともぶつからないため）。
/// 空の差し替えは見た目だけで、ステージの明るさ（環境光）は今までどおり（作り直しをしないので変わらない）。
/// ロビーやタイトルへ移ると、そのシーンの空に戻る（空はシーンごとの設定なので、元に戻す処理は要らない）。
///
/// シーンに置く物は無い。再生を始めたときに自分で動き出す。
/// エディタでマップのシーンを開いただけでは星空にならない（再生中だけ）。
/// </summary>
public static class SpaceJunkSkybox
{
    /// <summary>星空の素材の名前（`Assets/Resources` の中）。</summary>
    private const string MaterialName = "SpaceJunkSkybox";

    /// <summary>読み込んだ星空の素材（1回だけ読む）。</summary>
    private static Material material;

    /// <summary>再生を始めたら、シーンの読み込みと切り替えを見張る。最初のシーンがマップのときのために、1回目もここで見る。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        ApplyIfGameScene();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyIfGameScene();
    }

    private static void OnActiveSceneChanged(Scene before, Scene after)
    {
        ApplyIfGameScene();
    }

    /// <summary>
    /// **表に出ているシーンに進行役がいれば、空を星空にする。**
    /// シーンを読み込んだときと、表のシーンが替わったときの両方で呼ぶ（どちらが先に来ても間に合うように。2回替えても困らない）。
    /// </summary>
    private static void ApplyIfGameScene()
    {
        // シーンの入れ替わりの途中は、前のマップの進行役も残っているので、表のシーンの進行役だけを探す
        Scene active = SceneManager.GetActiveScene();
        bool isGameScene = false;
        foreach (SpaceJunkRound round in Object.FindObjectsByType<SpaceJunkRound>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (round.gameObject.scene == active)
            {
                isGameScene = true;
                break;
            }
        }

        if (!isGameScene)
        {
            return;
        }

        if (material == null)
        {
            material = Resources.Load<Material>(MaterialName);
            if (material == null)
            {
                Debug.LogWarning($"[JUNK] 星空の素材（Assets/Resources/{MaterialName}.mat）が見つからないので、空は替えません。");
                return;
            }
        }

        RenderSettings.skybox = material;
    }
}
