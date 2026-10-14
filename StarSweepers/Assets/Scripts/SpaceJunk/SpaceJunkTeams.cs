using UnityEngine;

/// <summary>
/// **宇宙ごみ集めの「チーム分け」と「ゴールの割り当て」の決まりを、1か所にまとめた場所。**
///
/// ルールそのものだけを置いてある。通信のコードは入っていないので、
/// **ここを読めば仕様が分かる**し、バランスを変えるときも直すのはこのファイルだけで済む。
///
/// ## 決まっているルール（2026/9/17・大槻さん）
///
/// - **チームは最大4つ。** 遊べるのは最大6人（2026/10/14 に8人から変えた。大槻さん）
/// - **誰がどのチームに入るかは、ホストが自由に決める**（`SpaceJunkSession` が覚えている）
/// - **チーム数を指定してランダムに割り振ることもできる**（<see cref="RandomAssign"/>）
/// - **人数の偏り（3人対1人など）も許す。** ホストの判断に任せる
/// - ゴールはステージの四方に常に4つ。**2チームのときは向かい合う2つだけ開けて残りを閉ざす。
///   3チームのときは余る1つを閉ざす**
///
/// ## 釣り（<c>FishingTeams</c>）との違い
///
/// 釣りは「参加した順に2人ずつ自動でチーム分け」で固定だった。
/// こちらは**ホストが手で決められる**のが前提なので、
/// 参加番号からチームを計算する仕組み（<c>TeamOf</c>）は持っていない。
/// </summary>
public static class SpaceJunkTeams
{
    /// <summary>チームの最大数。ゴールが4つなので、それ以上には増やせない。</summary>
    public const int MaxTeams = 4;

    /// <summary>遊べる人数の上限（**6人で固定**。2026/10/14・大槻さん。タイトルで作る部屋の定員もこれ）。</summary>
    public const int MaxPlayers = 6;

    /// <summary>ステージにあるゴールの数。チーム数が変わっても常にこの数。</summary>
    public const int GoalCount = 4;

    /// <summary>**使わない（閉ざす）ゴール**を表す番号。3チームのときに1つだけ出る。</summary>
    public const int ClosedGoal = -1;

    /// <summary>チームごとの色。画面表示・ゴールの床・プレイヤーの見た目に使う。</summary>
    private static readonly Color[] Colors =
    {
        new Color(0.30f, 0.60f, 0.95f), // 青
        new Color(0.95f, 0.45f, 0.35f), // 赤
        new Color(0.45f, 0.85f, 0.45f), // 緑
        new Color(0.95f, 0.80f, 0.30f), // 黄
    };

    private static readonly string[] Names = { "青チーム", "赤チーム", "緑チーム", "黄チーム" };

    /// <summary>
    /// **4つのゴールを、どのチームのものにするかを決める。**
    ///
    /// 戻り値は長さ <see cref="GoalCount"/> の配列で、中身はチーム番号か
    /// <see cref="ClosedGoal"/>（使わないゴール）。
    /// 並び順はステージのゴールの並び（北・東・南・西）に対応する。
    /// </summary>
    public static int[] GoalOwners(int teamCount)
    {
        return GoalOwners(teamCount, -1);
    }

    /// <summary>
    /// **マップにあるゴール（<paramref name="goalMask"/>）も見て、ゴールの持ち主を決める。**
    ///
    /// <paramref name="goalMask"/> は「マップにあるゴールの番号」を1ビットずつ（0番=1 / 1番=2 / 2番=4 / 3番=8）。
    ///
    /// | マップのゴール | 決め方 |
    /// | --- | --- |
    /// | **4つそろっている**（または分からない＝-1） | <see cref="GoalOwners(int)"/> と同じ（2チームは北と南など。方角で決まる） |
    /// | **4つ未満** | **番号の小さい順に**、1チーム目・2チーム目…と1つずつ持つ。チームより多いゴールは閉ざす |
    ///
    /// 4つ未満のときは、番号は方角ではなく**順番**として使う（2026/10/1・大槻さん）。
    /// これで 0と1 の1対1も、1と3 の東西の1対1も、120度ずつ置いた 0・1・2 の3チームのマップも遊べる。
    /// 1チームのときは、どのマップでも全部のゴールが自分たちのもの。
    /// </summary>
    public static int[] GoalOwners(int teamCount, int goalMask)
    {
        int teams = Mathf.Clamp(teamCount, 1, MaxTeams);
        int allGoals = (1 << GoalCount) - 1;

        if (teams > 1 && goalMask >= 0 && (goalMask & allGoals) != allGoals)
        {
            int[] owners = { ClosedGoal, ClosedGoal, ClosedGoal, ClosedGoal };
            int nextTeam = 0;

            for (int goal = 0; goal < GoalCount; goal++)
            {
                if ((goalMask & (1 << goal)) != 0 && nextTeam < teams)
                {
                    owners[goal] = nextTeam;
                    nextTeam++;
                }
            }

            return owners;
        }

        switch (teams)
        {
            // 1チーム … 4つすべて自分たちのもの（1人で試すとき用）
            case 1:
                return new[] { 0, 0, 0, 0 };

            // 2チーム（4対4など）… **向かい合う1つずつ（北と南）を持ち、東と西は閉ざす**
            // （2026/9/22・大槻さん。以前は隣り合う2つずつ持っていた）
            case 2:
                return new[] { 0, ClosedGoal, 1, ClosedGoal };

            // 3チーム … 1つずつ持ち、**余る1つは閉ざす**
            case 3:
                return new[] { 0, 1, 2, ClosedGoal };

            // 4チーム（1対1対1対1／2対2対2対2）… 1つずつ持つ
            default:
                return new[] { 0, 1, 2, 3 };
        }
    }

