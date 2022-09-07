using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using KModkit;
using UnityEngine;
using HarmonyXLib;
using UniverseLib;
using UniverseLib.Input;
using UniverseLib.UI.Panels;
using UnityExplorer;
using UnityExplorer.UI;
using UnityExplorer.Inspectors.MouseInspectors;

[RequireComponent(typeof(KMService), typeof(KMGameInfo))]
public class KtaneUnityExplorer : MonoBehaviour
{
    private static Harmony HarmonyInstance = new Harmony("qkrisi.unityexplorerktane");
    private static ExplorerStandalone Instance;
    private static MethodInfo EnableCursorMethod;
    private static MethodInfo DisableCursorMethod;
    private static MethodInfo DeselectMethod;
    private static FieldInfo MouseRotationField;
    private static Component KTMouseCursor;
    private static bool EnableMouseControls = true;
    private static bool InNavbar;
    private static object MouseControlsInstance;
    private static object SelectableManagerInstance;
    private static object MouseRotationInstance;
    private static Type MouseControlsType;
    private static bool CameraZoomPatched;
    
    static bool StopRotation
    {
        get
        {
            return InNavbar || !EnableMouseControls && ExplorerCore.RCAction == ExplorerCore.RightClickAction.None;
        }
    }
    
    static void ToggleMouseControls(bool controlsEnabled)
    {
        EnableMouseControls = controlsEnabled;
        if(!EnableMouseControls && MouseControlsInstance != null)
            DeselectMethod.Invoke(MouseControlsInstance, new object[0]);
    }
    
    void Awake()
    {
        if(Instance == null)
        {
            GetComponent<KMGameInfo>().OnStateChange += state => {
                if(!CameraZoomPatched && state == KMGameInfo.State.Setup)
                {
                    var camZoomType = ReflectionHelper.FindType("CamZoom", "CameraZoom");
                    if(camZoomType != null)
                    {
                        var camZoomMethod = AccessTools.Method(camZoomType, "Update");
                        if(camZoomMethod != null)
                        {
                            HarmonyInstance.Patch(camZoomMethod, new HarmonyMethod(typeof(KtaneUnityExplorer), "MouseControlsSelect_Prefix"));
                            CameraZoomPatched = true;
                        }
                    }
                }
            };
            
            var KTMouseCursorType = ReflectionHelper.FindGameType("KTMouseCursor");
            EnableCursorMethod = KTMouseCursorType.GetMethod("EnableCursor", BindingFlags.Public | BindingFlags.Instance);
            DisableCursorMethod = KTMouseCursorType.GetMethod("DisableCursor", BindingFlags.Public | BindingFlags.Instance);
            KTMouseCursor = (Component)FindObjectOfType(KTMouseCursorType);
            
            WorldInspector.IgnoreOBJ = KTMouseCursor.transform.parent.Find("CursorPlane").gameObject;
            WorldInspector.IgnoreTypes.Add(ReflectionHelper.FindGameType("Highlightable"));
            WorldInspector.IgnoreTypes.Add(ReflectionHelper.FindGameType("Bomb"));
            Universe.ToggleMouseControls = ToggleMouseControls;
            
            MouseControlsType = ReflectionHelper.FindGameType("MouseControls");
            DeselectMethod = AccessTools.Method(MouseControlsType, "Deselect");
            MouseRotationField = AccessTools.Field(MouseControlsType, "mouseRotation");
            MouseControlsInstance = FindObjectOfType(MouseControlsType);
            
            PanelDragger.SetCursor = cursorEnabled => {
                CursorUnlocker.ForceDisableCursor = cursorEnabled;
                var method = cursorEnabled ? EnableCursorMethod : DisableCursorMethod;
                method.Invoke(KTMouseCursor, new object[0]);
            };
            
            UIManager.InitCallback = () => {
                var updateMethod = AccessTools.Method(MouseControlsType, "Update");
                HarmonyInstance.Patch(AccessTools.Method(MouseControlsType, "Select"), new HarmonyMethod(typeof(KtaneUnityExplorer), "MouseControlsSelect_Prefix"));
                HarmonyInstance.Patch(AccessTools.Method(MouseControlsType, "HandleBackButtonPress"), new HarmonyMethod(typeof(KtaneUnityExplorer), "MouseControlsHandleBackButtonPress_Prefix"));
                try
                {
                    HarmonyInstance.Patch(updateMethod, transpiler: new HarmonyMethod(typeof(KtaneUnityExplorer), "MouseControlsUpdate_Transpiler"));
                }
                catch(Exception ex)
                {
                    Debug.LogError("[Unity Explorer] Failed to apply transpiler to MouseControls.Update:");
                    Debug.LogException(ex);
                    Debug.Log("[Unity Explorer] Using prefix instead");
                    HarmonyInstance.Patch(updateMethod, new HarmonyMethod(typeof(KtaneUnityExplorer), "MouseControlsUpdate_Prefix"));
                }
            };
            
            Instance = ExplorerStandalone.CreateInstance((msg, logType) =>
            {
                if(!msg.StartsWith("[Unity] "))
                    Debug.unityLogger.LogFormat(logType, "[Unity Explorer] {0}", msg);
            });
        }
    }
    
