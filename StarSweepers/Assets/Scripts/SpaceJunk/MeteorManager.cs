using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 隕石攻撃の生成間隔と落下場所を管理する。
///
/// intervalSecondsごとに新しい攻撃ラウンドを開始する。
/// 前のラウンドの隕石がまだ予告中・落下中でも、
/// 次のラウンドを開始する。
///
/// 1ラウンドでは、現在有効なMeteorAreaの中から
/// meteorsPerRound個をランダムに選ぶ。
///
/// 同じMeteorAreaは1ラウンド内で重複しない。
///
/// オンラインではServer / Hostだけが生成処理を行う。
///
/// シーンに1つ置く（`Tools > StarSweepers > Space Junk > Meteor > Add To Current Scene` が作る）。
/// 生成した1発ごとの処理（予告・落下・爆風）は <see cref="MeteorStrikeEvent"/> に任せる。
/// ラウンドが動いている間（<see cref="SpaceJunkRound.PlayAllowed"/>）だけ数える。
/// 機能の説明は `Documents/機能ドキュメント/隕石攻撃.md`。
/// </summary>
public class MeteorManager : MonoBehaviour
{
	[Header("生成")]

	[Tooltip("次の隕石攻撃ラウンドを開始するまでの間隔（秒）。前の隕石の終了は待たない")]
	[Min(0f)]
	[SerializeField]
	private float intervalSeconds = 3f;

	[Tooltip("1ラウンドで生成する隕石の数")]
	[Min(1)]
	[SerializeField]
	private int meteorsPerRound = 1;

	[Tooltip("地面予告を出してから着弾するまでの秒数")]
	[Min(0f)]
	[SerializeField]
	private float warningSeconds = 5f;

	[Tooltip("予告時間のうち、隕石が上空から落ちてくる秒数")]
	[Min(0f)]
	[SerializeField]
	private float fallSeconds = 1f;

	[Tooltip("1回分の隕石攻撃プレハブ")]
	[SerializeField]
	private MeteorStrikeEvent strikePrefab;

	[Tooltip("隕石を落としてよい範囲")]
	[SerializeField]
	private List<MeteorArea> activeAreas = new List<MeteorArea>();


	[Header("落下位置")]

	[Tooltip("ONの場合、選ばれたMeteor Areaの中心に隕石を落とす")]
	[SerializeField]
	private bool spawnAtAreaCenter = false;


	[Header("爆風の大きさ")]

	[Tooltip("地面予告、当たり判定、爆風の半径（m）")]
	[Min(0.01f)]
	[SerializeField]
	private float impactRadius = 4f;


	[Header("プレイヤーを飛ばす強さ")]

	[Tooltip("プレイヤーを爆心の外側へ飛ばす速さ（m/s）")]
	[Min(0f)]
	[SerializeField]
	private float playerKnockbackSpeed = 12f;

	[Tooltip("プレイヤーを上へ飛ばす速さ（m/s）")]
	[Min(0f)]
	[SerializeField]
	private float playerKnockbackLift = 6f;

	[Header("プレイヤーのスタン")]

	[Tooltip("隕石に当たったプレイヤーが操作できなくなる秒数。0ならスタンさせない")]
	[Min(0f)]
	[SerializeField]
	private float playerStunSeconds = 2f;


	[Header("素材・爆弾を飛ばす強さ")]

	[Tooltip("素材・爆弾を爆心の外側へ飛ばす速さ（m/s）")]
	[Min(0f)]
	[SerializeField]
	private float objectKnockbackSpeed = 14f;

	[Tooltip("素材・爆弾を上へ飛ばす速さ（m/s）")]
	[Min(0f)]
	[SerializeField]
	private float objectKnockbackLift = 7f;


	/// <summary>まだ終わっていない（予告中・落下中・爆発の見た目が残っている）隕石。終わったら自分で抜ける。</summary>
	private readonly List<MeteorStrikeEvent> activeStrikes =
		new List<MeteorStrikeEvent>();

