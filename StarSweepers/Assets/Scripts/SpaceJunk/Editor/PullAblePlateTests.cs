using System.Collections;
using System.Linq;
using System.Reflection;
using Type = System.Type;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 引っ張れる移動床（<see cref="PullAblePlate"/>）と、その柱・場外のアンカーを確かめる EditMode のテスト。
/// 仕組みは `PullAblePlate.cs` の説明と `引っ張れる移動床の再設計案.md` を参照。
///
/// 大きく4種類に分かれる。
/// ・オンラインの作り … 通信用の部品・ServerRpc の設定・同期する値があるか（型を調べるだけ）
/// ・計算 … 誰を動かすか・どれだけ動いたか・柱の向き（static の関数を直接呼ぶ）
/// ・動き … 仮の床とプレイヤーを作り、引っ張る → 床と乗客が動く／壁の手前で止まる、など
/// ・素材 … 床とアンカーのプレハブ、STAGE_05 に置いた床の作りが決めたとおりか
///
/// ## EditMode ならではの前提
///
/// ・再生していないので、`AddComponent` しても Awake・OnEnable は呼ばれない。
///   必要な準備（Rigidbody の控えなど）は、テストの中でリフレクションを使って済ませている。
/// ・再生していないとき、床は時間をかけずに**その場で一瞬で動く**（`PullAblePlate.StartMove` の決まり）。
///   そのため、引っ張った直後に位置を比べられる。
/// ・`RegisterPassenger` を呼ぶと、床が「有効な床の一覧」にも入る。OnEnable が来なくてもアンカーが床を見つけられるのはこのため。
///
/// ※ 本体の private メンバーを、**名前の文字列で指定して**呼んでいる所が多い。
///   本体の名前を変えるとコンパイルは通るのにテストが壊れるので、変えるときはこのファイルも直すこと。
/// </summary>
public class PullAblePlateTests
{
    private const string PlatePrefabPath = "Assets/Prefabs/SpaceJunk/Gimmick/PullAblePlate.prefab";
    private const string AnchorPrefabPath = "Assets/Prefabs/SpaceJunk/Gimmick/PullAblePlateAnchor.prefab";
    private const string Stage05ScenePath = "Assets/Scenes/Prototype/SpaceJunk/Stage/STAGE_05.unity";

    /// <summary>床は通信に対応した部品（NetworkBehaviour）であるはず。オンラインで床の位置をホストが決めるため。</summary>
    [Test]
    public void PlateIsANetworkBehaviour()
    {
        Assert.That(typeof(NetworkBehaviour).IsAssignableFrom(typeof(PullAblePlate)), Is.True);
    }

    /// <summary>
    /// 床を付けると NetworkObject と Rigidbody も必ず付く（[RequireComponent] で指定されている）はず。
    /// </summary>
    [Test]
    public void PlateRequiresNetworkObjectAndRigidbody()
    {
        // [RequireComponent] は1つにつき最大3つの型を持てるので、全部を集めてから空きを除く
        Type[] requiredTypes = typeof(PullAblePlate)
            .GetCustomAttributes<RequireComponent>()
            .SelectMany(attribute => new[]
            {
                attribute.m_Type0,
                attribute.m_Type1,
                attribute.m_Type2
            })
            .Where(type => type != null)
            .ToArray();

        Assert.That(requiredTypes, Does.Contain(typeof(NetworkObject)));
        Assert.That(requiredTypes, Does.Contain(typeof(Rigidbody)));
    }

