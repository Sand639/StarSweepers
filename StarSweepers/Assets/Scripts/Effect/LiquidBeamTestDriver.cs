using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **液体のビームの検証シーン用**の操作。
///
/// | キー | すること |
/// | --- | --- |
/// | F（長押し） | 溜める。**離すと撃つ。長く押すほど遠くまで届く** |
/// | F（流れが出ている間） | 止める（出しっぱなしのとき用） |
/// | C | **先端に物資がついた想定。**先端から根元へ、次の色に染まっていく |
/// | R | 最初の色に戻す |
/// | H | 出しっぱなし（Stop を呼ぶまで消えない）の切り替え |
/// | 2 | キャラクターを円を描いて動かす／止める（しぶきが置いていかれるかの確認） |
/// </summary>
public class LiquidBeamTestDriver : MonoBehaviour
{
    [SerializeField] private LiquidBeam beam;
    [SerializeField] private Transform character;

    [Tooltip("はじめから出しっぱなしにしておくか（色の変わり方を見やすくするため）")]
    [SerializeField] private bool holdUntilStopped = true;

    [Tooltip("C を押すたびに、この順で色を変える（物資の種類ごとの色のつもり）")]
    [SerializeField] private Color[] cargoColors =
    {
        new Color(1f, 0.55f, 0.1f, 1f),
        new Color(0.7f, 0.25f, 1f, 1f),
        new Color(0.3f, 1f, 0.35f, 1f),
        new Color(1f, 0.2f, 0.3f, 1f),
    };

    [SerializeField] private float moveRadius = 3f;
    [SerializeField] private float moveSpeed = 0.6f;

    private int colorIndex;
    private bool moving;
    private float moveAngle;

    private void Start()
    {
        if (beam != null)
        {
            beam.HoldUntilStopped = holdUntilStopped;
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return;
        }

        if (beam != null)
        {
            if (keyboard.fKey.wasPressedThisFrame)
            {
                if (beam.IsFiring)
                {
                    beam.Stop();
                }
                else
                {
                    beam.BeginCharge();
                }
            }

            if (keyboard.fKey.wasReleasedThisFrame)
            {
                beam.Release();
            }

            if (keyboard.cKey.wasPressedThisFrame && cargoColors.Length > 0)
            {
                beam.ChangeColorFromTip(cargoColors[colorIndex]);
                colorIndex = (colorIndex + 1) % cargoColors.Length;
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                beam.ResetColor();
                colorIndex = 0;
            }

            if (keyboard.hKey.wasPressedThisFrame)
            {
                beam.HoldUntilStopped = !beam.HoldUntilStopped;
            }
        }

        if (keyboard.digit2Key.wasPressedThisFrame)
        {
            moving = !moving;
        }

        if (moving && character != null)
        {
            moveAngle += moveSpeed * Time.deltaTime;
            character.position = new Vector3(Mathf.Cos(moveAngle) * moveRadius, 0f, Mathf.Sin(moveAngle) * moveRadius);
            character.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(moveAngle), 0f, Mathf.Cos(moveAngle)));
        }
    }

    private void OnGUI()
    {
        string hold = beam != null && beam.HoldUntilStopped ? "オン（F でとめる）" : "オフ（少しで消える）";

        GUI.Label(new Rect(16, 16, 700, 120),
            "F（長押し）：溜める → 離すと撃つ（長く押すほど遠くへ）\n" +
            "C：先端に物資がついた想定（先端から色が変わる）　R：最初の色に戻す\n" +
            $"H：出しっぱなし … {hold}\n" +
            "2：キャラクターを動かす／止める");

        if (beam != null && beam.IsCharging)
        {
            GUI.Label(new Rect(16, 110, 600, 24),
                $"溜め {beam.Charge01 * 100f:0}%　→　{beam.CurrentLength:0.0} m");
        }
    }
}
