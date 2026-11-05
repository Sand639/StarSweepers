using NUnit.Framework;
using UnityEngine;

/// <summary>
/// **ロビーで隠した「ほかの人の体」を、いつ見せるかの判定のテスト。**（2026/11/5）
///
/// ロビーでは、番号付きの出現場所へ着くまで体を隠している（2026/10/29）。
/// 見せる条件が「出現場所（高さ 1m）のすぐそばにいるのを見たとき」だけだったので、
/// 床へ落ちたあとの人・先に歩き出していた人が、試合中もずっと見えなかった（2026/11/5 に直した）。
///
/// `Window > General > Test Runner` の EditMode から実行できる。
/// </summary>
public class LobbyRevealTests
{
    /// <summary>ロビーの出現場所（シーンでは高さ 1m に置いてある）。</summary>
    private static readonly Vector3 Point = new Vector3(-4.5f, 1f, -6f);

    [Test]
    public void StaysHidden_RightAfterSpawn_WhileFarFromPoint()
    {
        // 生まれた直後は、まだ出現場所へ移されていない（別の場所にいる）。ここで見せると、一瞬ちがう場所に見える
        Assert.That(SpaceJunkPlayerSetup.ShouldRevealLobbyVisuals(true, new Vector3(0f, 0f, 6f), Point, 0.1f), Is.False);
    }

    [Test]
    public void Reveals_AtPoint()
    {
        Assert.That(SpaceJunkPlayerSetup.ShouldRevealLobbyVisuals(true, Point, Point, 0.1f), Is.True);
    }

    [Test]
    public void Reveals_AfterFallingToFloorUnderPoint()
    {
        // 出現場所は高さ 1m。体はすぐ床（高さ 0.07m）へ落ちる。真下にいるなら、着いたとみなす
        Assert.That(SpaceJunkPlayerSetup.ShouldRevealLobbyVisuals(true, new Vector3(-4.5f, 0.07f, -6f), Point, 0.1f), Is.True);
    }

    [Test]
    public void Reveals_AfterWaitingLongEnough_EvenIfWalkedAway()
    {
        // あとから入った人から見ると、先にいた人はもう出現場所を離れている。いつまでも隠さない
        Assert.That(SpaceJunkPlayerSetup.ShouldRevealLobbyVisuals(true, new Vector3(3f, 0.07f, 2f), Point, 1.5f), Is.True);
    }

    [Test]
    public void Reveals_OutsideLobby()
    {
        // 隠すのはロビーの出現のためだけ。試合のステージへ移ったら、すぐ見せる
        Assert.That(SpaceJunkPlayerSetup.ShouldRevealLobbyVisuals(false, new Vector3(20f, 0.1f, 1.25f), Point, 0f), Is.True);
    }
}
