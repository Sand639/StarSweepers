using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// プレイヤー1人分の得点。
///
/// **点数はホストだけが書き換えられる。**
/// 参加者側から自分の点数を勝手に増やすことはできない。
///
/// これが「動きは本人、勝敗と点数はホストが決める」という作りの実物。
/// 対戦ゲームで一番ズルされたくない部分を、ホスト側に閉じ込めている。
/// </summary>
public class PlayerScore : NetworkBehaviour
{
    /// <summary>
    /// いま出ているプレイヤー全員。
    ///
    /// アイテム側や画面表示から**毎フレーム探し回らずに済む**ように、
    /// 出てきたときに自分で登録する形にしている。
    /// </summary>
    public static readonly List<PlayerScore> All = new List<PlayerScore>();

    /// <summary>
    /// 得点。**書き換えられるのはホストのみ**（NetworkVariable の初期設定）。
    /// 読むのは全員できる。
    /// </summary>
    private readonly NetworkVariable<int> score = new NetworkVariable<int>(0);

    /// <summary>いまの得点。表示に使う。</summary>
    public int Score => score.Value;

    public override void OnNetworkSpawn()
    {
        // 同じ物が二度登録されないように
        if (!All.Contains(this))
        {
            All.Add(this);
        }

        // ホスト側でこの数が人数分に増えていれば、
        // 「全員の位置が見えている」＝アイテムを取れる状態になっている
        Debug.Log($"[LAN] 得点の登録：番号 {OwnerClientId}（登録数 {All.Count}／ホスト：{IsServer}）");
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
    }

    /// <summary>
    /// 得点を足す。**ホストでしか動かない。**
    /// 参加者側から呼んでも何も起きない（呼び間違いに気づけるよう警告を出す）。
    /// </summary>
    public void AddScore(int amount)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"{name}: 得点を足せるのはホストだけです。", this);
            return;
        }

        score.Value += amount;
    }

    /// <summary>得点を0に戻す。ホストでしか動かない。</summary>
    public void ResetScore()
    {
        if (!IsServer)
        {
            return;
        }

        score.Value = 0;
    }
}
