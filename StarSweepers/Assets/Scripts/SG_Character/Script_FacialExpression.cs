
using System.Collections;
using UnityEngine;

[ExecuteAlways]
public class FaceExpressionController : MonoBehaviour
{
    public enum Expression
    {
        Null,
        Neutral,
        Yes,
        No,
        Annoyed,
        Huh,
        Excited
    }

    [System.Serializable]
    public class FaceTextures
    {
        public Texture2D baseColor;
        public Texture2D emissive;
    }

    [Header("Character Renderer")]
    [SerializeField] private Renderer characterRenderer;

    [Tooltip("Material slot containing M_Face. Element 1 = index 1.")]
    [Min(0)]
    [SerializeField] private int faceMaterialIndex = 1;

    [Header("Expression Textures")]
    public FaceTextures nullFace;
    public FaceTextures neutral;
    public FaceTextures yes;
    public FaceTextures no;
    public FaceTextures annoyed;
    public FaceTextures huh;
    public FaceTextures excited;

    [Header("Expression Preview")]
    public Expression currentExpression = Expression.Neutral;

    [Header("Automatic Blinking")]
    public bool enableBlinking = true;

    [Min(0.1f)]
    public float minBlinkInterval = 2f;

    [Min(0.1f)]
    public float maxBlinkInterval = 5f;

    [Min(0.01f)]
    public float blinkDuration = 0.1f;

    private MaterialPropertyBlock propertyBlock;
    private Coroutine blinkCoroutine;
    private int expressionVersion;

    private static readonly int BaseMapID =
        Shader.PropertyToID("_BaseMap");

    private static readonly int EmissionMapID =
        Shader.PropertyToID("_EmissionMap");

    private void OnEnable()
    {
        ApplyExpression(currentExpression);

        // Automatic blinking only happens in Play Mode.
        if (Application.IsPlaying(gameObject))
        {
            blinkCoroutine = StartCoroutine(BlinkRoutine());
        }
    }

    private void OnDisable()
    {
        if (blinkCoroutine != null)
        {
            StopCoroutine(blinkCoroutine);
            blinkCoroutine = null;
        }
    }

    // Called by gameplay scripts.
    public void SetExpression(Expression expression)
    {
        currentExpression = expression;
        expressionVersion++;

        ApplyExpression(expression);
    }

    // Allows UnityEvents to select an expression by index.
    public void SetExpressionByIndex(int index)
    {
        if (System.Enum.IsDefined(typeof(Expression), index))
        {
            SetExpression((Expression)index);
        }
    }

    private FaceTextures GetFaceTextures(Expression expression)
    {
        switch (expression)
        {
            case Expression.Null:
                return nullFace;

            case Expression.Neutral:
                return neutral;

            case Expression.Yes:
                return yes;

            case Expression.No:
                return no;

            case Expression.Annoyed:
                return annoyed;

            case Expression.Huh:
                return huh;

            case Expression.Excited:
                return excited;

            default:
                return neutral;
        }
    }

    private void ApplyExpression(Expression expression)
    {
        if (characterRenderer == null)
            return;

        Material[] materials = characterRenderer.sharedMaterials;

        if (faceMaterialIndex < 0 ||
            faceMaterialIndex >= materials.Length)
            return;

        FaceTextures textures = GetFaceTextures(expression);

        if (textures == null)
            return;

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        characterRenderer.GetPropertyBlock(
            propertyBlock,
            faceMaterialIndex
        );

        if (textures.baseColor != null)
        {
            propertyBlock.SetTexture(
                BaseMapID,
                textures.baseColor
            );
        }

        if (textures.emissive != null)
        {
            propertyBlock.SetTexture(
                EmissionMapID,
                textures.emissive
            );
        }

        characterRenderer.SetPropertyBlock(
            propertyBlock,
            faceMaterialIndex
        );
    }

    // Checks whether an expression supports blinking.
    private bool CanBlink(Expression expression)
    {
        return expression == Expression.Neutral ||
               expression == Expression.Annoyed ||
               expression == Expression.Huh;
    }

    private IEnumerator BlinkRoutine()
    {
        while (true)
        {
            float waitTime = Random.Range(
                minBlinkInterval,
                Mathf.Max(minBlinkInterval, maxBlinkInterval)
            );

            yield return new WaitForSeconds(waitTime);

            if (!enableBlinking ||
                !CanBlink(currentExpression))
            {
                continue;
            }

            // Remember the expression before blinking.
            Expression originalExpression = currentExpression;
            int versionBeforeBlink = expressionVersion;

            // Close eyes using the Null texture pair.
            ApplyExpression(Expression.Null);

            yield return new WaitForSeconds(blinkDuration);

            // Restore the previous expression only if
            // nothing else changed during the blink.
            if (currentExpression == originalExpression &&
                expressionVersion == versionBeforeBlink)
            {
                ApplyExpression(originalExpression);
            }
        }
    }

#if UNITY_EDITOR
    // Preview expressions without entering Play Mode.
    private void OnValidate()
    {
        if (Application.IsPlaying(gameObject))
            return;

        UnityEditor.EditorApplication.delayCall +=
            ApplyEditorPreview;
    }

    private void ApplyEditorPreview()
    {
        if (this == null || characterRenderer == null)
            return;

        if (Application.IsPlaying(gameObject))
            return;

        ApplyExpression(currentExpression);
    }
#endif

    // Manual testing through the Inspector menu.
    [ContextMenu("Face/Neutral")]
    private void TestNeutral()
    {
        SetExpression(Expression.Neutral);
    }

    [ContextMenu("Face/Null")]
    private void TestNull()
    {
        SetExpression(Expression.Null);
    }

    [ContextMenu("Face/Yes")]
    private void TestYes()
    {
        SetExpression(Expression.Yes);
    }

    [ContextMenu("Face/No")]
    private void TestNo()
    {
        SetExpression(Expression.No);
    }

    [ContextMenu("Face/Annoyed")]
    private void TestAnnoyed()
    {
        SetExpression(Expression.Annoyed);
    }

    [ContextMenu("Face/Huh")]
    private void TestHuh()
    {
        SetExpression(Expression.Huh);
    }

    [ContextMenu("Face/Excited")]
    private void TestExcited()
    {
        SetExpression(Expression.Excited);
    }
}
