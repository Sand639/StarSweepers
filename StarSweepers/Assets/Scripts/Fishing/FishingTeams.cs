using UnityEngine;

/// <summary>
/// **チーム分けとゴールの割り当ての決まりを、1か所にまとめた場所。**
///
/// 「何人だと何チームで、4つのゴールを誰のものにするか」という**ルールそのもの**。
/// 通信のコードから切り離してあるので、**ここを読めば仕様が分かる**し、
/// バランス調整でルールを変えるときも、直すのはこのファイルだけで済む。
///
/// ## 決まっているルール（2026/9/10・小野田さん）
///
/// **2人1組のチームで戦う。ゴールは常に4つ（ステージの四方）。**
///
/// | 人数 | チーム数 | ゴール4つの割り当て |
/// | --- | --- | --- |
/// | 1〜2人 | 1 | 4つすべてそのチームのもの |
/// | 3〜4人 | 2 | **各チーム2つずつ** |
/// | 5〜6人 | 3 | 各チーム1つずつ ＋ **どのチームでも入る共通ゴール1つ** |
/// | 7〜8人 | 4 | **各チーム1つずつ** |
///
/// ## 点の入り方
///
/// | ゴールの種類 | 誰の点になるか |
/// | --- | --- |
/// | **チームのゴール** | **そのゴールを持つチーム**（誰が投げ入れても、そのチームの点） |
/// | **共通ゴール**（5〜6人のときの1つ） | **投げ入れた人のチーム** |
///
/// つまりチームのゴールは「自分たちの陣地へ運ぶ」形になり、
/// **間違えて相手のゴールへ入れると、相手に点が入る。**
/// </summary>
public static class FishingTeams
{
    /// <summary>1チームの人数。</summary>
    public const int PlayersPerTeam = 2;

    /// <summary>チームの最大数。</summary>
    public const int MaxTeams = 4;

    /// <summary>遊べる人数の上限（<see cref="PlayersPerTeam"/> × <see cref="MaxTeams"/>）。</summary>
    public const int MaxPlayers = PlayersPerTeam * MaxTeams;

    /// <summary>ステージにあるゴール（ポケット）の数。人数が変わっても常にこの数。</summary>
    public const int PocketCount = 4;

    /// <summary>どのチームのものでもない共通ゴールを表す番号。</summary>
    public const int NeutralTeam = -1;

    /// <summary>チームごとの色。画面表示とゴールの色に使う。</summary>
    private static readonly Color[] Colors =
    {
        new Color(0.30f, 0.60f, 0.95f), // 青
        new Color(0.95f, 0.45f, 0.35f), // 赤
        new Color(0.45f, 0.85f, 0.45f), // 緑
        new Color(0.95f, 0.80f, 0.30f), // 黄
    };

    private static readonly string[] Names = { "青チーム", "赤チーム", "緑チーム", "黄チーム" };

    /// <summary>参加番号（0から）から、所属チームを求める。0と1が1チーム、2と3が次のチーム。</summary>
    public static int TeamOf(int playerIndex)
    {
        if (playerIndex < 0)
        {
            return 0;
        }

        // 上限を超えた参加番号が来ても落ちないよう、最後のチームに入れる
        return Mathf.Min(playerIndex / PlayersPerTeam, MaxTeams - 1);
    }

    /// <summary>人数から、いくつのチームに分かれるかを求める（端数が出たら1チーム増やす）。</summary>
    public static int TeamCountFor(int playerCount)
    {
        if (playerCount <= 0)
        {
            return 1;
        }

        // 切り上げの割り算（3人 → 2チーム、4人 → 2チーム、5人 → 3チーム）
        int teams = (playerCount + PlayersPerTeam - 1) / PlayersPerTeam;
        return Mathf.Clamp(teams, 1, MaxTeams);
    }

    /// <summary>
    /// **4つのゴールを、どのチームのものにするかを決める。**
    ///
    /// 戻り値は長さ <see cref="PocketCount"/> の配列で、
    /// 中身はチーム番号か、共通ゴールを表す <see cref="NeutralTeam"/>。
    /// 並び順はステージのゴールの並び（北・東・南・西）に対応する。
    /// </summary>
    public static int[] PocketOwners(int teamCount)
    {
        switch (Mathf.Clamp(teamCount, 1, MaxTeams))
        {
            // 1チーム … 4つすべて自分たちのもの
            case 1:
                return new[] { 0, 0, 0, 0 };

            // 2チーム … 向かい合う2つずつを持つ（北と東 ／ 南と西）
            case 2:
                return new[] { 0, 0, 1, 1 };

            // 3チーム … 1つずつ持ち、残る1つはどのチームでも入る共通ゴール
            case 3:
                return new[] { 0, 1, 2, NeutralTeam };

            // 4チーム … 1つずつ持つ
            default:
                return new[] { 0, 1, 2, 3 };
        }
    }

    /// <summary>チームの色。</summary>
    public static Color TeamColor(int team)
    {
        if (team == NeutralTeam)
        {
            return new Color(0.75f, 0.75f, 0.78f); // 共通ゴールは灰色
        }

        return Colors[Mathf.Clamp(team, 0, Colors.Length - 1)];
    }

    /// <summary>チームの名前。</summary>
    public static string TeamName(int team)
    {
        if (team == NeutralTeam)
        {
            return "共通ゴール";
        }

        return Names[Mathf.Clamp(team, 0, Names.Length - 1)];
    }

    /// <summary>ゴールの位置の名前（並び順は <see cref="PocketOwners"/> と同じ）。</summary>
    public static string PocketPlaceName(int pocketIndex)
    {
        switch (Mathf.Clamp(pocketIndex, 0, PocketCount - 1))
        {
            case 0: return "北";
            case 1: return "東";
            case 2: return "南";
            default: return "西";
        }
    }
}