    /// <summary>
    /// 参加者がホストに「床を引っ張って」と頼む2つの ServerRpc は、床の持ち主でなくても呼べる
    /// （RequireOwnership = false）はず。床の持ち主はホストで、引っ張るのは別のプレイヤーだから。
    /// ※ メソッド名を文字列で指定しているので、本体の名前を変えるとこのテストが壊れる
    /// </summary>
    [TestCase("RequestPullServerRpc")]
    [TestCase("RequestPullTowardServerRpc")]
    public void PullRequestRpcAllowsNonOwnerClients(string methodName)
    {
        MethodInfo method = typeof(PullAblePlate).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null, $"{methodName} が存在する必要がある");
        ServerRpcAttribute attribute = method.GetCustomAttribute<ServerRpcAttribute>();
        Assert.That(attribute, Is.Not.Null, $"{methodName} は ServerRpc である必要がある");
        Assert.That(attribute.RequireOwnership, Is.False,
            "場外または別 Plate の Player も要求できるため、所有権を要求してはいけない");
    }

    /// <summary>
    /// 「動いている最中か」を全員のPCにそろえる値（NetworkVariable&lt;bool&gt;）を持っているはず。
    /// 参加者のPCでも、動いている最中の床の柱を引っ掛けられないようにするため。名前は問わない。
    /// </summary>
    [Test]
    public void MovingStateHasANetworkSynchronizedValue()
    {
        bool hasSynchronizedMovingState = typeof(PullAblePlate)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Any(field => field.FieldType == typeof(NetworkVariable<bool>));

        Assert.That(hasSynchronizedMovingState, Is.True);
    }

    /// <summary>
    /// このPCで乗客を動かしてよいかの決まり。並びは（床が通信中か, 乗客が通信中か, 乗客が自分のプレイヤーか → 動かすか）。
    /// 1人用・自分のプレイヤー・通信していない乗客なら動かし、**他の人のプレイヤーだけは動かさない**はず
    /// （その人のPCが動かし、位置は通信で届くので、二重に動かさないため）。
    /// ※ メソッド名を文字列で指定しているので、本体の名前を変えるとこのテストが壊れる
    /// </summary>
    [TestCase(false, false, false, true, TestName = "OfflinePassengerMoves")]
    [TestCase(true, true, true, true, TestName = "OnlineOwnedPassengerMoves")]
    [TestCase(true, true, false, false, TestName = "OnlineRemotePassengerDoesNotMove")]
    [TestCase(true, false, false, true, TestName = "OnlinePassengerWithoutSpawnedNetworkObjectMoves")]
    public void PassengerMovementUsesTheLocalOwnershipPolicy(
        bool plateIsOnline,
        bool passengerIsOnline,
        bool passengerIsOwner,
        bool expected)
    {
        MethodInfo method = typeof(PullAblePlate).GetMethod(
            "ShouldMovePassengerOnThisPeer",
            BindingFlags.Public | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        bool actual = (bool)method.Invoke(
            null,
            new object[] { plateIsOnline, passengerIsOnline, passengerIsOwner });
        Assert.That(actual, Is.EqualTo(expected));
    }

    /// <summary>
    /// オンラインで床の位置を初めて見たフレームは、前の位置が控えていない（false）ので →
    /// 前の位置がどれだけ離れていても、乗客を動かす量は 0 のはず（出現時のワープを乗客に渡さないため）。
    /// </summary>
    [Test]
    public void FirstObservedOnlinePositionProducesNoPassengerDelta()
    {
        MethodInfo method = typeof(PullAblePlate).GetMethod(
            "CalculateObservedDelta",
            BindingFlags.Public | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        Vector3 actual = (Vector3)method.Invoke(
            null,
            new object[]
            {
                false,
                new Vector3(-100f, 0f, -100f),
                new Vector3(10f, 0f, 20f)
            });

        Assert.That(actual, Is.EqualTo(Vector3.zero));
    }

    /// <summary>
    /// 前の位置を控えている2フレーム目以降は → 今の位置 − 前の位置（ここでは (3, 5, 6)）がそのまま乗客を動かす量になるはず。
    /// </summary>
    [Test]
    public void LaterObservedOnlinePositionProducesItsExactDelta()
    {
        MethodInfo method = typeof(PullAblePlate).GetMethod(
            "CalculateObservedDelta",
            BindingFlags.Public | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        Vector3 actual = (Vector3)method.Invoke(
            null,
            new object[]
            {
                true,
                new Vector3(2f, 3f, 4f),
                new Vector3(5f, 8f, 10f)
            });

        Assert.That(actual, Is.EqualTo(new Vector3(3f, 5f, 6f)));
    }

    /// <summary>
    /// 回していない床なら → 8方向の柱は、名前どおりの水平な向き（Front = 奥 +Z、Right = 右 +X、斜めはその間）で、長さ 1 のはず。
    /// </summary>
    [TestCase(PullAblePlateDirection.Front, 0f, 1f)]
    [TestCase(PullAblePlateDirection.FrontRight, 1f, 1f)]
    [TestCase(PullAblePlateDirection.Right, 1f, 0f)]
    [TestCase(PullAblePlateDirection.BackRight, 1f, -1f)]
    [TestCase(PullAblePlateDirection.Back, 0f, -1f)]
    [TestCase(PullAblePlateDirection.BackLeft, -1f, -1f)]
    [TestCase(PullAblePlateDirection.Left, -1f, 0f)]
    [TestCase(PullAblePlateDirection.FrontLeft, -1f, 1f)]
    public void CalculateDirectionReturnsTheNamedHorizontalDirection(
        PullAblePlateDirection direction,
        float expectedX,
        float expectedZ)
    {
        Vector3 expected = new Vector3(expectedX, 0f, expectedZ).normalized;

        Vector3 actual = PullAblePlate.CalculateDirection(direction, Quaternion.identity);

        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.0001f));
    }

    /// <summary>床を Y軸で 90度回して置いたら → Front の柱は、床と一緒に回って右（+X）へ引っ張るはず。</summary>
    [Test]
    public void CalculateDirectionRotatesWithThePlate()
    {
        Vector3 actual = PullAblePlate.CalculateDirection(
            PullAblePlateDirection.Front,
            Quaternion.Euler(0f, 90f, 0f));

        Assert.That(Vector3.Distance(actual, Vector3.right), Is.LessThan(0.0001f));
    }

    /// <summary>
    /// 床に乗っている人は、その床の柱を引っ掛けられず → 乗っていない人は引っ掛けられるはず
    /// （乗ったまま自分の床を引っ張れないようにする決まり）。
    /// </summary>
    [Test]
    public void PullPointRejectsPassengerButAllowsAnotherPlayer()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject passenger = CreatePlayer("Passenger", out CharacterController passengerController);
        GameObject outsider = CreatePlayer("Outsider", out _);
        GameObject pointObject = CreatePullPoint(plate, PullAblePlateDirection.Front, out PullAblePlatePullPoint point);

        try
        {
            plate.RegisterPassenger(passenger.transform, passengerController);

            Assert.That(point.CanBeHookedBy(passenger.transform), Is.False);
            Assert.That(point.CanBeHookedBy(outsider.transform), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(passenger);
            Object.DestroyImmediate(outsider);
        }
    }

    /// <summary>
    /// Front の柱を OFF にしたら → 柱の GameObject ごと非表示になり、フックでも引っ掛けられなくなるはず。
    /// </summary>
    [Test]
    public void DisablingDirectionHidesItsPullPointAndPreventsHooking()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject pointObject = CreatePullPoint(plate, PullAblePlateDirection.Front, out PullAblePlatePullPoint point);

        try
        {
            // 床が「どの柱を消すか」を知れるよう、プレハブと同じく床の項目に柱をつないでおく
            // ※ frontPullPoint は PullAblePlate の private の項目名。変えるとここが null で落ちる
            SerializedObject serializedPlate = new SerializedObject(plate);
            serializedPlate.FindProperty("frontPullPoint").objectReferenceValue = point;
            serializedPlate.ApplyModifiedPropertiesWithoutUndo();

            plate.SetPullPointActive(PullAblePlateDirection.Front, false);

            Assert.That(pointObject.activeSelf, Is.False);
            Assert.That(point.CanBeHooked, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
        }
    }

    /// <summary>
    /// 2人が乗った床を右へ 2m 引っ張ったら → 2人とも床と同じ 2m だけ右へ動くはず。
    /// 同じ人を2回登録しても、2倍（4m）動いたりはしない。
    /// </summary>
    [Test]
    public void ImmediatePullMovesEveryUniquePassengerByThePlateDelta()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject firstPlayer = CreatePlayer("FirstPlayer", out CharacterController firstController);
        GameObject secondPlayer = CreatePlayer("SecondPlayer", out CharacterController secondController);

        try
        {
            // 位置を変えたら、物理側（CharacterController.Move が使う位置）にもすぐ反映させる
            firstPlayer.transform.position = new Vector3(0f, 1f, 0f);
            secondPlayer.transform.position = new Vector3(0f, 1f, 1f);
            Physics.SyncTransforms();
            Vector3 firstStart = firstPlayer.transform.position;
            Vector3 secondStart = secondPlayer.transform.position;

            // 1回で 2m・時間をかけずに動かす（ConfigureImmediatePull と同じ設定）
            SerializedObject serializedPlate = new SerializedObject(plate);
            serializedPlate.FindProperty("moveDistance").floatValue = 2f;
            serializedPlate.FindProperty("moveDuration").floatValue = 0f;
            serializedPlate.ApplyModifiedPropertiesWithoutUndo();

            // 1人目はわざと2回登録する（乗客の判定は毎フレーム登録してくるため、重なっても1人ぶんであること）
            plate.RegisterPassenger(firstPlayer.transform, firstController);
            plate.RegisterPassenger(firstPlayer.transform, firstController);
            plate.RegisterPassenger(secondPlayer.transform, secondController);

            Assert.That(plate.Pull(PullAblePlateDirection.Right), Is.True);

            Vector3 expectedDelta = new Vector3(2f, 0f, 0f);
            Assert.That(Vector3.Distance(firstPlayer.transform.position, firstStart + expectedDelta), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(secondPlayer.transform.position, secondStart + expectedDelta), Is.LessThan(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(firstPlayer);
            Object.DestroyImmediate(secondPlayer);
        }
    }

    /// <summary>
    /// 乗客の CharacterController が無効（オンラインで他の人の体を見ているとき・操作が止まっているとき）なら →
    /// 床が動いても、その乗客は動かさないはず。
    /// </summary>
    [Test]
    public void ImmediatePullSkipsADisabledPassengerController()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject remotePlayer = CreatePlayer("RemotePlayer", out CharacterController controller);

        try
        {
            remotePlayer.transform.position = new Vector3(0f, 1f, 0f);
            Physics.SyncTransforms();
            Vector3 start = remotePlayer.transform.position;
            // 他の人のプレイヤーは、そのPCでは CharacterController を切って見た目だけ動かしている。その状態をまねる
            controller.enabled = false;

            SerializedObject serializedPlate = new SerializedObject(plate);
            serializedPlate.FindProperty("moveDistance").floatValue = 2f;
            serializedPlate.FindProperty("moveDuration").floatValue = 0f;
            serializedPlate.ApplyModifiedPropertiesWithoutUndo();

            plate.RegisterPassenger(remotePlayer.transform, controller);

            Assert.That(plate.Pull(PullAblePlateDirection.Right), Is.True);
            Assert.That(remotePlayer.transform.position, Is.EqualTo(start));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(remotePlayer);
        }
    }

    /// <summary>
    /// 場外のアンカーは、どれかの床に乗っている人だけが引っ掛けられるはず。
    /// 乗る前は引っ掛けられず → 乗ったら引っ掛けられ → 乗っていない別の人は引っ掛けられない。
    /// </summary>
    [Test]
    public void AnchorCanOnlyBeHookedByAPlayerRidingAPlate()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject passenger = CreatePlayer("Passenger", out CharacterController passengerController);
        GameObject outsider = CreatePlayer("Outsider", out _);
        GameObject anchorObject = CreateAnchor(new Vector3(10f, 0f, 0f), out PullAblePlateAnchor anchor);

        try
        {
            Assert.That(anchor.CanBeHookedBy(passenger.transform), Is.False);

            plate.RegisterPassenger(passenger.transform, passengerController);

            Assert.That(anchor.CanBeHookedBy(passenger.transform), Is.True);
            Assert.That(anchor.CanBeHookedBy(outsider.transform), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(passenger);
            Object.DestroyImmediate(outsider);
            Object.DestroyImmediate(anchorObject);
        }
    }

    /// <summary>
    /// 床に乗った人が、右前方の高い所（10, 4, 0）にあるアンカーを引っ張り切ったら →
    /// 床と乗客が、高さは無視してアンカーの方（右）へ 1回ぶん（初期値の 2m）動くはず。
    /// 移動の時間は設定していないが、再生していないので一瞬で動く。
    /// </summary>
    [Test]
    public void AnchorCompletionMovesPlateAndPassengerTowardAnchor()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject passenger = CreatePlayer("Passenger", out CharacterController controller);
        GameObject anchorObject = CreateAnchor(new Vector3(10f, 4f, 0f), out PullAblePlateAnchor anchor);

        try
        {
            passenger.transform.position = new Vector3(0f, 1f, 0f);
            Physics.SyncTransforms();
            Vector3 passengerStart = passenger.transform.position;
            plate.RegisterPassenger(passenger.transform, controller);

            // フックが触れた → 引っ張りのゲージがたまり切った、の順にフック側の呼び方をまねる
            anchor.SetHooked(true);
            anchor.CompletePull(new HookPullContext(passengerStart, 1f, passenger.transform));

            Vector3 expectedDelta = new Vector3(2f, 0f, 0f);
            Assert.That(Vector3.Distance(plateObject.transform.position, expectedDelta), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(passenger.transform.position, passengerStart + expectedDelta), Is.LessThan(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(passenger);
            Object.DestroyImmediate(anchorObject);
        }
    }

    /// <summary>
    /// アンカーを引っ掛けたあと、引っ張り切る前に床から降りたら →
    /// どの床を動かすか決まらないので、床は動かないはず。
    /// </summary>
    [Test]
    public void AnchorCompletionDoesNothingAfterPlayerLeavesThePlate()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject passenger = CreatePlayer("Passenger", out CharacterController controller);
        GameObject anchorObject = CreateAnchor(new Vector3(10f, 0f, 0f), out PullAblePlateAnchor anchor);

        try
        {
            plate.RegisterPassenger(passenger.transform, controller);
            Assert.That(anchor.CanBeHookedBy(passenger.transform), Is.True);
            anchor.SetHooked(true);
            plate.UnregisterPassenger(passenger.transform);

            anchor.CompletePull(new HookPullContext(passenger.transform.position, 1f, passenger.transform));

            Assert.That(plateObject.transform.position, Is.EqualTo(Vector3.zero));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(passenger);
            Object.DestroyImmediate(anchorObject);
        }
    }

    /// <summary>
    /// アンカーが床の真上（横方向の位置が同じ）にあるときは → 向かう向きが決まらないので、床は動かないはず。
    /// </summary>
    [Test]
    public void AnchorAtTheSameHorizontalPositionDoesNotMoveThePlate()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject passenger = CreatePlayer("Passenger", out CharacterController controller);
        GameObject anchorObject = CreateAnchor(new Vector3(0f, 10f, 0f), out PullAblePlateAnchor anchor);

        try
        {
            plate.RegisterPassenger(passenger.transform, controller);
            anchor.SetHooked(true);

            anchor.CompletePull(new HookPullContext(passenger.transform.position, 1f, passenger.transform));

            Assert.That(plateObject.transform.position, Is.EqualTo(Vector3.zero));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(passenger);
            Object.DestroyImmediate(anchorObject);
        }
    }

    /// <summary>
    /// 床（1m の箱）の右に 0.4m のすき間を空けて固定の箱があるとき、右へ 2m 引っ張ったら →
    /// 箱にめり込まず、その手前（x が 0.5 より小さい所）で止まるはず。
    /// </summary>
    [Test]
    public void ImmediatePullStopsBeforeAFixedCube()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        BoxCollider plateCollider = plateObject.AddComponent<BoxCollider>();
        GameObject fixedObject = new GameObject("FixedMap");
        BoxCollider fixedCollider = fixedObject.AddComponent<BoxCollider>();

        try
        {
            // どちらも 1m の箱なので、中心を 1.4m 離すと、あいだのすき間は 0.4m
            fixedObject.transform.position = new Vector3(1.4f, 0f, 0f);
            ConfigureImmediatePull(plate);
            Physics.SyncTransforms();

            Assert.That(plate.Pull(PullAblePlateDirection.Right), Is.True);

            // 「当たり判定を無視してすり抜けた」のではなく、本当に手前で止まったことを確かめる
            Assert.That(Physics.GetIgnoreCollision(plateCollider, fixedCollider), Is.False);
            Assert.That(AreOverlapping(plateCollider, fixedCollider), Is.False);
            Assert.That(plateObject.transform.position.x, Is.LessThan(0.5f));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(fixedObject);
        }
    }

    /// <summary>
    /// Right の柱を3回続けて引っ張ったら → 毎回フックが外れて次も引っ掛けられ、床は合計 6m（2m × 3）右へ動くはず。
    /// </summary>
    [Test]
    public void PullPointCanCompleteThreeConsecutivePulls()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        GameObject pointObject = CreatePullPoint(
            plate,
            PullAblePlateDirection.Right,
            out PullAblePlatePullPoint point);

        try
        {
            ConfigureImmediatePull(plate);

            for (int pullIndex = 0; pullIndex < 3; pullIndex++)
            {
                Assert.That(point.CanBeHooked, Is.True, $"拉扯 {pullIndex + 1} 次前應可勾中");
                point.SetHooked(true);
                point.CompletePull(new HookPullContext(Vector3.left, 1f));
                Assert.That(point.IsHooked, Is.False, $"拉扯 {pullIndex + 1} 次後應解除 Hook");
                Physics.SyncTransforms();
            }

            Assert.That(Vector3.Distance(plateObject.transform.position, new Vector3(6f, 0f, 0f)),
                Is.LessThan(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
        }
    }

    /// <summary>
    /// 時間をかけて動いている途中で床が無効にされたら → 「動いている最中」が解けるはず
    /// （解けないと、次に有効にしたとき二度と引っ張れなくなる）。
    /// ※ private の body・MoveTo・OnDisable を名前で指定して呼んでいる。本体の名前を変えるとこのテストが壊れる
    /// </summary>
    [Test]
    public void DisablingPlateDuringMoveClearsMovingState()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);

        try
        {
            // EditMode では Awake が呼ばれず、床の Rigidbody の控え（body）が空のままなので、ここで入れておく
            typeof(PullAblePlate).GetField(
                    "body",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(plate, plateObject.GetComponent<Rigidbody>());
            MethodInfo moveTo = typeof(PullAblePlate).GetMethod(
                "MoveTo",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(moveTo, Is.Not.Null);

            // 時間をかけて動くコルーチンを、StartCoroutine を使わず手で1歩だけ進める。
            // 最初の待ち（WaitForFixedUpdate）まで進むので、「動いている最中」になったところで止まる
            IEnumerator moveRoutine = (IEnumerator)moveTo.Invoke(
                plate,
                new object[] { new Vector3(2f, 0f, 0f) });
            Assert.That(moveRoutine.MoveNext(), Is.True);
            Assert.That(plate.IsMoving, Is.True);

            // EditMode では無効にしても OnDisable が来ないので、直接呼んで「無効にされた」をまねる
            typeof(PullAblePlate).GetMethod(
                    "OnDisable",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(plate, null);

            Assert.That(plate.IsMoving, Is.False,
                "移動中に物件が停用されても、再啟用後は再次拉扯できる必要がある");
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
        }
    }

    /// <summary>
    /// フックが掛かったままの柱が無効にされたら → 「掛かっている」が外れ、また引っ掛けられる状態に戻るはず。
    /// ※ 柱の private の OnDisable を名前で指定して呼んでいる（EditMode では自動で呼ばれないため）
    /// </summary>
    [Test]
    public void DisablingPlateClearsHookedPullPointState()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        CreatePullPoint(plate, PullAblePlateDirection.Right, out PullAblePlatePullPoint point);

        try
        {
            point.SetHooked(true);
            Assert.That(point.IsHooked, Is.True);

            typeof(PullAblePlatePullPoint).GetMethod(
                    "OnDisable",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(point, null);

            Assert.That(point.IsHooked, Is.False);
            Assert.That(point.CanBeHooked, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
        }
    }

    /// <summary>
    /// 柱にフックを掛けているプレイヤーのフック（HookController）が無効にされたら → 柱の「掛かっている」も外れるはず
    /// （外れないと、その柱を誰も引っ掛けられなくなる）。
    /// ※ HookController の private の attachedPullable・OnDisable を名前で指定している。名前を変えるとこのテストが壊れる
    /// </summary>
    [Test]
    public void DisablingHookControllerReleasesAttachedPullPoint()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        CreatePullPoint(plate, PullAblePlateDirection.Right, out PullAblePlatePullPoint point);
        // 非表示にしてから付けて、フック本来の準備（Awake・OnEnable で照準や入力を探す処理）が動かないようにする。
        // ここで見たいのは、OnDisable の片付けだけ
        GameObject playerObject = new GameObject("Player");
        playerObject.SetActive(false);
        HookController hook = playerObject.AddComponent<HookController>();

        try
        {
            // 「この柱にフックが掛かっている」状態を、柱とフックの両方に作る
            point.SetHooked(true);
            typeof(HookController).GetField(
                    "attachedPullable",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(hook, point);

            typeof(HookController).GetMethod(
                    "OnDisable",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(hook, null);

            Assert.That(point.IsHooked, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(playerObject);
        }
    }

    /// <summary>
    /// 固定の箱の手前で止まった床を、反対（左）へ引っ張ったら → 触れている箱に止められず、ほぼ 2m 離れられるはず
    /// （進む向きと反対を向いた面は「止まる相手」にしない決まり。でないと一度ぶつかった床が動けなくなる）。
    /// </summary>
    [Test]
    public void PlateCanMoveAwayAfterStoppingAtAFixedCube()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        BoxCollider plateCollider = plateObject.AddComponent<BoxCollider>();
        GameObject fixedObject = new GameObject("FixedMap");
        BoxCollider fixedCollider = fixedObject.AddComponent<BoxCollider>();

        try
        {
            fixedObject.transform.position = new Vector3(1.4f, 0f, 0f);
            ConfigureImmediatePull(plate);
            Physics.SyncTransforms();

            Assert.That(plate.Pull(PullAblePlateDirection.Right), Is.True);
            float stoppedX = plateObject.transform.position.x;
            Assert.That(AreOverlapping(plateCollider, fixedCollider), Is.False);

            Assert.That(plate.Pull(PullAblePlateDirection.Left), Is.True);

            Assert.That(plateObject.transform.position.x, Is.LessThan(stoppedX - 1.9f));
            Assert.That(AreOverlapping(plateCollider, fixedCollider), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(fixedObject);
        }
    }

    /// <summary>
    /// 床の右に 0.4m 空けて別の床があるとき、右へ引っ張ったら → 別の床にめり込まず、その手前で止まるはず。
    /// </summary>
    [Test]
    public void ImmediatePullStopsBeforeAnotherPlate()
    {
        GameObject plateObject = CreatePlate(out PullAblePlate plate);
        BoxCollider plateCollider = plateObject.AddComponent<BoxCollider>();
        GameObject otherPlateObject = CreatePlate(out _);
        BoxCollider otherPlateCollider = otherPlateObject.AddComponent<BoxCollider>();

        try
        {
            otherPlateObject.transform.position = new Vector3(1.4f, 0f, 0f);
            ConfigureImmediatePull(plate);
            Physics.SyncTransforms();

            Assert.That(plate.Pull(PullAblePlateDirection.Right), Is.True);

            Assert.That(Physics.GetIgnoreCollision(plateCollider, otherPlateCollider), Is.False);
            Assert.That(AreOverlapping(plateCollider, otherPlateCollider), Is.False);
            Assert.That(plateObject.transform.position.x, Is.LessThan(0.5f));
        }
        finally
        {
            Object.DestroyImmediate(plateObject);
            Object.DestroyImmediate(otherPlateObject);
        }
    }

    /// <summary>
    /// 床のプレハブが決めた作りになっているはず。
    /// ・ルート … 専用の PullAblePlate だけ（昔の MovableBlock は付いていない）。見た目・当たり判定は持たず、位置・回転・拡大は初期値
    /// ・Rigidbody … キネマティックで重力なし（スクリプトで動かすため）
    /// ・見た目 … 子の PlateBody（10m × 0.5m × 10m の箱。上面がルートの高さ 0 に来るよう 0.25m 下げてある）
    /// ・8方向の柱 … 名前どおりの向きで、どれも最初から有効なトリガー
    /// ・PassengerArea … 乗っている人を見つけるトリガー
    /// </summary>
    [Test]
    public void PlatePrefabUsesDedicatedComponentsAndEightActivePullPoints()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlatePrefabPath);

        Assert.That(prefab, Is.Not.Null);
        PullAblePlate plate = prefab.GetComponent<PullAblePlate>();
        Assert.That(plate, Is.Not.Null);
        Assert.That(prefab.GetComponent<MovableBlock>(), Is.Null);
        Assert.That(prefab.transform.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(prefab.transform.localRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));

        Rigidbody body = prefab.GetComponent<Rigidbody>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body.isKinematic, Is.True);
        Assert.That(body.useGravity, Is.False);

        Assert.That(prefab.GetComponent<MeshFilter>(), Is.Null);
        Assert.That(prefab.GetComponent<MeshRenderer>(), Is.Null);
        Assert.That(prefab.GetComponent<Collider>(), Is.Null);

        Transform plateBody = prefab.transform.Find("PlateBody");
        Assert.That(plateBody, Is.Not.Null);
        Assert.That(Vector3.Distance(plateBody.localPosition, new Vector3(0f, -0.25f, 0f)),
            Is.LessThan(0.0001f));
        Assert.That(Vector3.Distance(plateBody.localScale, new Vector3(10f, 0.5f, 10f)),
            Is.LessThan(0.0001f));

        MeshFilter plateMesh = plateBody.GetComponent<MeshFilter>();
        Assert.That(plateMesh, Is.Not.Null);
        Assert.That(plateMesh.sharedMesh, Is.Not.Null);
        Assert.That(plateMesh.sharedMesh.name, Is.EqualTo("Cube"));
        Assert.That(plateBody.GetComponent<MeshRenderer>(), Is.Not.Null);

        BoxCollider plateCollider = plateBody.GetComponent<BoxCollider>();
        Assert.That(plateCollider, Is.Not.Null);
        Assert.That(plateCollider.isTrigger, Is.False);

        AssertPlatePullPoint(prefab.transform, "Front", PullAblePlateDirection.Front);
        AssertPlatePullPoint(prefab.transform, "FrontRight", PullAblePlateDirection.FrontRight);
        AssertPlatePullPoint(prefab.transform, "Right", PullAblePlateDirection.Right);
        AssertPlatePullPoint(prefab.transform, "BackRight", PullAblePlateDirection.BackRight);
        AssertPlatePullPoint(prefab.transform, "Back", PullAblePlateDirection.Back);
        AssertPlatePullPoint(prefab.transform, "BackLeft", PullAblePlateDirection.BackLeft);
        AssertPlatePullPoint(prefab.transform, "Left", PullAblePlateDirection.Left);
        AssertPlatePullPoint(prefab.transform, "FrontLeft", PullAblePlateDirection.FrontLeft);

        Transform passengerArea = prefab.transform.Find("PassengerArea");
        Assert.That(passengerArea, Is.Not.Null);
        Collider passengerTrigger = passengerArea.GetComponent<Collider>();
        Assert.That(passengerTrigger, Is.Not.Null);
        Assert.That(passengerTrigger.isTrigger, Is.True);
        Assert.That(passengerArea.GetComponent<PullAblePlatePassengerArea>(), Is.Not.Null);

        // 8方向の ON/OFF の項目が、どれも ON のまま保存されているか。
        // ※ PullAblePlate の private の項目名。変えるとここが null で落ちる
        SerializedObject serializedPlate = new SerializedObject(plate);
        string[] activeProperties =
        {
            "frontActive",
            "frontRightActive",
            "rightActive",
            "backRightActive",
            "backActive",
            "backLeftActive",
            "leftActive",
            "frontLeftActive"
        };
        foreach (string propertyName in activeProperties)
        {
            Assert.That(serializedPlate.FindProperty(propertyName).boolValue, Is.True, propertyName);
        }
    }

    /// <summary>
    /// 床のプレハブの通信設定は、`PullAblePlateOnlineSetup` で入れたとおりのはず。
    /// 位置の持ち主はホスト（Server）で、配るのは位置だけ（回転・拡大は配らない）。
    /// </summary>
    [Test]
    public void PlatePrefabUsesServerAuthoritativePositionSyncOnly()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlatePrefabPath);

        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<NetworkObject>(), Is.Not.Null);
        NetworkTransform networkTransform = prefab.GetComponent<NetworkTransform>();
        Assert.That(networkTransform, Is.Not.Null);
        Assert.That(networkTransform.AuthorityMode, Is.EqualTo(NetworkTransform.AuthorityModes.Server));
        Assert.That(networkTransform.SyncPositionX, Is.True);
        Assert.That(networkTransform.SyncPositionY, Is.True);
        Assert.That(networkTransform.SyncPositionZ, Is.True);
        Assert.That(networkTransform.SyncRotAngleX, Is.False);
        Assert.That(networkTransform.SyncRotAngleY, Is.False);
        Assert.That(networkTransform.SyncRotAngleZ, Is.False);
        Assert.That(networkTransform.SyncScaleX, Is.False);
        Assert.That(networkTransform.SyncScaleY, Is.False);
        Assert.That(networkTransform.SyncScaleZ, Is.False);
    }

    /// <summary>
    /// STAGE_05 に置いた床（1つだけ）は、通信用の番号（GlobalObjectIdHash など）が 0 でなく、
    /// シーンの中のほかの通信オブジェクトと重なっていないはず。0 や重なりがあると、参加者のPCで床が取り違えられる。
    /// ※ 番号は外から見えないので、インスペクターと同じ読み方で名前を指定して読んでいる
    /// </summary>
    [Test]
    public void Stage05PlateHasUniqueNonZeroSceneNetworkIds()
    {
        Scene scene = EditorSceneManager.OpenScene(Stage05ScenePath, OpenSceneMode.Additive);
        try
        {
            // 開いた直後に「変更あり」になっていたら、番号が保存されておらず開いた拍子に作り直されたということ
            Assert.That(scene.isDirty, Is.False,
                "NetworkObject と配置 ID は STAGE_05 に保存済みである必要がある");
            PullAblePlate[] plates = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<PullAblePlate>(true))
                .ToArray();
            Assert.That(plates, Has.Length.EqualTo(1));

            NetworkObject plateNetworkObject = plates[0].GetComponent<NetworkObject>();
            Assert.That(plateNetworkObject, Is.Not.Null);
            SerializedObject serializedPlateNetworkObject = new SerializedObject(plateNetworkObject);
            uint globalId = (uint)serializedPlateNetworkObject
                .FindProperty("GlobalObjectIdHash").longValue;
            uint sourceId = (uint)serializedPlateNetworkObject
                .FindProperty("InScenePlacedSourceGlobalObjectIdHash").longValue;

            Assert.That(globalId, Is.Not.EqualTo(0u));
            Assert.That(sourceId, Is.Not.EqualTo(0u));

            NetworkObject[] duplicateIds = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<NetworkObject>(true))
                .Where(networkObject => networkObject != plateNetworkObject)
                .Where(networkObject =>
                {
                    SerializedProperty property = new SerializedObject(networkObject)
                        .FindProperty("GlobalObjectIdHash");
                    return property != null && (uint)property.longValue == globalId;
                })
                .ToArray();
            Assert.That(duplicateIds, Is.Empty);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// アンカーのプレハブは、目に見えて（MeshRenderer あり）、フックが触れたことだけ分かるトリガーを持っているはず。
    /// </summary>
    [Test]
    public void AnchorPrefabIsVisibleAndHookableThroughATrigger()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnchorPrefabPath);

        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<PullAblePlateAnchor>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<MeshRenderer>(), Is.Not.Null);
        Collider trigger = prefab.GetComponent<Collider>();
        Assert.That(trigger, Is.Not.Null);
        Assert.That(trigger.isTrigger, Is.True);
    }

    /// <summary>
    /// 原点に、テスト用の床（キネマティックの Rigidbody ＋ PullAblePlate）を作る。見た目も当たり判定も無い。
    /// 壁にぶつけるテストでは、呼んだ側で BoxCollider（1m の箱）を足す。
    /// </summary>
    private static GameObject CreatePlate(out PullAblePlate plate)
    {
        GameObject plateObject = new GameObject("PullAblePlate");
        Rigidbody body = plateObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        plate = plateObject.AddComponent<PullAblePlate>();
        return plateObject;
    }

    /// <summary>
    /// 1回で動く距離を 2m、かける時間を 0 秒（一瞬で動く）にする。
    /// ※ moveDistance・moveDuration は PullAblePlate の private の項目名。変えるとここが null で落ちる
    /// </summary>
    private static void ConfigureImmediatePull(PullAblePlate plate)
    {
        SerializedObject serializedPlate = new SerializedObject(plate);
        serializedPlate.FindProperty("moveDistance").floatValue = 2f;
        serializedPlate.FindProperty("moveDuration").floatValue = 0f;
        serializedPlate.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>2つの当たり判定が、いまの位置でめり込み合っているか。触れているだけ（すき間 0）なら false。</summary>
    private static bool AreOverlapping(Collider first, Collider second)
    {
        return Physics.ComputePenetration(
            first,
            first.transform.position,
            first.transform.rotation,
            second,
            second.transform.position,
            second.transform.rotation,
            out _,
            out _);
    }

    /// <summary>テスト用のプレイヤー（CharacterController だけを付けた物）を原点に作る。</summary>
    private static GameObject CreatePlayer(string name, out CharacterController controller)
    {
        GameObject player = new GameObject(name);
        controller = player.AddComponent<CharacterController>();
        return player;
    }

    /// <summary>
    /// 床の子に、指定の向きの柱（トリガー ＋ PullAblePlatePullPoint）を作る。床の子なので、床を消せば柱も消える。
    /// ※ direction は PullAblePlatePullPoint の private の項目名。変えるとここが null で落ちる
    /// </summary>
    private static GameObject CreatePullPoint(
        PullAblePlate plate,
        PullAblePlateDirection direction,
        out PullAblePlatePullPoint point)
    {
        GameObject pointObject = new GameObject(direction.ToString());
        pointObject.transform.SetParent(plate.transform, false);
        BoxCollider trigger = pointObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        point = pointObject.AddComponent<PullAblePlatePullPoint>();

        SerializedObject serializedPoint = new SerializedObject(point);
        serializedPoint.FindProperty("direction").enumValueIndex = (int)direction;
        serializedPoint.ApplyModifiedPropertiesWithoutUndo();
        return pointObject;
    }

    /// <summary>指定の位置に、場外のアンカー（トリガー ＋ PullAblePlateAnchor）を作る。</summary>
    private static GameObject CreateAnchor(Vector3 position, out PullAblePlateAnchor anchor)
    {
        GameObject anchorObject = new GameObject("PullAblePlateAnchor");
        anchorObject.transform.position = position;
        BoxCollider trigger = anchorObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        anchor = anchorObject.AddComponent<PullAblePlateAnchor>();
        return anchorObject;
    }

    /// <summary>
    /// プレハブの子の柱が、名前どおりにあり、最初から有効で、トリガーを持ち、名前と同じ向きになっているかを確かめる。
    /// </summary>
    private static void AssertPlatePullPoint(
        Transform root,
        string childName,
        PullAblePlateDirection expectedDirection)
    {
        Transform child = root.Find(childName);
        Assert.That(child, Is.Not.Null, $"{childName} 拉點不存在");
        Assert.That(child.gameObject.activeSelf, Is.True, $"{childName} 必須預設啟用");

        Collider trigger = child.GetComponent<Collider>();
        Assert.That(trigger, Is.Not.Null, $"{childName} 沒有 Collider");
        Assert.That(trigger.isTrigger, Is.True, $"{childName} 的 Collider 必須是 Trigger");

        PullAblePlatePullPoint pullPoint = child.GetComponent<PullAblePlatePullPoint>();
        Assert.That(pullPoint, Is.Not.Null, $"{childName} 沒有 PullAblePlatePullPoint");
        Assert.That(pullPoint.Direction, Is.EqualTo(expectedDirection));
    }
}
