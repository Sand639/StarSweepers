using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 隕石の管理役（<see cref="MeteorManager"/>）が、候補の落下範囲から**どの範囲を選び、どこに落とすか**を確かめるテスト。
///
/// `TryChoosePoint` は 0～1 の値で「何番目の範囲か」と「範囲の中のどこか」を決める、テスト用の入口。
/// 2つ目の引数（半径 2m）は古い呼び方との互換で残っているだけで、**今は使われていない**
/// （爆風の円が範囲からはみ出すかは見ない決まり。`隕石攻撃.md` 参照）。
/// </summary>
public class MeteorManagerTests
{
	/// <summary>テストの中で作った物。終わったら <see cref="TearDown"/> でまとめて消す。</summary>
	private readonly List<GameObject> created =
		new List<GameObject>();


	[TearDown]
	public void TearDown()
	{
		foreach (GameObject item in created)
		{
			if (item != null)
			{
				Object.DestroyImmediate(item);
			}
		}

		created.Clear();
	}


	/// <summary>候補の範囲が1つも無ければ → 落とす場所が無いので false になるはず。</summary>
	[Test]
	public void TryChoosePoint_ReturnsFalseWhenListIsEmpty()
	{
		bool result = MeteorManager.TryChoosePoint(
			new List<MeteorArea>(),
			2f,
			0f,
			0.5f,
			0.5f,
			out _,
			out _);

		Assert.That(result, Is.False);
	}


	/// <summary>
	/// 候補に非表示（無効）の範囲が混ざっていたら → それは飛ばし、有効な範囲の真ん中
	/// （高さは箱の上面 0.5m）が選ばれるはず。
	/// </summary>
	[Test]
	public void TryChoosePoint_SkipsInactiveAreas()
	{
		MeteorArea inactive =
			CreateArea(
				"Inactive",
				new Vector3(20f, 1f, 20f));

		inactive.gameObject.SetActive(false);


		MeteorArea valid =
			CreateArea(
				"Valid",
				new Vector3(10f, 1f, 10f));


		bool result = MeteorManager.TryChoosePoint(
			new List<MeteorArea>
			{
				inactive,
				valid
			},
			2f,
			0f,
			0.5f,
			0.5f,
			out MeteorArea chosen,
			out Vector3 point);


		Assert.That(result, Is.True);

		Assert.That(
			chosen,
			Is.SameAs(valid));

		Assert.That(
			point,
			Is.EqualTo(
				new Vector3(0f, 0.5f, 0f)));
	}


	/// <summary>
	/// 爆風の円（半径2m）より小さい 3m 四方の範囲でも → 外さずに選ばれるはず。
	/// 範囲は「爆心を決める場所」で、円が収まるかは見ない決まりのため。
	/// </summary>
	[Test]
	public void TryChoosePoint_AllowsSmallAreas()
	{
		MeteorArea small =
			CreateArea(
				"Small",
				new Vector3(3f, 1f, 3f));


		bool result = MeteorManager.TryChoosePoint(
			new List<MeteorArea>
			{
				small
			},
			2f,
			0f,
			0.5f,
			0.5f,
			out MeteorArea chosen,
			out Vector3 point);


		Assert.That(result, Is.True);

		Assert.That(
			chosen,
			Is.SameAs(small));

		Assert.That(
			point,
			Is.EqualTo(
				new Vector3(0f, 0.5f, 0f)));
	}


	/// <summary>
	/// 有効な範囲が2つあり、「何番目か」に 0.99 を渡したら → 後ろ側（2つ目、X=20m に置いた方）が選ばれるはず。
	/// </summary>
	[Test]
	public void TryChoosePoint_UsesNormalizedAreaValueAcrossValidAreas()
	{
		MeteorArea first =
			CreateArea(
				"First",
				new Vector3(10f, 1f, 10f));

		MeteorArea second =
			CreateArea(
				"Second",
				new Vector3(12f, 1f, 12f));

		second.transform.position =
			new Vector3(20f, 0f, 0f);


		bool result = MeteorManager.TryChoosePoint(
			new List<MeteorArea>
			{
				first,
				second
			},
			2f,
			0.99f,
			0.5f,
			0.5f,
			out MeteorArea chosen,
			out Vector3 point);


		Assert.That(result, Is.True);

		Assert.That(
			chosen,
			Is.SameAs(second));

		Assert.That(
			point.x,
			Is.EqualTo(20f)
				.Within(0.0001f));
	}


	/// <summary>
	/// 範囲を (15, 2, 30) に動かして、範囲の中の位置に (0.5, 0.5) を渡したら →
	/// 範囲の真ん中（横方向は X=15m・Z=30m）に落ちるはず。
	/// </summary>
	[Test]
	public void TryChoosePoint_CenterValuesReturnAreaCenter()
	{
		MeteorArea area =
			CreateArea(
				"CenterTest",
				new Vector3(10f, 1f, 10f));

		area.transform.position =
			new Vector3(15f, 2f, 30f);


		bool result = MeteorManager.TryChoosePoint(
			new List<MeteorArea>
			{
				area
			},
			2f,
			0f,
			0.5f,
			0.5f,
			out MeteorArea chosen,
			out Vector3 point);


		Assert.That(result, Is.True);

		Assert.That(
			chosen,
			Is.SameAs(area));

		Assert.That(
			point.x,
			Is.EqualTo(15f)
				.Within(0.0001f));

		Assert.That(
			point.z,
			Is.EqualTo(30f)
				.Within(0.0001f));
	}


	/// <summary>
	/// 原点に、指定の大きさのトリガー BoxCollider と <see cref="MeteorArea"/> を付けた物を作る。
	/// 後で消せるよう <see cref="created"/> に入れておく。
	/// </summary>
	private MeteorArea CreateArea(
		string objectName,
		Vector3 size)
	{
		GameObject root =
			new GameObject(objectName);

		created.Add(root);


		BoxCollider box =
			root.AddComponent<BoxCollider>();

		box.size = size;
		box.isTrigger = true;


		return root.AddComponent<MeteorArea>();
	}
}