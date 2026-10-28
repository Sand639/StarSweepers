using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **ゴールエリアにチームのプレイヤーを乗せて、奥から横に並んで飛ばし、元のゴールの位置に設置する係。**
/// （2026/10/27 に <see cref="SpaceJunkGoalFlyIntro"/> の中に作り、10/28 に <see cref="SpaceJunkLandingIntro"/> でも使うために分けた）
///
/// 始まりの演出の部品が持って使う。シーンには置かない。
///
/// 1. 飛ぶ（使う側が渡す Fly 秒）… 各チームのゴールエリアが、そのチームのプレイヤーを乗せ、横に並んでマップの奥から、
///    マップの奥の端の少し先まで飛ぶ（近づくにつれてゆっくりに）。乗っている人の並びが、奥を向いたときの左右になるように向きを回しておく
/// 2. 設置（使う側が渡す Land 秒）… それぞれ元のゴールの位置へ飛んで設置される（途中で少しふくらむ。向きも元に戻す）
///
/// 飛んでいるのは**ゴールの見た目の写し**（当たり判定なし）。本物のゴールは動かさず、演出のあいだ見た目だけ隠す
/// （本物を動かすと、ボットが作る歩ける床の地図や、ゴールの判定がずれるため）。
/// 写すのは <see cref="SpaceJunkGoal.IntroVisual"/>（ふだんは色を塗る床。ゴールエリアを3Dモデルに替えたら、そのモデル）。
///
/// プレイヤーは、**設置されたときに立つ場所**（<see cref="SpaceJunkPlayerSetup.TryGetGoalSpawnPosition"/>。ゴールエリアの上の決まった場所）を
/// ゴールエリアと同じだけ動かして回した所に、**全員のPCで全員ぶん**置く。自分のぶんはその位置が通信で配られ、
/// ほかの人のぶんは、通信の遅れでゴールエリアからずれて見えないように、こちらで上書きする。設置された時点で、ずれは 0 になる。
/// そのため、**使う側は `DefaultExecutionOrder(10000)` の LateUpdate から <see cref="Update"/> を呼ぶこと**
/// （通信の位置合わせ（NetworkTransform）は LateUpdate より前に動くので、そのあとで上書きすれば、そのフレームの表示に勝てる）。
///
/// 向き・距離・高さ・すき間は、<see cref="SpaceJunkRound"/> の「ゴールが飛んでくる演出」の Fly In From・Fly In Distance・Fly In Height・Fly In Gap。
/// 時間は、全員で同期された時計から求めた秒数を使う側が渡すので、どのPCでも同じ場面になる。
/// </summary>
public class SpaceJunkGoalCarrier
{
    /// <summary>設置するときに、途中でふくらむ高さ（m）。まっすぐ降りるより「飛んでいく」感じにするため。</summary>
    private const float LandArcHeight = 1.5f;

    /// <summary>見た目が無いゴールの、横に並べるときの幅（m）。</summary>
    private const float DefaultGoalWidth = 4f;

    /// <summary>飛んでくるゴールエリア1つぶん。</summary>
    public class FlyingGoal
    {
        /// <summary>元のゴール（動かさない）。</summary>
        public SpaceJunkGoal Goal;

        /// <summary>設置される場所（元のゴールの位置）。</summary>
        public Vector3 Final;

        /// <summary>飛び始め（奥）。</summary>
        public Vector3 Start;

        /// <summary>飛び終わり（マップに近づいた所）。ここから元の位置へ設置される。</summary>
        public Vector3 Approach;

        /// <summary>飛んでいる間に回しておく角度（度）。プレイヤーが並ぶ向きが、奥を向いたときの左右になるように回す。設置で 0 に戻る。</summary>
        public float FlyYaw;

        /// <summary>横に並べるときの幅（m）。</summary>
        public float Width = DefaultGoalWidth;

        /// <summary>いまの位置（毎フレーム決める）。</summary>
        public Vector3 Position;

        /// <summary>いまの向き（毎フレーム決める）。</summary>
        public Quaternion Rotation = Quaternion.identity;

        /// <summary>飛んでいる見た目の写し（当たり判定なし）。</summary>
        public GameObject Copy;

