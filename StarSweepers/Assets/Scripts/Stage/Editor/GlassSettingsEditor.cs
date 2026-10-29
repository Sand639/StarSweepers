using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(GravityGlassFloor))]
[CanEditMultipleObjects]
public class GravityGlassFloorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // GlassType 顯示在最上方
        SerializedProperty type =
            serializedObject.FindProperty("glasstype");

        EditorGUILayout.PropertyField(type);

        EditorGUILayout.Space();

        // GlassType 專用設定
        if (!type.hasMultipleDifferentValues)
        {
            switch ((GravityGlassFloor.GlassType)type.intValue)
            {
                case GravityGlassFloor.GlassType.StepBreak:
                    DrawProperty("secondsUntilBreak");
                    break;

                case GravityGlassFloor.GlassType.CounterBreak:
                    DrawProperty("breakCount");
                    break;

                case GravityGlassFloor.GlassType.StayTime:
                    DrawProperty("stayTime");
                    break;
            }
        }

        EditorGUILayout.Space();

        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            "glasstype",
            "secondsUntilBreak",
            "breakCount",
            "stayTime"
        );

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawProperty(string propertyName)
    {
        SerializedProperty property =
            serializedObject.FindProperty(propertyName);

        if (property != null)
        {
            EditorGUILayout.PropertyField(property, true);
        }
    }
}