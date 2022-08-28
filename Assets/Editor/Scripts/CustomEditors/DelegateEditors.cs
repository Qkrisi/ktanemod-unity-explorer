using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

public class KMDelegateEditor : Editor
{
    private class DelegateMethodInfo
    {
        public readonly MethodInfo method;
        public readonly bool CompilerGenerated;
        public readonly bool Inherited;
        public bool Special;

        public DelegateMethodInfo(MethodInfo _method)
        {
            method = _method;
            CompilerGenerated = method.IsSpecialName || method.GetCustomAttributes(typeof(CompilerGeneratedAttribute), true).Length > 0;
            Inherited = method.DeclaringType != method.ReflectedType;
        }
    }

    private static Dictionary<Type, FieldInfo[]> DelegateFields = new Dictionary<Type, FieldInfo[]>();
    private static Dictionary<Type, Dictionary<Type, DelegateMethodInfo[]>> CandidateMethods = new Dictionary<Type, Dictionary<Type, DelegateMethodInfo[]>>();
    
    //Types of delegates that are assigned by the game
    private static readonly Type[] ExcludeDelegateTypes =
    {
        typeof(KMBombInfo.GetTimeHandler),
        typeof(KMBombInfo.GetFormattedTimeHandler),
        typeof(KMBombInfo.GetStrikesHandler),
        typeof(KMBombInfo.GetModuleNamesHandler),
        typeof(KMBombInfo.GetSolvableModuleNamesHandler),
        typeof(KMBombInfo.GetSolvedModuleNamesHandler),
        typeof(KMBombInfo.GetModuleIDsHandler),
        typeof(KMBombInfo.GetSolvableModuleIDsHandler),
        typeof(KMBombInfo.GetSolvedModuleIDsHandler),
        typeof(KMBombInfo.GetWidgetQueryResponsesHandler),
        typeof(KMBombInfo.KMIsBombPresent),
        typeof(KMBombModule.KMStrikeEvent),
        typeof(KMBombModule.KMPassEvent),
        typeof(KMBombModule.KMRuleGenerationSeedDelegate),
        typeof(KMNeedyModule.KMStrikeEvent),
        typeof(KMNeedyModule.KMPassEvent),
        typeof(KMNeedyModule.KMRuleGenerationSeedDelegate),
        typeof(KMNeedyModule.KMGetNeedyTimeRemainingDelegate),
        typeof(KMNeedyModule.KMSetNeedyTimeRemainingDelegate),
        typeof(KMGameInfo.KMGetAvailableModuleInfoDelgate),
        typeof(KMGameInfo.KMGetMaximumBombModulesDelgate),
        typeof(KMGameInfo.KMGetMaximumModulesFrontFaceDelgate),
        typeof(KMSelectable.KMOnAddInteractionPunchDelegate),
        typeof(KMSelectable.KMOnUpdateChildrenDelegate)
    };

    private static Dictionary<Type, bool> DelegatesFoldout = new Dictionary<Type, bool>();
    private static Dictionary<Type, List<bool>> FoldoutStates = new Dictionary<Type, List<bool>>();
    private bool ShowHelp;

    protected bool SkipBase;


