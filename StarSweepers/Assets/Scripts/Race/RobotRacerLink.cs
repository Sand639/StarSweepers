using UnityEngine;

/// <summary>
/// **ロボットをレースに出すための繋ぎ役。**
///
/// カート（<see cref="KartController"/>）を <c>RobotRig</c> に置き換えるための部品。
/// **RobotRig のプレハブは一切変えずに**、シーンに置いたものへこれを足すだけで走れるようになる。
///
/// ## なぜ繋ぎ役が要るか
///
/// ロボットは**入れ物（RobotRig）が動かず、中の体が動く**作りになっている。
/// しかも**合体中は「合体した体」、分離中は「上半身」か「下半身」**と、動く体が入れ替わる。
///
/// そのままだと、レース側は**動かない入れ物の位置**を見てしまい、
/// いつまで経ってもスタート地点にいることになる。
///
/// そこでこの部品が、毎フレーム
/// **「いま操作している体」をレース側に教えている。**
///
/// | やっていること | なぜ |
/// | --- | --- |
/// | 位置を見る先を、操作中の体にする | 入れ物は動かないため |
/// | カウントダウン中とゴール後は操作を止める | フライングを防ぐため |
/// | やり直しのとき、**3つの体をまとめて**スタートに戻す | 分離したまま戻すと、片方が置き去りになるため |
/// </summary>
[RequireComponent(typeof(RaceRacer))]
public class RobotRacerLink : MonoBehaviour
{
    [Tooltip("**同じオブジェクトの RobotController。** 空なら自分で探す")]
    [SerializeField] private RobotController robot;

    [Tooltip("やり直しのとき、上半身をどれだけ上に置くか（分離していたとき用）")]
    [SerializeField] private float upperResetHeight = 1.2f;

    private RaceRacer racer;

    private void Awake()
    {
        racer = GetComponent<RaceRacer>();

        if (robot == null)
        {
            robot = GetComponent<RobotController>();
        }

        if (robot == null)
        {
            Debug.LogError($"{name}: RobotController が見つかりません。レースに出られません。", this);
            enabled = false;
            return;
        }

        racer.ResetHandler = ResetBodies;
        racer.LowestHeightProvider = LowestBodyHeight;
        UpdatePositionSource();
    }

    private void OnDestroy()
    {
        if (racer != null)
        {
            racer.ResetHandler = null;
            racer.LowestHeightProvider = null;
        }
    }

    /// <summary>
    /// **出ている体のうち、一番低いものの高さ。**
    ///
    /// 分離中に**操作していないほうの体**が落ちても、戻せるようにするため。
    /// 合体中は、隠れている上半身・下半身は数えない（位置が古いまま残っているため）。
    /// </summary>
    private float LowestBodyHeight()
    {
        float lowest = float.MaxValue;

        ConsiderBody(robot.CombinedBodyPart, ref lowest);
        ConsiderBody(robot.UpperBodyPart, ref lowest);
        ConsiderBody(robot.LowerBodyPart, ref lowest);

        return lowest < float.MaxValue ? lowest : racer.TrackedPosition.y;
    }

    /// <summary>その体が出ていれば（隠れていなければ）、いちばん低い高さの候補に入れる。</summary>
    private static void ConsiderBody(RobotBody body, ref float lowest)
    {
        if (body != null && body.gameObject.activeInHierarchy)
        {
            lowest = Mathf.Min(lowest, body.transform.position.y);
        }
    }

    /// <summary>毎フレーム、位置を見る先を合わせ直し、操作してよいかをロボットに伝える。</summary>
    private void Update()
    {
        UpdatePositionSource();

        // カウントダウン中とゴール後は動けない
        robot.ControlSuspended = !racer.ControlEnabled;
    }

    /// <summary>レース側が見る位置を、**いま操作している体**に合わせる。</summary>
    private void UpdatePositionSource()
    {
        RobotBody active = robot.ActiveBody;

        if (active != null)
        {
            racer.PositionSource = active.transform;
        }
    }

    /// <summary>
    /// **3つの体をまとめてスタート地点へ戻す。**
    ///
    /// 分離中に戻した場合、上半身と下半身が同じ場所に並ぶので、
    /// **E を押せばその場で合体できる。**
    /// </summary>
    private void ResetBodies(Vector3 position, Quaternion rotation)
    {
        Teleport(robot.CombinedBodyPart, position, rotation);
        Teleport(robot.LowerBodyPart, position, rotation);
        Teleport(robot.UpperBodyPart, position + Vector3.up * upperResetHeight, rotation);
    }

    /// <summary>体が繋がれていない（null）なら何もしない、安全な瞬間移動。</summary>
    private static void Teleport(RobotBody body, Vector3 position, Quaternion rotation)
    {
        if (body != null)
        {
            body.Teleport(position, rotation);
        }
    }
}
