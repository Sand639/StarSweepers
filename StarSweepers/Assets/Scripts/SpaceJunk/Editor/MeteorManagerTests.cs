using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class MeteorManagerTests
{
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