    public override void OnInspectorGUI()
    {
        if(!SkipBase)
            base.OnInspectorGUI();
        if(target == null)
            return;
        serializedObject.Update();
        if (targets.Length > 1)
        {
            EditorGUILayout.LabelField("Multi-object editing is not supported for delegates");
            goto END;
        }
        var targetComponent = target as Component;
        var targetComponentType = targetComponent.GetType();
        FieldInfo[] fields;
        if (!DelegateFields.TryGetValue(targetComponentType, out fields))
        {
            fields = targetComponentType.GetFields(KMDelegateInfo.FLAGS)
                .Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType) &&
                            !ExcludeDelegateTypes.Contains(f.FieldType)).ToArray();
            DelegateFields.Add(targetComponentType, fields);
        }
        var delegateInfoHandler = targetComponent.GetComponent<KMDelegateInfo>() ??
                                  targetComponent.gameObject.AddComponent<KMDelegateInfo>();
        var serializedDelegateHandler = new SerializedObject(delegateInfoHandler);
        serializedDelegateHandler.Update();
        var delegateInfosProperty = serializedDelegateHandler.FindProperty("DelegateInfos");
        var allowUnityComponentsProperty = serializedDelegateHandler.FindProperty("AllowUnityComponents");
        var allowInheritedsProperty = serializedDelegateHandler.FindProperty("AllowInheriteds");
        var allowCompilerGeneratedsProperty = serializedDelegateHandler.FindProperty("AllowCompilerGenerateds");
        
        //Group the assigned delegates by the destination delegate
        var delegateInfos = new Dictionary<string, List<SerializedProperty>>();
        for (int i = 0; i < delegateInfosProperty.arraySize; i++)
        {
            var info = delegateInfosProperty.GetArrayElementAtIndex(i);
            var delegateName = info.FindPropertyRelative("DelegateName").stringValue;
            info.FindPropertyRelative("Index").intValue = i;
            if(!delegateInfos.ContainsKey(delegateName))
                delegateInfos.Add(delegateName, new List<SerializedProperty>());
            delegateInfos[delegateName].Add(info);
        }
        //
        
        var remove = new List<int>();
        if (fields.Length > 0)
        {
            
            //Keep foldouts open
            if(!DelegatesFoldout.ContainsKey(targetComponentType))
                DelegatesFoldout.Add(targetComponentType, false);
            var delegatesFoldoutState = DelegatesFoldout[targetComponentType];
            delegatesFoldoutState = EditorGUILayout.Foldout(delegatesFoldoutState, "Delegates");
            if (!delegatesFoldoutState)
                ShowHelp = false;
            DelegatesFoldout[targetComponentType] = delegatesFoldoutState;
            //
            
            if(delegatesFoldoutState)
            {
                EditorGUI.indentLevel++;
                
                //Help buttons & message
                if (!ShowHelp)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(15);
                    ShowHelp = GUILayout.Button("?", GUILayout.Width(20));
                    EditorGUILayout.EndHorizontal();
                }
                if (ShowHelp)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(15);
                    ShowHelp = !GUILayout.Button("^", GUILayout.Width(20));
                    EditorGUILayout.EndHorizontal();
                    if(ShowHelp)
                        EditorGUILayout.HelpBox(
                            string.Format(
                                "Assign methods to delegates. Select the GameObject, the component on it, and the selected component's (public) method to call when the delegate is called.\nAdd methods with the\"+\" button and remove them with the \"-\" button.\nThe returned value will be the returned value of the method marked by the \"Use return value\" checkbox (only one of the selected methods can have it checked).\nMethods should have the same signature as the delegates (return type and parameter types).\nA new parameter can be added as the first parameter with the same type as this component ({0}), the value of the paramater will be this component (the sender). These methods are denoted by a '{1}' postfix on their names in the method selection.\n\nThe 3 checkboxes below can be used to filter components and methods.\nUnity/KMFramework components - Allow the selection of Unity and KMFramework components (for ex. Transform, KMAudio) to call a method of\n\nInherited methods - Allow the selection of methods that the selected component only inherits\n\nCompiler generated/special methods - Allow the selection of methods that are generated by the compiler (for ex. property getters)",
                                targetComponentType.Name,
                                KMDelegateInfo.SpecialPostfix),
                            MessageType.Info);
                }
                //

                //Filter toggles
                allowUnityComponentsProperty.boolValue =
                    EditorGUILayout.ToggleLeft("Unity/KMFramework components", allowUnityComponentsProperty.boolValue);
                allowInheritedsProperty.boolValue =
                    EditorGUILayout.ToggleLeft("Inherited methods", allowInheritedsProperty.boolValue);
                allowCompilerGeneratedsProperty.boolValue =
                    EditorGUILayout.ToggleLeft("Compiler generated/special methods", allowCompilerGeneratedsProperty.boolValue);
                //
                
                int fieldIndex = -1;
                foreach (var field in fields)
                {
                    SerializedProperty lastValue = null;
                    var forceRemove = false;
                    var delegateName = field.Name;
                    fieldIndex++;
                    if(!FoldoutStates.ContainsKey(targetComponentType))
                        FoldoutStates.Add(targetComponentType, new List<bool>());
                    var foldoutStates = FoldoutStates[targetComponentType];
                    while(foldoutStates.Count <= fieldIndex)
                        foldoutStates.Add(false);
                    foldoutStates[fieldIndex] = EditorGUILayout.Foldout(foldoutStates[fieldIndex], delegateName);
                    if (foldoutStates[fieldIndex])
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(18);
                        
                        //Add new delegate
                        if (GUILayout.Button("+", GUILayout.Width(20)))
                        {
                            var newInfo = delegateInfosProperty.GetArrayElementAtIndex(delegateInfosProperty.arraySize++);
                            newInfo.FindPropertyRelative("DestinationComponent").objectReferenceValue = targetComponent;
                            newInfo.FindPropertyRelative("DelegateName").stringValue = delegateName;
                            newInfo.FindPropertyRelative("Index").intValue = delegateInfosProperty.arraySize - 1;
                            if(!delegateInfos.ContainsKey(delegateName))
                                delegateInfos.Add(delegateName, new List<SerializedProperty>());
                            delegateInfos[delegateName].Add(newInfo);
                            forceRemove = true;
                            lastValue = newInfo.FindPropertyRelative("UseReturnValue");
                        }
                        //
                        
                        EditorGUILayout.EndHorizontal();
                        if (delegateInfos.ContainsKey(delegateName))
                        {
                            foreach (var info in delegateInfos[delegateName])
                            {
                                EditorGUILayout.BeginHorizontal();
                                GUILayout.Space(18);
                                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                                EditorGUILayout.BeginHorizontal();
                                GUILayout.Space(29);
                                
                                //Remove delegate
                                if (GUILayout.Button("-", GUILayout.Width(20)))
                                {
                                    remove.Add(info.FindPropertyRelative("Index").intValue);    //Removing at the end
                                    goto EndInfo;
                                }
                                //
                                
                                //Return value usage
                                var useReturnValueProperty = info.FindPropertyRelative("UseReturnValue");
                                var defaultState = (!forceRemove || lastValue == useReturnValueProperty) && (useReturnValueProperty.boolValue || (lastValue == null && info == delegateInfos[delegateName].Last()));
                                GUI.enabled = !defaultState;
                                useReturnValueProperty.boolValue = EditorGUILayout.ToggleLeft("Use return value", defaultState) || defaultState;
                                GUI.enabled = true;
                                if (useReturnValueProperty.boolValue)
                                {
                                    if (lastValue != null && lastValue != useReturnValueProperty)
                                        lastValue.boolValue = false;
                                    lastValue = useReturnValueProperty;
                                    forceRemove = true;
                                }
                                //
                                
                                EditorGUILayout.EndHorizontal();
                                EditorGUILayout.BeginHorizontal();
                                
                                var sourceGameObjectProperty = info.FindPropertyRelative("SourceGameObject");
                                var sourceComponentProperty = info.FindPropertyRelative("SourceComponent");
                                var methodNameProperty = info.FindPropertyRelative("MethodName");
                                
                                //Assign GameObject
                                var oldGameObject = sourceGameObjectProperty.objectReferenceValue;
                                var gameObject = EditorGUILayout.ObjectField(sourceGameObjectProperty.objectReferenceValue,
                                    typeof(GameObject), true) as GameObject;
                                if (oldGameObject != gameObject)
                                {
                                    sourceComponentProperty.objectReferenceValue = null;
                                    methodNameProperty.stringValue = "";
                                }
                                sourceGameObjectProperty.objectReferenceValue = gameObject;
                                if (gameObject == null)
                                    goto EndInfo;
                                //
                               
                                //Assign component
                                var components = gameObject.GetComponents<Component>();
                                if (!allowUnityComponentsProperty.boolValue)
                                    components = components.Where(c =>
                                    {
                                        var componentType = c.GetType();
                                        return !componentType.Name.StartsWith("KM") &&
                                               !componentType.Assembly.GetName().Name.StartsWith("UnityEngine");
                                    }).ToArray();
                                var componentIndex = EditorGUILayout.Popup(
                                    Array.IndexOf(components, (Component)sourceComponentProperty.objectReferenceValue) + 1,
                                    new[] { "Select component" }.Concat(components.Select(c => c.GetType().Name))
                                        .ToArray()) - 1;
                                var selectedComponent = componentIndex == -1 ? null : components[componentIndex];
                                sourceComponentProperty.objectReferenceValue = selectedComponent;
                                //
                                
                                if (selectedComponent != null)
                                {
                                    var selectedComponentType = selectedComponent.GetType();
                                    Dictionary<Type, DelegateMethodInfo[]> RegisteredTypes;
                                    
                                    //Assign method
                                    DelegateMethodInfo[] candidateMethods;
                                    var delegateType = field.FieldType;
                                    if (!CandidateMethods.TryGetValue(selectedComponentType, out RegisteredTypes))
                                    {
                                        RegisteredTypes = new Dictionary<Type, DelegateMethodInfo[]>();
                                        CandidateMethods.Add(selectedComponentType, RegisteredTypes);
                                    }
                                    if (!RegisteredTypes.TryGetValue(delegateType, out candidateMethods))
                                    {
                                        var invokeMethod = delegateType.GetMethod("Invoke", KMDelegateInfo.FLAGS);
                                        candidateMethods = selectedComponentType.GetMethods(KMDelegateInfo.FLAGS).Select(m => new DelegateMethodInfo(m))
                                            .Where(m =>
                                            {
                                                if (m.method.GetCustomAttributes(typeof(HideInInspector), true).Length >
                                                    0)
                                                    return false;
                                                var invokeParameterTypes = invokeMethod.GetParameters().Select(p => p.ParameterType).ToList();
                                                var parameterTypes = m.method.GetParameters().Select(p => p.ParameterType);
                                                if (!m.method.ReturnType.IsAssignableFrom(invokeMethod.ReturnType))
                                                    return false;
                                                var ret = parameterTypes.SequenceEqual(invokeParameterTypes);
                                                if (!ret)
                                                {
                                                    invokeParameterTypes.Insert(0, targetComponentType);
                                                    ret |= parameterTypes.SequenceEqual(invokeParameterTypes);
                                                    m.Special = ret;
                                                }
                                                return ret;
                                            }).ToArray();
                                        RegisteredTypes.Add(delegateType, candidateMethods);
                                    }

                                    var candidateMethodNames = candidateMethods.Where(m =>
                                            (allowInheritedsProperty.boolValue || !m.Inherited) &&
                                            (allowCompilerGeneratedsProperty.boolValue || !m.CompilerGenerated))
                                        .Select(m => m.method.Name + (m.Special ? KMDelegateInfo.SpecialPostfix : ""))
                                        .ToArray();
                                    var methodNameIndex = EditorGUILayout.Popup(
                                        Array.IndexOf(candidateMethodNames, methodNameProperty.stringValue) + 1,
                                        new[] { "Select method" }.Concat(candidateMethodNames).ToArray()) - 1;
                                    methodNameProperty.stringValue = methodNameIndex == -1 ? "" : candidateMethodNames[methodNameIndex];
                                    //
                                    
                                }
                                EndInfo:
                                EditorGUILayout.EndHorizontal();
                                EditorGUILayout.EndVertical();
                                EditorGUILayout.EndHorizontal();
                                GUILayout.Space(2);
                            }
                        }
                        EditorGUI.indentLevel--;
                    }
                }
                EditorGUI.indentLevel--;
            }
        }
        
        //Remove delegates
        foreach (var index in remove.OrderByDescending(i => i))
            delegateInfosProperty.DeleteArrayElementAtIndex(index);
        //
        
        serializedDelegateHandler.ApplyModifiedPropertiesWithoutUndo();
        END:
        serializedObject.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(KMBombInfo)), CanEditMultipleObjects]
public class KMBombInfoEditor : KMDelegateEditor
{
}

[CustomEditor(typeof(KMGameInfo)), CanEditMultipleObjects]
public class KMGameInfoEditor : KMDelegateEditor
{
}

[CustomEditor(typeof(KMGameplayRoom)), CanEditMultipleObjects]
public class KMGameplayRoomEditor : KMDelegateEditor
{
}

[CustomEditor(typeof(KMSelectable)), CanEditMultipleObjects]
public class KMSelectableEditor : KMDelegateEditor
{
}

[CustomEditor(typeof(KMWidget)), CanEditMultipleObjects]
public class KMWidgetEditor : KMDelegateEditor
{
}
