using ProjectEL4S.InputControl;
using UnityEngine;

/// <summary>
/// **1台のPCで何人遊ぶか（オンライン対戦でも使う）。**
///
/// - <see cref="PlayerCount"/> が 1（初期値）なら、今までどおり「1台に1人」。どの機器でも動く。
/// - 2 以上にすると、このPCに席（<see cref="InputSeatManager"/>）を用意し、
///   ホストに頼んで**このPCが操作するプレイヤーを人数ぶん出してもらう**（<c>SpaceJunkSession.RequestLocalPlayerCount</c>）。
///   1人目＝席0（P1）、2人目＝席1（P2）… の機器で動く。
///
/// ## 人ごとの番号（プレイヤーキー）
///
/// チーム分けなどは、これまで「接続番号（ClientId）」＝「1台のPC」で人を見分けていた。
/// 1台に複数人いると見分けられないので、**接続番号の上の桁に「そのPCの何人目か」を足した番号**を使う。
/// **1人目は接続番号そのまま**なので、1台1人のときは今までと同じ番号になる。
/// </summary>
public static class LocalMultiplayer
{
    /// <summary>1台のPCで遊べる最大の人数。</summary>
    public const int MaxLocalPlayers = 4;

    /// <summary>何人目かを入れる桁（接続番号は小さい数なので、上の 8 ビットは使われていない）。</summary>
    private const int SeatShift = 56;
    private const ulong ClientMask = (1UL << SeatShift) - 1UL;

    private static int playerCount = 1;

    /// <summary>このPCで作った席の管理役（シーンに最初から置いてある物は消さないため、自分で作った物だけ覚える）。</summary>
    private static InputSeatManager createdSeats;

    /// <summary>このPCで遊ぶ人数（1〜<see cref="MaxLocalPlayers"/>）。変えると席を用意し直す。</summary>
    public static int PlayerCount
    {
        get => playerCount;
        set
        {
            int clamped = Mathf.Clamp(value, 1, MaxLocalPlayers);
            if (clamped == playerCount)
            {
                return;
            }

            playerCount = clamped;
            EnsureInputSeats();
        }
    }

    /// <summary>1台で複数人か。false なら今までどおり（席を使わない）。</summary>
    public static bool IsActive => playerCount > 1;

    // ------------------------------------------------------------
    // 人ごとの番号
    // ------------------------------------------------------------

    /// <summary>接続番号と「そのPCの何人目か（0から）」から、人ごとの番号を作る。0人目は接続番号そのまま。</summary>
    public static ulong MakeKey(ulong clientId, int localSeat)
    {
        return localSeat <= 0 ? clientId : (clientId & ClientMask) | ((ulong)localSeat << SeatShift);
    }

    /// <summary>人ごとの番号から、その人がいるPCの接続番号を取り出す。</summary>
    public static ulong ClientOf(ulong key)
    {
        return key & ClientMask;
    }

    /// <summary>人ごとの番号から、そのPCの何人目か（0から）を取り出す。</summary>
    public static int SeatOf(ulong key)
    {
        return (int)(key >> SeatShift);
    }

    // ------------------------------------------------------------
    // 席（キーボード・マウス・コントローラの割り当て）
    // ------------------------------------------------------------

    /// <summary>
    /// 人数に合わせて席の管理役を用意する。2人以上なら作り（シーンをまたいで残す）、1人なら片付ける。
    /// シーンにもともと置いてある管理役（検証シーンなど）はそのまま使い、消さない。
    /// </summary>
    public static void EnsureInputSeats()
    {
        if (!IsActive)
        {
            DestroyCreatedSeats();
            return;
        }

        InputSeatManager current = InputSeatManager.Instance;
        if (current != null && current != createdSeats)
        {
            // シーンに置いてある物を使う
            return;
        }

        if (current != null && current.SeatCount == playerCount)
        {
            return;
        }

        DestroyCreatedSeats();
        createdSeats = InputSeatManager.CreatePersistent(playerCount);
    }

    private static void DestroyCreatedSeats()
    {
        if (createdSeats != null)
        {
            // すぐ消す（同じフレームで作り直すと、古い管理役が残っていて新しいほうが使われないため）
            Object.DestroyImmediate(createdSeats.gameObject);
        }

        createdSeats = null;
    }
}