	/// <summary>次のラウンドを始めるまでの残り秒数。</summary>
	private float spawnTimer;

	/// <summary>設定不足の警告をもう出したか（毎フレーム出してログを埋めないため）。</summary>
	private bool warnedMissingSetup;


	/// <summary>同じラウンドの落下点どうしが、これより近ければ「同じ場所」とみなしてやり直す（m）。</summary>
	private const float DuplicatePositionDistance = 0.01f;

	/// <summary>ランダムな落下点が他とかぶったときに、選び直す回数の上限。</summary>
	private const int PositionRetryCount = 20;


	private void OnEnable()
	{
		// 始まってすぐには落とさず、まず1回ぶんの間隔を待つ
		spawnTimer = Mathf.Max(0f, intervalSeconds);
	}


	private void OnDestroy()
	{
		// 残っている隕石からの「終わった」の知らせを、消えた自分が受け取らないようにする
		for (int i = 0; i < activeStrikes.Count; i++)
		{
			MeteorStrikeEvent strike = activeStrikes[i];

			if (strike != null)
			{
				strike.ServerFinished -= OnStrikeFinished;
			}
		}

		activeStrikes.Clear();
	}


	private void Update()
	{
		// 数えるのはホスト（または1人用）だけ。参加者のPCは、ホストが出した隕石を見るだけ
		if (!CanRunOnThisPC())
		{
			return;
		}

		// ラウンドの前後（始まりの演出中・結果が出たあと）は落とさない
		if (!SpaceJunkRound.PlayAllowed)
		{
			return;
		}

		spawnTimer -= Time.deltaTime;

		if (spawnTimer > 0f)
		{
			return;
		}

		if (TrySpawnRound())
		{
			spawnTimer = Mathf.Max(0f, intervalSeconds);
		}
		else
		{
			// 出せなかった（Area が無い等）ときは、1秒後にもう一度試す
			spawnTimer = 1f;
		}
	}


	/// <summary>
	/// 1ラウンド分の隕石を生成する。
	/// </summary>
	private bool TrySpawnRound()
	{
		if (strikePrefab == null)
		{
			WarnMissingSetup(
				"Meteor Strike Event Prefab が入っていません。");

			return false;
		}

		List<MeteorArea> availableAreas =
			GetAvailableAreas();

		if (availableAreas.Count == 0)
		{
			WarnMissingSetup(
				"有効なMeteor Areaがありません。");

			return false;
		}

		warnedMissingSetup = false;


		// 1ラウンドで生成する数。
		// 有効Area数を超えないようにする。
		int spawnCount = Mathf.Min(
			Mathf.Max(1, meteorsPerRound),
			availableAreas.Count);


		// Areaの順番をシャッフルする。
		ShuffleAreas(availableAreas);


		List<Vector3> usedPositions =
			new List<Vector3>();

		int spawnedCount = 0;


		for (int i = 0; i < spawnCount; i++)
		{
			MeteorArea area = availableAreas[i];

			if (!TryGetUniquePoint(
					area,
					usedPositions,
					out Vector3 point))
			{
				Debug.LogWarning(
					$"[METEOR] {area.name} で落下位置を取得できませんでした。",
					area);

				continue;
			}

			if (SpawnStrike(area, point))
			{
				usedPositions.Add(point);
				spawnedCount++;
			}
		}

		return spawnedCount > 0;
	}


	/// <summary>
	/// 有効なMeteorAreaを取得する。
	///
	/// 同じAreaがInspectorに複数登録されていても、
	/// 1回だけ候補に入れる。
	/// </summary>
	private List<MeteorArea> GetAvailableAreas()
	{
		List<MeteorArea> result =
			new List<MeteorArea>();

		if (activeAreas == null)
		{
			return result;
		}

		HashSet<MeteorArea> alreadyAdded =
			new HashSet<MeteorArea>();


		for (int i = 0; i < activeAreas.Count; i++)
		{
			MeteorArea area = activeAreas[i];

			if (area == null)
			{
				continue;
			}

			if (!area.IsAvailable)
			{
				continue;
			}

			if (!alreadyAdded.Add(area))
			{
				continue;
			}

			// 中心（0.5, 0.5）すら取れない Area（大きさが 0 など）は候補から外す
			if (!area.TryGetPoint(
					0f,
					0.5f,
					0.5f,
					out _))
			{
				continue;
			}

			result.Add(area);
		}

		return result;
	}


