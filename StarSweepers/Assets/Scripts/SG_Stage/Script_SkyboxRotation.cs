using UnityEngine;

public class RotateSkybox : MonoBehaviour
{
    public float rotationSpeed = 0.1f;

    private float rotation;

    void Update()
    {
        rotation += rotationSpeed * Time.deltaTime;
        rotation %= 360f;

        RenderSettings.skybox.SetFloat("_Rotation", rotation);
    }
}