        /// <summary>演出のあいだ隠している、本物の見た目。</summary>
        public readonly List<Renderer> Hidden = new List<Renderer>();
    }

    /// <summary>ラウンドの係。向き・距離などの設定と、マップの中央はここから読む。</summary>
    private readonly SpaceJunkRound round;

    /// <summary>飛んでくるゴールエリアを用意したか（用意するのは1回だけ）。</summary>
    private bool prepared;

    /// <summary>ゴールエリアに乗せるために切った、プレイヤーの当たり判定（設置したら戻す）。</summary>
    private readonly List<CharacterController> disabledControllers = new List<CharacterController>();

    /// <summary>飛んでくるゴールエリア（奥を向いたときの左から順）。</summary>
    public List<FlyingGoal> Goals { get; } = new List<FlyingGoal>();

    /// <summary>乗っているプレイヤーと、奥を向いたときの左右の位置（ゴールエリアが動いている間、毎フレーム作り直す）。</summary>
    public List<(FishingNetPlayer player, float across)> Riders { get; } = new List<(FishingNetPlayer, float)>();

    /// <summary>飛んでくる元の向き（奥）。</summary>
    public Vector3 Backward { get; private set; } = Vector3.forward;

    /// <summary>奥を向いたときの右。ゴールエリアはこの向きに並ぶ。</summary>
    public Vector3 Right { get; private set; } = Vector3.right;

    /// <summary>横に並んだゴールエリア全部の幅（すき間も入れる。m）。</summary>
    public float TotalWidth { get; private set; }

    /// <summary>設置し終えたか。</summary>
    public bool Landed { get; private set; }

    public SpaceJunkGoalCarrier(SpaceJunkRound round)
    {
        this.round = round;
    }

    /// <summary>マップの中央（<see cref="SpaceJunkRound.IntroLookCenter"/>）。</summary>
    private Vector3 Center => round.IntroLookCenter;