    /// <summary>
    /// **ゴールの置き方（<paramref name="goalMask"/>）のマップで、そのチーム数を遊べるか。**
    /// 全チームが、自分のゴールを少なくとも1つ持てれば遊べる（2026/9/30・大槻さん）。
    ///
    /// <paramref name="goalMask"/> は「マップにあるゴールの番号」を1ビットずつ（北=1 / 東=2 / 南=4 / 西=8）。
    /// **数だけでなく場所も見る。** たとえば2チームは北と南を使うので、北と東の2つでは遊べない。
    /// 分からない（-1）ときは、遊べるものとして扱う。
    /// </summary>
    public static bool GoalsSupportTeamCount(int goalMask, int teamCount)
    {
        if (goalMask < 0)
        {
            return true;
        }

        int teams = Mathf.Clamp(teamCount, 1, MaxTeams);
        int[] owners = GoalOwners(teams, goalMask);

        for (int team = 0; team < teams; team++)
        {
            bool hasGoal = false;

            for (int goal = 0; goal < owners.Length; goal++)
            {
                if (owners[goal] == team && (goalMask & (1 << goal)) != 0)
                {
                    hasGoal = true;
                    break;
                }
            }

            if (!hasGoal)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>そのゴールの置き方で遊べる、いちばん多いチーム数。1チームも遊べなければ 0。</summary>
    public static int MaxPlayableTeams(int goalMask)
    {
        for (int teams = MaxTeams; teams >= 1; teams--)
        {
            if (GoalsSupportTeamCount(goalMask, teams))
            {
                return teams;
            }
        }

        return 0;
    }

    /// <summary>
    /// **チーム数を指定して、参加者をランダムに割り振る。**
    ///
    /// できるだけ均等になるように配ってから、順番を混ぜる。
    /// 戻り値は「参加者の並び順 → チーム番号」の配列。
    /// </summary>
    public static int[] RandomAssign(int playerCount, int teamCount)
    {
        int count = Mathf.Max(0, playerCount);
        int teams = Mathf.Clamp(teamCount, 1, MaxTeams);
        int[] result = new int[count];

        // まず 0,1,2,3,0,1,2,3… と順に配る（これで人数の差は最大1人になる）
        for (int i = 0; i < count; i++)
        {
            result[i] = i % teams;
        }

        // 配った結果を混ぜる（フィッシャー・イェーツ）
        for (int i = count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }

        return result;
    }

    /// <summary>チームの色。<see cref="ClosedGoal"/> を渡すと灰色になる。</summary>
    public static Color TeamColor(int team)
    {
        if (team == ClosedGoal)
        {
            return new Color(0.45f, 0.45f, 0.48f); // 閉ざしたゴールは暗い灰色
        }

        return Colors[Mathf.Clamp(team, 0, Colors.Length - 1)];
    }

    /// <summary>チームの名前。</summary>
    public static string TeamName(int team)
    {
        if (team == ClosedGoal)
        {
            return "使わない";
        }

        return Names[Mathf.Clamp(team, 0, Names.Length - 1)];
    }

    /// <summary>ゴールの位置の名前（並び順は <see cref="GoalOwners"/> と同じ）。</summary>
    public static string GoalPlaceName(int goalIndex)
    {
        switch (Mathf.Clamp(goalIndex, 0, GoalCount - 1))
        {
            case 0: return "北";
            case 1: return "東";
            case 2: return "南";
            default: return "西";
        }
    }

    /// <summary>
    /// そのチーム番号が、いまのチーム数で実際に使われているか。
    /// ホストがチーム数を減らしたときに、はみ出した人を拾い直すために使う。
    /// </summary>
    public static bool IsValidTeam(int team, int teamCount)
    {
        return team >= 0 && team < Mathf.Clamp(teamCount, 1, MaxTeams);
    }
}
