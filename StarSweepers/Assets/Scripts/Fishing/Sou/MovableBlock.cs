using System.Collections;
using UnityEngine;

/// <summary>拉扯時，方塊要用哪種方式決定移動方向。</summary>
/// <remarks>（日本語）引っ張ったとき、ブロックがどの向きへ動くかの決め方。</remarks>
public enum MovableBlockMoveMode
{
    /// <summary>引っ張った人のいる方向へ動く（どの柱を引いても同じ）。</summary>
    TowardPlayer,

    /// <summary>引っ掛けた柱（Front／Back／Left／Right）が指す、ブロック基準の向きへ動く。</summary>
    FourDirections
}

/// <summary>方塊四周可被拉扯的位置。</summary>
/// <remarks>（日本語）ブロックの4辺にある、フックを掛けられる柱の位置。ブロックのローカル座標で見た向き。</remarks>
public enum MovableBlockPullSide
{
    Front,
    Back,
    Left,
    Right
}

/// <summary>可由玩家拉動的大型方塊。</summary>
/// <remarks>
/// （日本語）**フックで引っ張ると、決まった距離だけ動く大きなブロック。**
///
/// ブロック本体にはこの部品と、キネマティックな Rigidbody を付ける。
/// 4辺の子オブジェクト（柱）に <see cref="MovableBlockPullPoint"/> を付け、
/// フックは柱に掛かる。柱が <see cref="Pull"/> を呼ぶと、1回ぶん動く。
///
/// プレハブ：`Assets/Scenes/Test/Sou/MovableBlock.prefab`。
/// テスト：`Fishing/Editor/MovableBlockTests.cs`（インスペクターの項目名を文字列で指定しているので、項目名は変えないこと）。
/// オンラインの位置同期は未対応（各PCでそれぞれ動く）。
/// </remarks>
[RequireComponent(typeof(Rigidbody))]
public class MovableBlock : MonoBehaviour
{
    /// <summary>動く向きの決め方（<see cref="MovableBlockMoveMode"/>）。</summary>
    [Header("拉扯移動")]
    [SerializeField] private MovableBlockMoveMode moveMode = MovableBlockMoveMode.FourDirections;

    /// <summary>1回の引っ張りで進む距離（メートル）。</summary>
    [Min(0f)]
    [SerializeField] private float moveDistance = 2f;

    /// <summary>動くのにかける秒数。0 なら一瞬で移動する。</summary>
    [Min(0f)]
    [SerializeField] private float moveDuration = 0.35f;

    private Rigidbody body;

    /// <summary>いま動いている最中か。動いている間は柱に掛けられない（<see cref="MovableBlockPullPoint.CanBeHooked"/>）。</summary>
    public bool IsMoving { get; private set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    /// <summary>指定された拉點から、方塊を一回移動させる。</summary>
    /// <returns>動き始めたら true。動いている最中・向きが決まらない・距離 0 のときは false。</returns>
    public bool Pull(Vector3 playerPosition, MovableBlockPullSide side)
    {
        if (IsMoving)
        {
            return false;
        }

        Vector3 direction = CalculateMoveDirection(
            moveMode, transform.rotation, side, transform.position, playerPosition);
        // 向きがほぼゼロ（真上から引いた など）なら動かさない
        if (direction.sqrMagnitude <= 0.0001f || moveDistance <= 0f)
        {
            return false;
        }

        Vector3 destination = transform.position + direction * moveDistance;
        // Awake より前に呼ばれた場合（エディタのテストなど）に備える
        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }

        // 秒数 0、またはエディタで再生していないとき（コルーチンが回らない）は一気に動かす
        if (moveDuration <= 0f || !Application.isPlaying)
        {
            body.position = destination;
            transform.position = destination;
            return true;
        }

        StartCoroutine(MoveTo(destination));
        return true;
    }

    /// <summary>
    /// 物理の更新（FixedUpdate）に合わせて、なめらかに目的地まで動かす。
    /// MovePosition を使うので、キネマティックでも上に乗った物やプレイヤーを押せる。
    /// </summary>
    private IEnumerator MoveTo(Vector3 destination)
    {
        IsMoving = true;
        Vector3 start = body.position;
        float elapsed = 0f;

        while (elapsed < moveDuration)
        {
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;
            // SmoothStep で、動き始めと止まる直前をゆっくりにする
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / moveDuration));
            body.MovePosition(Vector3.Lerp(start, destination, t));
        }

        // 端数で少し手前に止まらないよう、最後に目的地へぴったり合わせる
        body.MovePosition(destination);
        IsMoving = false;
    }

    /// <summary>依目前模式計算水平移動方向。</summary>
    /// <remarks>
    /// （日本語）いまのモードで、水平方向の動く向き（長さ1）を求める。向きが決まらなければゼロ。
    /// 部品の状態に頼らない静的な計算にしてあるので、再生せずにテストできる。
    /// </remarks>
    public static Vector3 CalculateMoveDirection(
        MovableBlockMoveMode mode,
        Quaternion blockRotation,
        MovableBlockPullSide side,
        Vector3 blockPosition,
        Vector3 playerPosition)
    {
        if (mode == MovableBlockMoveMode.TowardPlayer)
        {
            // 高さの差は無視して、水平方向だけで向きを取る
            Vector3 towardPlayer = playerPosition - blockPosition;
            towardPlayer.y = 0f;
            return towardPlayer.sqrMagnitude > 0.0001f ? towardPlayer.normalized : Vector3.zero;
        }

        // 柱の位置（ブロック基準の向き）を、ブロックの回転に合わせてワールドの向きに直す
        Vector3 localDirection;
        switch (side)
        {
            case MovableBlockPullSide.Back:
                localDirection = Vector3.back;
                break;
            case MovableBlockPullSide.Left:
                localDirection = Vector3.left;
                break;
            case MovableBlockPullSide.Right:
                localDirection = Vector3.right;
                break;
            default:
                localDirection = Vector3.forward;
                break;
        }

        Vector3 worldDirection = blockRotation * localDirection;
        worldDirection.y = 0f;
        return worldDirection.sqrMagnitude > 0.0001f ? worldDirection.normalized : Vector3.zero;
    }
}