    /// <summary>
    /// **飛んでくるゴールエリアを用意する**（何度呼んでも、用意するのは1回だけ）。どこかのチームのゴール（閉ざされていないもの）を、
    /// 奥を向いたときの左から順に横に並べ、飛び始めと飛び終わりの位置・見た目の写しを決める。
    /// </summary>
    public void Prepare()
    {
        if (prepared)
        {
            return;
        }

        prepared = true;

        Vector3 backward = Flat(round.FlyInFrom);
        Backward = backward.sqrMagnitude < 0.01f ? Vector3.forward : backward.normalized;
        Right = Vector3.Cross(Vector3.up, Backward);

        Scene scene = round.gameObject.scene;
        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null && goal.gameObject.scene == scene && !goal.IsClosed)
            {
                Goals.Add(new FlyingGoal { Goal = goal, Final = goal.transform.position });
            }
        }

        // 奥を向いたときの左から順（同じ所にあれば番号順。どのPCでも同じ並びになるように）
        Goals.Sort((a, b) =>
        {
            int compare = Vector3.Dot(a.Final, Right).CompareTo(Vector3.Dot(b.Final, Right));
            return compare != 0 ? compare : a.Goal.GoalIndex.CompareTo(b.Goal.GoalIndex);
        });

        // マップの大きさ … 中央からいちばん遠いゴールまでの水平の距離＋4m（8〜80m に収める）
        float radius = 0f;
        foreach (FlyingGoal goal in Goals)
        {
            radius = Mathf.Max(radius, Flat(goal.Final - Center).magnitude);
        }
        radius = Mathf.Clamp(radius + 4f, 8f, 80f);

        float total = round.FlyInGap * Mathf.Max(0, Goals.Count - 1);
        foreach (FlyingGoal goal in Goals)
        {
            BuildCopy(goal);
            goal.FlyYaw = FlyYawOf(goal.Final);
            total += goal.Width;
        }
        TotalWidth = total;

        // 飛び終わり … マップの奥の端の少し先に、横に並ぶ。飛び始めは、そこからさらに奥
        Vector3 approachCenter = Center + Backward * radius;
        float cursor = -total * 0.5f;

        foreach (FlyingGoal goal in Goals)
        {
            float across = cursor + goal.Width * 0.5f;
            cursor += goal.Width + round.FlyInGap;

            goal.Approach = new Vector3(approachCenter.x, goal.Final.y + round.FlyInHeight, approachCenter.z) + Right * across;
            goal.Start = goal.Approach + Backward * round.FlyInDistance;
            goal.Position = goal.Start;
        }
    }

    /// <summary>
    /// **ゴールエリアを動かし、全員を乗せる。** <paramref name="sinceStart"/> は飛び始めてからの秒数（マイナスの間は飛び始めの位置で止まる）。
    /// 飛ぶ＋設置の秒数を過ぎたら設置する（そのあとは何もしない）。
    /// </summary>
    public void Update(float sinceStart, float flySeconds, float landSeconds)
    {
        if (Landed)
        {
            return;
        }

        if (sinceStart < flySeconds + landSeconds)
        {
            MoveGoals(sinceStart, flySeconds, landSeconds);
            RidePlayers();
        }
        else
        {
            Land();
        }
    }

    /// <summary>
    /// **ゴールの見た目の写しを作り、本物の見た目を隠す。** 当たり判定は写さない。
    /// 色を塗る床の材質は本物と同じもの（チームの色を塗った物）を使うので、色もそのまま出る。
    /// </summary>
    private static void BuildCopy(FlyingGoal goal)
    {
        goal.Copy = new GameObject($"FlyingGoal_{goal.Goal.name}");
        goal.Copy.transform.position = goal.Final;

        Transform root = goal.Goal.IntroVisual;
        if (root == null)
        {
            return;
        }

        if (goal.Goal.TryGetIntroVisualBounds(out Bounds bounds))
        {
            goal.Width = Mathf.Max(DefaultGoalWidth, bounds.size.x, bounds.size.z);
        }

        foreach (MeshRenderer source in root.GetComponentsInChildren<MeshRenderer>())
        {
            MeshFilter filter = source.GetComponent<MeshFilter>();
            if (!source.enabled || filter == null || filter.sharedMesh == null)
            {
                continue;
            }

            // 写しの親は元のゴールの位置にあり、回転も大きさも無いので、世界での位置・向き・大きさをそのまま入れればよい
            GameObject part = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(goal.Copy.transform, false);
            part.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            part.transform.localScale = source.transform.lossyScale;
            part.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            part.GetComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;

            source.enabled = false;
            goal.Hidden.Add(source);
        }
    }

    /// <summary>
    /// 飛んでいる間に回しておく角度（度）。ゴールの「横」（ゴールから中心へ向かう向きと直角。プレイヤーが並ぶ向き。
    /// <see cref="SpaceJunkPlayerSetup.TryGetGoalSpawnPosition"/> と同じ求め方）が、奥を向いたときの左右になる角度。
    /// 回す量が小さいほう（±90 度以内）を選ぶ。
    /// </summary>
    private float FlyYawOf(Vector3 final)
    {
        Vector3 toCenter = new Vector3(-final.x, 0f, -final.z);
        if (toCenter.sqrMagnitude < 0.01f)
        {
            toCenter = Vector3.forward;
        }

        Vector3 side = Vector3.Cross(Vector3.up, toCenter.normalized);
        float angle = Vector3.SignedAngle(side, Right, Vector3.up);

        if (angle > 90f)
        {
            angle -= 180f;
        }
        else if (angle < -90f)
        {
            angle += 180f;
        }

        return angle;
    }

    /// <summary>
    /// ゴールエリアの、いまの位置と向きを決めて写しを動かす。
    /// 飛ぶ（奥から、マップに近づいた所まで。近づくにつれてゆっくりに）→ 設置（元のゴールの位置へ）。
    /// </summary>
    private void MoveGoals(float sinceStart, float flySeconds, float landSeconds)
    {
        foreach (FlyingGoal goal in Goals)
        {
            Quaternion flyRotation = Quaternion.Euler(0f, goal.FlyYaw, 0f);

            // 下の Mathf.Max(0.01f, …) は、秒数が 0 に設定されたときに 0 で割らないための保険
            if (sinceStart < flySeconds)
            {
                // 飛ぶ。始まる前（マイナス）は飛び始めの位置で止まる。終わりに向かってゆっくりにする
                float t = Mathf.Clamp01(sinceStart / Mathf.Max(0.01f, flySeconds));
                float eased = 1f - (1f - t) * (1f - t);
                goal.Position = Vector3.Lerp(goal.Start, goal.Approach, eased);
                goal.Rotation = flyRotation;
            }
            else
            {
                // 設置。元のゴールの位置へ（途中で少しふくらむ）。向きも元に戻す
                float t = Mathf.Clamp01((sinceStart - flySeconds) / Mathf.Max(0.01f, landSeconds));
                float eased = Mathf.SmoothStep(0f, 1f, t);
                goal.Position = Vector3.Lerp(goal.Approach, goal.Final, eased) + Vector3.up * (LandArcHeight * Mathf.Sin(t * Mathf.PI));
                goal.Rotation = Quaternion.Slerp(flyRotation, Quaternion.identity, eased);
            }

            if (goal.Copy != null)
            {
                goal.Copy.transform.SetPositionAndRotation(goal.Position, goal.Rotation);
            }
        }
    }

    /// <summary>
    /// **全員を、自分のチームのゴールエリアに乗せる**（全員のPCで、全員ぶん）。
    /// 設置されたときに立つ場所を、ゴールエリアと同じだけ動かして回した所に置く。
    /// 落ちたりぶつかったりしないよう、乗っている間は当たり判定（CharacterController）を切る（ほかの人のぶんは元から切れている）。
    /// </summary>
    private void RidePlayers()
    {
        Riders.Clear();

        SpaceJunkSession session = SpaceJunkSession.Current;
        if (session == null)
        {
            return;
        }

        Scene active = SceneManager.GetActiveScene();

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player == null || !player.IsSpawned)
            {
                continue;
            }

            SpaceJunkPlayerSetup setup = player.GetComponent<SpaceJunkPlayerSetup>();
            if (setup == null || !setup.TryGetGoalSpawnPosition(out Vector3 spot))
            {
                continue;
            }

            FlyingGoal ride = FindFlying(SpaceJunkGoal.FirstOwnedBy(session.TeamOf(player.PlayerKey), active));
            if (ride == null)
            {
                continue;
            }

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null && controller.enabled)
            {
                controller.enabled = false;
                disabledControllers.Add(controller);
            }

            Vector3 position = ride.Position + ride.Rotation * (spot - ride.Final);
            player.transform.position = position;
            Riders.Add((player, Vector3.Dot(position, Right)));
        }
    }

    /// <summary>そのゴールの飛んでくるゴールエリア（無ければ null）。</summary>
    private FlyingGoal FindFlying(SpaceJunkGoal goal)
    {
        foreach (FlyingGoal flyingGoal in Goals)
        {
            if (flyingGoal.Goal == goal)
            {
                return flyingGoal;
            }
        }

        return null;
    }

    /// <summary>
    /// **設置する。** 全員を設置された場所（ずれ 0）に置いてから、写しを消して本物の見た目を出し、当たり判定を戻す。
    /// </summary>
    private void Land()
    {
        Landed = true;

        foreach (FlyingGoal goal in Goals)
        {
            goal.Position = goal.Final;
            goal.Rotation = Quaternion.identity;
        }

        RidePlayers();
        Restore();
    }

    /// <summary>
    /// 写しを消して本物の見た目を出し、切った当たり判定を戻す（設置したとき・演出が途中で終わったとき）。何度呼んでもよい。
    /// </summary>
    public void Restore()
    {
        foreach (FlyingGoal goal in Goals)
        {
            if (goal.Copy != null)
            {
                Object.Destroy(goal.Copy);
                goal.Copy = null;
            }

            foreach (Renderer hidden in goal.Hidden)
            {
                if (hidden != null)
                {
                    hidden.enabled = true;
                }
            }

            goal.Hidden.Clear();
        }

        foreach (CharacterController controller in disabledControllers)
        {
            if (controller != null)
            {
                controller.enabled = true;
            }
        }

        disabledControllers.Clear();
    }

    /// <summary>高さを 0 にした（地面と平行にした）ベクトル。</summary>
    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value;
    }
}
