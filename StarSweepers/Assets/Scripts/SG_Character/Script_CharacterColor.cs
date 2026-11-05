
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class CharacterColorController : MonoBehaviour
{
    public enum ColorMode
    {
        Default,
        Custom
    }

    [Header("Character Material")]
    public Renderer characterRenderer;

    [Tooltip("Material slot containing M_Body")]
    [Min(0)]
    public int bodyMaterialIndex = 0;

    [Tooltip("Reference name of Suit Color in Shader Graph")]
    public string colorProperty = "_SuitColor";

    [Header("Color Settings")]
    public ColorMode colorMode = ColorMode.Default;

    [HideInInspector]
    public Color customColor = new Color(
        0.93f, 0.54f, 0.24f, 1f
    );

    private MaterialPropertyBlock propertyBlock;

    private void OnEnable()
    {
        ApplyColor();
    }

    public void SetMode(ColorMode mode)
    {
        colorMode = mode;
        ApplyColor();
    }

    public void SetCustomColor(Color color)
    {
        customColor = color;
        colorMode = ColorMode.Custom;
        ApplyColor();
    }

    public void ResetToDefault()
    {
        colorMode = ColorMode.Default;
        ApplyColor();
    }

    public void ApplyColor()
    {
        if (characterRenderer == null)
            return;

        Material[] materials =
            characterRenderer.sharedMaterials;

        if (bodyMaterialIndex < 0 ||
            bodyMaterialIndex >= materials.Length)
            return;

        Material bodyMaterial =
            materials[bodyMaterialIndex];

        if (bodyMaterial == null ||
            !bodyMaterial.HasProperty(colorProperty))
            return;

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        int propertyID =
            Shader.PropertyToID(colorProperty);

        // Read the default from M_Body.
        Color defaultColor =
            bodyMaterial.GetColor(propertyID);

        Color targetColor =
            colorMode == ColorMode.Default
            ? defaultColor
            : customColor;

        characterRenderer.GetPropertyBlock(
            propertyBlock,
            bodyMaterialIndex
        );

        // Only change the suit color property.
        propertyBlock.SetColor(
            propertyID,
            targetColor
        );

        characterRenderer.SetPropertyBlock(
            propertyBlock,
            bodyMaterialIndex
        );
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.IsPlaying(gameObject))
            return;

        EditorApplication.delayCall += () =>
        {
            if (this != null &&
                !Application.IsPlaying(gameObject))
            {
                ApplyColor();
            }
        };
    }
#endif
}

// Custom Inspector: shows Custom Color only
// when Custom mode is selected.
#if UNITY_EDITOR
[CustomEditor(typeof(CharacterColorController))]
public class CharacterColorControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty(
                "characterRenderer")
        );

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty(
                "bodyMaterialIndex")
        );

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty(
                "colorProperty")
        );

        EditorGUILayout.Space(10);

        SerializedProperty mode =
            serializedObject.FindProperty("colorMode");

        EditorGUILayout.PropertyField(mode);

        if (mode.enumValueIndex == 1)
        {
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(
                    "customColor")
            );
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(8);

        if (GUILayout.Button("Reset to Default"))
        {
            ((CharacterColorController)target)
                .ResetToDefault();
        }
    }
}
#endif