	/// <summary>
	/// Fisher-Yates shuffle。
	/// 同じラウンド内でAreaが重複しないように、
	/// 候補リストをランダム順にする。
	/// </summary>
	private static void ShuffleAreas(
		List<MeteorArea> areas)
	{
		for (int i = areas.Count - 1; i > 0; i--)
		{
			int randomIndex =
				Random.Range(0, i + 1);

			MeteorArea temp =
				areas[i];

			areas[i] =
				areas[randomIndex];

			areas[randomIndex] =
				temp;
		}
	}


	/// <summary>
	/// Area内から落下位置を取得する。
	///
	/// 半径は 0 で渡している。つまり **Area は「爆心を決める範囲」で、爆風の円全体を Area の中に収める制限は
	/// かけていない**（STAGE_03 の設置でそう決めた。`隕石攻撃.md` 参照）。円ごと収めたくなったら impactRadius を渡す。
	/// </summary>
	private bool TryGetUniquePoint(
		MeteorArea area,
		IReadOnlyList<Vector3> usedPositions,
		out Vector3 point)
	{
		point = default;

		if (area == null)
		{
			return false;
		}


		// 中心固定。
		if (spawnAtAreaCenter)
		{
			if (!area.TryGetPoint(
					0f,
					0.5f,
					0.5f,
					out point))
			{
				return false;
			}

			return !IsPositionAlreadyUsed(
				point,
				usedPositions);
		}


		// ランダム位置。
		for (int i = 0; i < PositionRetryCount; i++)
		{
			if (!area.TryGetPoint(
					0f,
					Random.value,
					Random.value,
					out Vector3 candidate))
			{
				continue;
			}

			if (IsPositionAlreadyUsed(
					candidate,
					usedPositions))
			{
				continue;
			}

			point = candidate;
			return true;
		}

		return false;
	}


	/// <summary>
	/// 同じラウンドですでに使った位置か確認する。
	/// </summary>
	private static bool IsPositionAlreadyUsed(
		Vector3 point,
		IReadOnlyList<Vector3> usedPositions)
	{
		if (usedPositions == null)
		{
			return false;
		}

		float minDistanceSquared =
			DuplicatePositionDistance *
			DuplicatePositionDistance;


		for (int i = 0; i < usedPositions.Count; i++)
		{
			Vector3 difference =
				point - usedPositions[i];

			if (difference.sqrMagnitude <=
				minDistanceSquared)
			{
				return true;
			}
		}

		return false;
	}


	/// <summary>
	/// 隕石を1つ生成する。
	///
	/// 順番が大事：**先に Spawn して全員の画面に出し、そのあと <see cref="MeteorStrikeEvent.InitializeServer"/> で
	/// 条件（着弾の時刻など）を入れる。** 条件は NetworkVariable なので、Spawn 後に入れれば全員へ配られる。
	/// 通信していない（1人用）ときは Spawn を飛ばして、そのまま条件を入れる。
	/// </summary>
	private bool SpawnStrike(
		MeteorArea area,
		Vector3 point)
	{
		MeteorStrikeEvent strike =
			Instantiate(
				strikePrefab,
				point,
				area.transform.rotation);

		activeStrikes.Add(strike);

		strike.ServerFinished +=
			OnStrikeFinished;


		NetworkManager network =
			NetworkManager.Singleton;

		if (network != null &&
			network.IsListening)
		{
			NetworkObject networkObject =
				strike.GetComponent<NetworkObject>();

			if (networkObject == null)
			{
				Debug.LogError(
					"[METEOR] MeteorStrikeEventにNetworkObjectがありません。",
					strike);

				strike.ServerFinished -=
					OnStrikeFinished;

				activeStrikes.Remove(strike);

				Destroy(strike.gameObject);

				return false;
			}

			networkObject.Spawn(true);
		}


		strike.InitializeServer(
			warningSeconds,
			fallSeconds,
			impactRadius,
			playerKnockbackSpeed,
			playerKnockbackLift,
			playerStunSeconds,
			objectKnockbackSpeed,
			objectKnockbackLift);

		return true;
	}