    static void UpdateMousePosition()
    {
        if(PanelManager.UpdateMouseControls)
        {
            var panelInstances = PanelManager.Instances.SelectMany(manager => manager.panelInstances);
            var mousePos = DisplayManager.MousePosition;
            InNavbar = UIManager.ShowMenu && UIManager.NavBarRect.rect.Contains(UIManager.NavBarRect.InverseTransformPoint(mousePos));
            ToggleMouseControls(!InNavbar && !panelInstances.Where(p => p.Enabled).Any(p => p.Dragger.HoveringDragger || p.Rect.rect.Contains(p.Rect.InverseTransformPoint(mousePos))));
        }
    }
    
    static bool MouseControlsUpdate_Prefix()
    {
        UpdateMousePosition();
        return EnableMouseControls;
    }
    
    static bool MouseControlsSelect_Prefix()
    {
        return EnableMouseControls;
    }
    
    static bool MouseControlsHandleBackButtonPress_Prefix()
    {
        return !InNavbar && (EnableMouseControls || ExplorerCore.RCAction == ExplorerCore.RightClickAction.RotateAndDeselect);
    }
    
    static IEnumerable<CodeInstruction> MouseControlsUpdate_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        Label stopRotationLabel;
        Label skipInteractionsLabel;
        return new CodeMatcher(instructions, generator)
            .End()
            .MatchStartBackwards(
                new CodeMatch(new CodeInstruction(OpCodes.Ldarg_0)),
                new CodeMatch(new CodeInstruction(OpCodes.Ldfld, MouseRotationField))
            )
            .CreateLabel(out stopRotationLabel)
            .MatchStartBackwards(
                new CodeMatch(new CodeInstruction(OpCodes.Ldc_I4_1)),
                new CodeMatch(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Input), "GetMouseButtonDown")))
            )
            .CreateLabel(out skipInteractionsLabel)
            .Start()
            .Insert(
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(KtaneUnityExplorer), "UpdateMousePosition")),
                new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(KtaneUnityExplorer), "StopRotation")),
                new CodeInstruction(OpCodes.Brtrue, stopRotationLabel)
            )
            .MatchEndForward(new CodeMatch(new CodeInstruction(OpCodes.Call, AccessTools.Method(MouseControlsType, "HandleViewRotation", new Type[] {typeof(bool)}))))
            .Advance(-3)
            .Insert(
                new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(KtaneUnityExplorer), "EnableMouseControls")),
                new CodeInstruction(OpCodes.Brfalse, skipInteractionsLabel)
            )
            .InstructionEnumeration();
    }
}
