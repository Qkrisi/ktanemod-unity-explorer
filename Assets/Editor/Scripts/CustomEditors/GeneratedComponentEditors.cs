using UnityEditor;
using UnityEngine;

public class GeneratedComponentEditor : Editor
{
    private bool enabled;
    
    public override void OnInspectorGUI()
    {
        if (!enabled)
        {
            EditorGUILayout.HelpBox(
                "This component is managed automatically and is not intended to be edited by the user.",
                MessageType.Warning);
            enabled = GUILayout.Button("Edit anyway");
        }
        GUI.enabled = enabled;
        base.OnInspectorGUI();
        GUI.enabled = true;
    }
}

[CustomEditor(typeof(KMMaterialInfo)), CanEditMultipleObjects]
public class KMMaterialInfoEditor : GeneratedComponentEditor
{
}

[CustomEditor(typeof(KMDelegateInfo)), CanEditMultipleObjects]
public class KMDelegateInfoEditor : GeneratedComponentEditor
{
}