	/// <summary>隕石が終わった（ホストで消える直前）ときに呼ばれ、一覧から外す。</summary>
	private void OnStrikeFinished(
		MeteorStrikeEvent strike)
	{
		if (strike == null)
		{
			return;
		}

		strike.ServerFinished -=
			OnStrikeFinished;

		activeStrikes.Remove(strike);
	}


	/// <summary>設定不足の警告を1回だけ出す（直ったら <see cref="warnedMissingSetup"/> を戻して、また出せるようにする）。</summary>
	private void WarnMissingSetup(
		string message)
	{
		if (warnedMissingSetup)
		{
			return;
		}

		warnedMissingSetup = true;

		Debug.LogWarning(
			$"[METEOR] {message}",
			this);
	}


	/// <summary>このPCが隕石を出す役目か。1人用なら自分、通信中ならホストだけ。</summary>
	private static bool CanRunOnThisPC()
	{
		NetworkManager network =
			NetworkManager.Singleton;

		return network == null
			   || !network.IsListening
			   || network.IsServer;
	}


	// =========================================================
	// TryChoosePoint
	// Test互換用
	// =========================================================

	/// <summary>
	/// 候補の Area から1つ選んで落下点を返す（0〜1 の値で決める。テストから使う）。
	/// <paramref name="radius"/> は古い呼び方との互換のために残してあるだけで、**使っていない**。
	/// </summary>
	public static bool TryChoosePoint(
		IReadOnlyList<MeteorArea> areas,
		float radius,
		float normalizedArea,
		float normalizedX,
		float normalizedZ,
		out MeteorArea chosenArea,
		out Vector3 point)
	{
		return TryChoosePoint(
			areas,
			normalizedArea,
			normalizedX,
			normalizedZ,
			out chosenArea,
			out point);
	}


	/// <summary>
	/// 候補の Area から1つ選んで落下点を返す。
	/// <paramref name="normalizedArea"/>（0〜1）で「使える Area の何番目か」を、
	/// <paramref name="normalizedX"/>・<paramref name="normalizedZ"/>（0〜1）で Area 内のどこかを決める。
	/// 実際の試合では使っておらず（<see cref="TrySpawnRound"/> が別の決め方をする）、テストから呼ぶためのもの。
	/// </summary>
	public static bool TryChoosePoint(
		IReadOnlyList<MeteorArea> areas,
		float normalizedArea,
		float normalizedX,
		float normalizedZ,
		out MeteorArea chosenArea,
		out Vector3 point)
	{
		chosenArea = null;
		point = default;

		if (areas == null ||
			areas.Count == 0)
		{
			return false;
		}

		List<MeteorArea> candidates =
			new List<MeteorArea>();


		for (int i = 0; i < areas.Count; i++)
		{
			MeteorArea area = areas[i];

			if (area == null)
			{
				continue;
			}

			if (!area.IsAvailable)
			{
				continue;
			}

			if (!area.TryGetPoint(
					0f,
					0.5f,
					0.5f,
					out _))
			{
				continue;
			}

			candidates.Add(area);
		}


		if (candidates.Count == 0)
		{
			return false;
		}


		// 0〜1 を候補の番号に直す（1.0 がちょうど最後の番号を越えないように Min で抑える）
		int index = Mathf.Min(
			candidates.Count - 1,
			Mathf.FloorToInt(
				Mathf.Clamp01(normalizedArea) *
				candidates.Count));


		chosenArea = candidates[index];


		return chosenArea.TryGetPoint(
			0f,
			normalizedX,
			normalizedZ,
			out point);
	}
}
