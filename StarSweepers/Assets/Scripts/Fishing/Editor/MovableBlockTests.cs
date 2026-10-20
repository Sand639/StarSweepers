using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// **フックで引っ張ると動く大きなブロック（<see cref="MovableBlock"/>）の自動テスト。**（EditMode のテスト）
///
/// 動かし方：Unity のメニュー「Window > General > Test Runner」を開き、**EditMode** のタブで実行する。
/// 再生しなくても動く（シーンも開かない）。テストの中で一時的に作った物は、`finally` で必ず消している。
///
/// 確かめていること：
///   ・動く向きの決め方（プレイヤーの方向へ／引いた柱の向きへ）の計算が合っているか
///   ・フックが当たった当たり判定（柱）から、引っ張れる物が見つかるか
///   ・引っ張りが「完成」したときだけブロックが動くか
///   ・プレハブ `MovableBlock.prefab` に、4辺の柱が正しく付いているか
///
/// ⚠ テストの中で `FindProperty("moveMode")` のように、ブロックや柱の**インスペクターの項目名を文字列で**指定している。
/// `MovableBlock` / `MovableBlockPullPoint` のフィールドを改名すると、テストが NullReferenceException で失敗する。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public class MovableBlockTests
{
    /// <summary>
    /// 確かめるプレハブの場所。**プレハブを移したら、ここも直す**（直さないと最後のテストが失敗する）。
    /// </summary>
    private const string PrefabPath = "Assets/Scenes/Test/Sou/MovableBlock.prefab";

    /// <summary>
    /// 動く向きの決め方は2つ、柱の位置は4つだけで、名前も変わっていないか。
    /// （名前や数が変わると、インスペクターで選んでいた値がずれたり、ほかのテストの前提が崩れたりするため、見張っている）
    /// </summary>
    [Test]
    public void BlockEnumsContainOnlyTheTwoModesAndFourSides()
    {
        Assert.That(
            System.Enum.GetNames(typeof(MovableBlockMoveMode)),
            Is.EqualTo(new[] { "TowardPlayer", "FourDirections" }));
        Assert.That(
            System.Enum.GetNames(typeof(MovableBlockPullSide)),
            Is.EqualTo(new[] { "Front", "Back", "Left", "Right" }));
    }

    /// <summary>
    /// 「人によって引っ掛けてよいかが変わる物」（<see cref="IPlayerAwareHookPullable"/>）で、
    /// 物が「だめ」と答えたときだけフックが掛からないか。中身は下の <see cref="PlayerAwarePullableFake"/>（テスト用の偽物）。
    /// </summary>
    [Test]
    public void PlayerAwarePullableRejectsOnlyThePlayerItDisallows()
    {
        GameObject pullableObject = new GameObject("PlayerAwarePullable");
        GameObject player = new GameObject("Player");

        try
        {
            PlayerAwarePullableFake pullable = pullableObject.AddComponent<PlayerAwarePullableFake>();
            pullable.CanHook = false;

            Assert.That(HookController.CanHookPullable(pullable, player.transform), Is.False);

            pullable.CanHook = true;

            Assert.That(HookController.CanHookPullable(pullable, player.transform), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(pullableObject);
            Object.DestroyImmediate(player);
        }
    }

    /// <summary>
    /// ふつうのブロックの柱は「人による条件」を持たないので、ふだんの条件だけで、誰のフックでも掛かるか。
    /// </summary>
    [Test]
    public void NormalMovableBlockPullPointKeepsUsingGeneralHookCondition()
    {
        GameObject root = new GameObject("MovableBlock");
        GameObject pointObject = new GameObject("Front");
        GameObject player = new GameObject("Player");

        try
        {
            root.AddComponent<Rigidbody>().isKinematic = true;
            root.AddComponent<MovableBlock>();
            pointObject.transform.SetParent(root.transform, false);
            pointObject.AddComponent<BoxCollider>();
            MovableBlockPullPoint pullable = pointObject.AddComponent<MovableBlockPullPoint>();

            Assert.That(HookController.CanHookPullable(pullable, player.transform), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(player);
        }
    }

    /// <summary>
    /// 引っ張りの情報（<see cref="HookPullContext"/>）が、渡した「引っ張った人の体の根元」をそのまま持っているか（別の物にすり替わらないか）。
    /// </summary>
    [Test]
    public void HookPullContextKeepsTheExactPlayerRoot()
    {
        GameObject player = new GameObject("Player");

        try
        {
            HookPullContext context = new HookPullContext(Vector3.one, 0.5f, player.transform);

            Assert.That(context.PlayerRoot, Is.SameAs(player.transform));
        }
        finally
        {
            Object.DestroyImmediate(player);
        }
    }

    /// <summary>
    /// 「プレイヤーの方向へ動く」決め方で、**高さの差は無視して、水平な向きだけ**になるか。
    /// プレイヤーは (3, 7, 4) にいるので、高さ 7 を捨てた (3, 0, 4) を長さ1にした (0.6, 0, 0.8) が正解。
    /// </summary>
    [Test]
    public void TowardPlayerUsesHorizontalDirectionFromBlockToPlayer()
    {
        // 引数は順に：決め方・ブロックの向き・引いた柱・ブロックの位置・プレイヤーの位置
        Vector3 direction = MovableBlock.CalculateMoveDirection(
            MovableBlockMoveMode.TowardPlayer,
            Quaternion.identity,
            MovableBlockPullSide.Front,
            Vector3.zero,
            new Vector3(3f, 7f, 4f));

        Assert.That(Vector3.Distance(direction, new Vector3(0.6f, 0f, 0.8f)), Is.LessThan(0.0001f));
    }

    /// <summary>
    /// 「引いた柱の向きへ動く」決め方で、回っていないブロックなら Front＝+Z（北）、Back＝-Z、Left＝-X、Right＝+X になるか。
    /// プレイヤーの位置（-20, 5, -20）は、この決め方では**使われない**ことも一緒に確かめている。
    /// </summary>
    [TestCase(MovableBlockPullSide.Front, 0f, 0f, 1f)]
    [TestCase(MovableBlockPullSide.Back, 0f, 0f, -1f)]
    [TestCase(MovableBlockPullSide.Left, -1f, 0f, 0f)]
    [TestCase(MovableBlockPullSide.Right, 1f, 0f, 0f)]
    public void FourDirectionsUsesNamedLocalSide(
        MovableBlockPullSide side,
        float expectedX,
        float expectedY,
        float expectedZ)
    {
        Vector3 direction = MovableBlock.CalculateMoveDirection(
            MovableBlockMoveMode.FourDirections,
            Quaternion.identity,
            side,
            Vector3.zero,
            new Vector3(-20f, 5f, -20f));

        Assert.That(Vector3.Distance(direction, new Vector3(expectedX, expectedY, expectedZ)), Is.LessThan(0.0001f));
    }

    /// <summary>
    /// ブロックを右へ 90度回すと、Front の柱の向き（もとは +Z）も一緒に回って +X になるか。
    /// </summary>
    [Test]
    public void FourDirectionsRotatesWithBlock()
    {
        Vector3 direction = MovableBlock.CalculateMoveDirection(
            MovableBlockMoveMode.FourDirections,
            Quaternion.Euler(0f, 90f, 0f),
            MovableBlockPullSide.Front,
            Vector3.zero,
            Vector3.back);

        Assert.That(Vector3.Distance(direction, Vector3.right), Is.LessThan(0.0001f));
    }

    /// <summary>
    /// フックが掛かっただけではブロックは動かず、**引っ張りが完成したとき（CompletePull）に初めて** 2m 動くか。
    /// 動いたあとは、フックが外れた扱いに戻るか。
    /// </summary>
    [Test]
    public void PullPointCompletesPullOnlyWhenHookCallsCompletion()
    {
        GameObject root = new GameObject("MovableBlock");
        GameObject pointObject = new GameObject("Front");

        try
        {
            root.AddComponent<Rigidbody>().isKinematic = true;
            MovableBlock block = root.AddComponent<MovableBlock>();
            // 「柱の向きへ 2m、時間 0 秒（その場で一気に）動く」設定にする。
            // ⚠ "moveMode" などは MovableBlock のフィールド名（改名するとここが失敗する）
            SerializedObject serializedBlock = new SerializedObject(block);
            serializedBlock.FindProperty("moveMode").enumValueIndex = (int)MovableBlockMoveMode.FourDirections;
            serializedBlock.FindProperty("moveDistance").floatValue = 2f;
            serializedBlock.FindProperty("moveDuration").floatValue = 0f;
            serializedBlock.ApplyModifiedPropertiesWithoutUndo();

            pointObject.transform.SetParent(root.transform, false);
            pointObject.AddComponent<BoxCollider>();
            MovableBlockPullPoint point = pointObject.AddComponent<MovableBlockPullPoint>();
            SerializedObject serializedPoint = new SerializedObject(point);
            serializedPoint.FindProperty("side").enumValueIndex = (int)MovableBlockPullSide.Front;
            serializedPoint.ApplyModifiedPropertiesWithoutUndo();

            IHookPullable pullable = point;
            pullable.SetHooked(true);

            Assert.That(root.transform.position, Is.EqualTo(Vector3.zero));

            pullable.CompletePull(new HookPullContext(new Vector3(-20f, 0f, -20f), 0.5f));

            Assert.That(Vector3.Distance(root.transform.position, new Vector3(0f, 0f, 2f)), Is.LessThan(0.0001f));
            Assert.That(pullable.IsHooked, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// フックが柱の当たり判定に当たったとき、その柱（引っ張れる物）が見つかるか。
    /// </summary>
    [Test]
    public void HookFindsPullableFromTheColliderItHit()
    {
        GameObject root = new GameObject("MovableBlock");
        GameObject pointObject = new GameObject("Front");

        try
        {
            root.AddComponent<Rigidbody>().isKinematic = true;
            root.AddComponent<MovableBlock>();
            pointObject.transform.SetParent(root.transform, false);
            BoxCollider pointCollider = pointObject.AddComponent<BoxCollider>();
            MovableBlockPullPoint point = pointObject.AddComponent<MovableBlockPullPoint>();

            IHookPullable found = HookController.FindPullable(pointCollider);

            Assert.That(found, Is.SameAs(point));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// ゲージが確定したとき、ThrowController がブロックの柱に「引っ張り完成」を伝え、ブロックが動くか。
    /// 今度は Right の柱なので、+X へ 2m 動けば正解。
    /// </summary>
    [Test]
    public void ThrowControllerCallsPullableWhenPullIsCompleted()
    {
        GameObject root = new GameObject("MovableBlock");
        GameObject pointObject = new GameObject("Front");

        try
        {
            root.AddComponent<Rigidbody>().isKinematic = true;
            MovableBlock block = root.AddComponent<MovableBlock>();
            SerializedObject serializedBlock = new SerializedObject(block);
            serializedBlock.FindProperty("moveMode").enumValueIndex = (int)MovableBlockMoveMode.FourDirections;
            serializedBlock.FindProperty("moveDistance").floatValue = 2f;
            serializedBlock.FindProperty("moveDuration").floatValue = 0f;
            serializedBlock.ApplyModifiedPropertiesWithoutUndo();

            pointObject.transform.SetParent(root.transform, false);
            pointObject.AddComponent<BoxCollider>();
            MovableBlockPullPoint point = pointObject.AddComponent<MovableBlockPullPoint>();
            SerializedObject serializedPoint = new SerializedObject(point);
            serializedPoint.FindProperty("side").enumValueIndex = (int)MovableBlockPullSide.Right;
            serializedPoint.ApplyModifiedPropertiesWithoutUndo();

            IHookPullable pullable = point;
            pullable.SetHooked(true);
            Assert.That(root.transform.position, Is.EqualTo(Vector3.zero));

            ThrowController.CompletePullable(pullable, new Vector3(-10f, 0f, -10f), 0.75f);

            Assert.That(Vector3.Distance(root.transform.position, new Vector3(2f, 0f, 0f)), Is.LessThan(0.0001f));
            Assert.That(pullable.IsHooked, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// 実際のプレハブに、Front / Back / Left / Right という名前の柱が4つあり、それぞれ正しく設定されているか。
    /// </summary>
    [Test]
    public void MovableBlockPrefabHasFourNamedTriggerPullPoints()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<MovableBlock>(), Is.Not.Null);
        AssertPullPoint(prefab.transform, "Front", MovableBlockPullSide.Front);
        AssertPullPoint(prefab.transform, "Back", MovableBlockPullSide.Back);
        AssertPullPoint(prefab.transform, "Left", MovableBlockPullSide.Left);
        AssertPullPoint(prefab.transform, "Right", MovableBlockPullSide.Right);
    }

    /// <summary>
    /// <paramref name="childName"/> という名前の柱が、すり抜ける BoxCollider と MovableBlockPullPoint を持ち、
    /// 向きが <paramref name="expectedSide"/> になっているか。
    /// 失敗したときの文は中国語（「拉點不存在」＝柱が無い、「沒有」＝付いていない、「必須是 Trigger」＝すり抜ける判定にすること）。
    /// </summary>
    private static void AssertPullPoint(Transform root, string childName, MovableBlockPullSide expectedSide)
    {
        Transform child = root.Find(childName);
        Assert.That(child, Is.Not.Null, $"{childName} 拉點不存在");

        BoxCollider trigger = child.GetComponent<BoxCollider>();
        Assert.That(trigger, Is.Not.Null, $"{childName} 沒有 BoxCollider");
        Assert.That(trigger.isTrigger, Is.True, $"{childName} 的 Collider 必須是 Trigger");

        MovableBlockPullPoint pullPoint = child.GetComponent<MovableBlockPullPoint>();
        Assert.That(pullPoint, Is.Not.Null, $"{childName} 沒有 MovableBlockPullPoint");

        SerializedObject serializedPoint = new SerializedObject(pullPoint);
        Assert.That(serializedPoint.FindProperty("side").enumValueIndex, Is.EqualTo((int)expectedSide));
    }
}

/// <summary>
/// **テスト専用の偽物。** 「人によって引っ掛けてよいか」の答えを、<see cref="CanHook"/> で自由に切り替えられる。
/// 本物の物件を用意しなくても、<see cref="HookController.CanHookPullable"/> の判断だけを確かめるために使う。
/// Editor フォルダにあるので、ゲームのビルドには含まれない。
/// </summary>
public sealed class PlayerAwarePullableFake : MonoBehaviour, IHookPullable, IPlayerAwareHookPullable
{
    public bool CanHook { get; set; }
    public Component HookComponent => this;
    public Vector3 HookAnchorPoint => transform.position;
    public bool IsHooked { get; private set; }
    public bool CanBeHooked => true;

    public bool CanBeHookedBy(Transform playerRoot)
    {
        return CanHook;
    }

    public void SetHooked(bool hooked)
    {
        IsHooked = hooked;
    }

    public void CompletePull(HookPullContext context)
    {
        IsHooked = false;
    }
}
