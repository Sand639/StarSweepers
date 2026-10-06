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

	[Min(0f)]
	[SerializeField]
	private float playerKnockbackSpeed = 12f;

	[Min(0f)]
	[SerializeField]
	private float playerKnockbackLift = 6f;

	[Header("プレイヤーのスタン")]

	[Tooltip("隕石に当たったプレイヤーが操作できなくなる秒数。0ならスタンさせない")]
	[Min(0f)]
	[SerializeField]
	private float playerStunSeconds = 2f;


	[Header("素材・爆弾を飛ばす強さ")]

	[Min(0f)]
	[SerializeField]
	private float objectKnockbackSpeed = 14f;

	[Min(0f)]
	[SerializeField]
	private float objectKnockbackLift = 7f;


	private readonly List<MeteorStrikeEvent> activeStrikes =
		new List<MeteorStrikeEvent>();

	private float spawnTimer;
	private bool warnedMissingSetup;


	private const float DuplicatePositionDistance = 0.01f;
	private const int PositionRetryCount = 20;


	private void OnEnable()
	{
		spawnTimer = Mathf.Max(0f, intervalSeconds);
	}


	private void OnDestroy()
	{
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
		if (!CanRunOnThisPC())
		{
			return;
		}

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
