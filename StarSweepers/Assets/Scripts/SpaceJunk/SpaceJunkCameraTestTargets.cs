using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **カメラの検証シーン用。「誰が自分で、誰が味方か」を、通信なしで決める部品。**（2026/10/7・小野田さん）
///
/// これがシーンにあると、カメラの種類の部品（<see cref="SpaceJunkCameraMode"/>）は、
/// 通信のプレイヤーではなく、ここに入れた人を「自分」「味方」として映す。
/// **本番のステージには置かないこと。**
///
/// ・<b>Tab</b> キーで「自分」を入れ替える（B の PC の画面がどう見えるかを確かめられる）
/// ・<b>Q／E</b> キーで「自陣の向き」を 90 度ずつ回す（自陣が南・東・北・西のチームの見え方）
/// ・画面の左上に、操作の案内と、いまのカメラ・距離を出す
///
/// 検証シーンは `Tools > StarSweepers > 宇宙ごみ > チームカメラの検証シーンを作り直す` で作る。
/// </summary>
public class SpaceJunkCameraTestTargets : MonoBehaviour
{
    [Tooltip("映す人。いちばん上が最初の「自分」、残りが味方")]
    [SerializeField] private List<Transform> players = new List<Transform>();

    [Tooltip("自陣が手前に来る向き（度）。0＝北向き（自陣が南）。Q／E で 90 度ずつ回せる")]
    [SerializeField] private float yaw;

    [Tooltip("「自分」を入れ替えるキー")]
    [SerializeField] private Key swapKey = Key.Tab;

    /// <summary>いまシーンにある、この部品（無ければ null。本番では null）。</summary>
    public static SpaceJunkCameraTestTargets Active { get; private set; }

    /// <summary>「自分」。</summary>
    public Transform Self => players.Count > 0 ? players[selfIndex % players.Count] : null;

    /// <summary>自陣が手前に来る向き（度）。</summary>
    public float Yaw => yaw;

    private int selfIndex;
    private GUIStyle style;

    private void OnEnable()
    {
        Active = this;
    }

    private void OnDisable()
    {
        if (Active == this)
        {
            Active = null;
        }
    }

    /// <summary>「自分」以外を入れる。</summary>
    public void GetTeammates(List<Transform> into)
    {
        Transform self = Self;
        foreach (Transform player in players)
        {
            if (player != null && player != self)
            {
                into.Add(player);
            }
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard[swapKey].wasPressedThisFrame && players.Count > 0)
        {
            selfIndex = (selfIndex + 1) % players.Count;
        }

        if (keyboard.qKey.wasPressedThisFrame)
        {
            yaw = Mathf.Repeat(yaw - 90f, 360f);
        }

        if (keyboard.eKey.wasPressedThisFrame)
        {
            yaw = Mathf.Repeat(yaw + 90f, 360f);
        }
    }

    private void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15, wordWrap = true };
        }

        string camera = SpaceJunkCameraModeSwitch.LocalModeSummary;
        SpaceJunkTeamGroupCamera team = FindFirstObjectByType<SpaceJunkTeamGroupCamera>();
        string distance = team != null ? $"チームカメラの距離：{team.Distance:0.0} m" : string.Empty;
        string selfName = Self != null ? Self.name : "なし";

        GUI.Box(new Rect(12f, 12f, 430f, 210f),
            "チームカメラの検証\n" +
            "WASD：A を動かす　矢印キー：B を動かす　Space：A がジャンプ\n" +
            "B の「勝手に歩く」を ON にすると、A が止まっていても B が動く\n" +
            $"［{SpaceJunkCameraModeSwitch.LocalToggleKeyName}］カメラ切り替え　{camera}\n" +
            $"［{swapKey}］自分を入れ替え（いまの自分：{selfName}）\n" +
            $"［Q／E］自陣の向きを回す（いま {yaw:0} 度）\n" +
            distance + "\n" +
            "値は再生中にカメラのインスペクターで変えて試せる（止めると戻る）",
            style);
    }
}
