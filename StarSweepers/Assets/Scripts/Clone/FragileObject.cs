using UnityEngine;

/// <summary>
/// 強くぶつかると壊れるオブジェクト。
///
/// 重いものを上から落とすと壊れ、軽いものを乗せただけでは壊れない。
/// 「重い箱は壊れやすい板の上に置けない」という遊びを作るための仕掛け。
///
/// 壊れる強さは、ぶつかった瞬間の勢い（質量 × 速度）で決まる。
/// つまり**重いものほど、遅くても壊せる**。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FragileObject : MonoBehaviour
{
    [Header("壊れる条件")]
    [Tooltip("この強さ以上でぶつかると壊れる。小さくすると壊れやすくなる")]
    [SerializeField] private float breakImpulse = 12f;

    [Tooltip("置かれた直後は壊れないようにする時間（秒）。生成時のめり込みで即壊れるのを防ぐ")]
    [SerializeField] private float safeSeconds = 0.4f;

    [Header("壊れたときの見た目")]
    [Tooltip("壊れたときに、いくつの破片に分かれるか")]
    [SerializeField] private int pieceCount = 6;

    [Tooltip("破片が飛び散る強さ")]
    [SerializeField] private float scatterForce = 2.5f;

    [Tooltip("破片が消えるまでの時間（秒）")]
    [SerializeField] private float pieceLifeSeconds = 3f;

    private float spawnedTime;
    private bool isBroken;

    /// <summary>置かれた（有効になった）時刻を覚える。壊れない時間の起点になる。</summary>
    private void OnEnable()
    {
        spawnedTime = Time.time;
        isBroken = false;
    }

    /// <summary>ぶつかった勢いが基準を超えていたら壊す。</summary>
    private void OnCollisionEnter(Collision collision)
    {
        if (isBroken)
        {
            return;
        }

        // 置いた直後は判定しない。床にわずかにめり込んだだけで壊れてしまうため
        if (Time.time - spawnedTime < safeSeconds)
        {
            return;
        }

        // impulse は「ぶつかった瞬間にかかった力」。重いものほど、また速いものほど大きくなる
        if (collision.impulse.magnitude < breakImpulse)
        {
            return;
        }

        Break();
    }

    /// <summary>壊す。外から壊したいときにも呼べるように公開している。</summary>
    public void Break()
    {
        if (isBroken)
        {
            return;
        }

        isBroken = true;

        SpawnPieces();
        Destroy(gameObject);
    }

    /// <summary>
    /// 壊れた見た目として、小さな破片をいくつか飛ばす。
    /// 本物の破壊処理ではなく、「壊れた」と分かればよい簡易なもの。
    /// </summary>
    private void SpawnPieces()
    {
        Renderer sourceRenderer = GetComponentInChildren<Renderer>();
        Material pieceMaterial = sourceRenderer != null ? sourceRenderer.sharedMaterial : null;

        Vector3 center = transform.position;

        // 破片の大きさは、元の物の大きさの 8%（最低 8cm）
        float pieceSize = Mathf.Max(0.08f, transform.localScale.magnitude * 0.08f);

        for (int i = 0; i < pieceCount; i++)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = name + "_Piece";

            // 中心から 15cm 以内にばらけて出す
            piece.transform.position = center + Random.insideUnitSphere * 0.15f;
            piece.transform.rotation = Random.rotation;
            piece.transform.localScale = Vector3.one * pieceSize;

            if (pieceMaterial != null)
            {
                piece.GetComponent<Renderer>().sharedMaterial = pieceMaterial;
            }

            // 軽い（0.2kg）破片を、でたらめな方向へ弾く
            Rigidbody body = piece.AddComponent<Rigidbody>();
            body.mass = 0.2f;
            body.AddForce(Random.onUnitSphere * scatterForce, ForceMode.Impulse);

            Destroy(piece, pieceLifeSeconds);
        }
    }
}
