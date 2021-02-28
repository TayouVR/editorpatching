﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace needle.EditorPatching
{
    public class PatchManagerSettingsProvider : SettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreatePatchManagerSettingsProvider()
        {
            try
            {
                // PatchManagerSettings.instance.Save();
                return new PatchManagerSettingsProvider("Project/Needle/Editor Patch Manager", SettingsScope.Project);
            }
            catch (System.Exception e)
            {
                Debug.Log(e);
            }

            return null;
        }

        public PatchManagerSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null) : base(path, scopes, keywords)
        {
        }

        #region UI Implementation

        private Vector2 scroll;
        const string k_ActiveOnlyKey = "Needle.PatchManager.ActivePatchesOnly";

        public override void OnGUI(string searchContext)
        {
            InitStyles();

            EditorGUILayout.BeginVertical();
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.BeginVertical();
            var skipInactive = EditorPrefs.GetBool(k_ActiveOnlyKey);
            
            var managedPatches = PatchManager.KnownPatches;
            if (managedPatches != null && managedPatches.Count > 0)
            {
                foreach (var patch in managedPatches)
                {
                    var isActive = patch.IsActive;
                    if (skipInactive && !isActive) continue;
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(10);

                    var title = patch.Name;
                    if (PatchManager.IsWaitingForLoad(patch.Id)) title += " (loading)";
                    else if (isActive) title += " (active)";
                    var tooltip = patch.Description ?? "";
                    if (!string.IsNullOrWhiteSpace(tooltip)) tooltip += "\n";
                    tooltip += "Patch Type: " + patch.GetType().Name;
                    if (GUILayout.Button(new GUIContent(title, tooltip), isActive ? patchTitleActive : patchTitleInactive))
                    {
                        if (PatchManager.TryGetFilePathForPatch(patch.Id, out var path))
                        {
                            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(path));
                        }
                    }

                    if (!isActive)
                    {
                        if (GUILayout.Button("Activate", GUILayout.Width(100)))
                            patch.EnablePatch();
                    }
                    else
                    {
                        if (GUILayout.Button("Deactivate", GUILayout.Width(100)))
                            patch.DisablePatch();
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }    


            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
            
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorPrefs.SetBool(k_ActiveOnlyKey, GUILayout.Toggle(EditorPrefs.GetBool(k_ActiveOnlyKey, false), "Hide Inactive"));
            if(GUILayout.Button("Refresh Patch List", GUILayout.Width(180)))
            {
                PatchesCollector.CollectPatches();
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
        }

        private GUIStyle patchTitleInactive, patchTitleActive, descriptionStyle;

        private void InitStyles()
        {
            if (descriptionStyle == null)
                descriptionStyle = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = true,
                    normal = {textColor = new Color(.5f, .5f, .5f)}
                };

            if (patchTitleActive == null)
            {
                patchTitleActive = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Bold,
                };
            }

            if (patchTitleInactive == null)
                patchTitleInactive = new GUIStyle(patchTitleActive)
                {
                    normal = {textColor = new Color(.5f, .5f, .5f),},
                };
        }

        #endregion
    }

    [FilePath("ProjectSettings/EditorPatchingSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal class PatchManagerSettings : ScriptableSingleton<PatchManagerSettings>
    {        
        [SerializeField] private List<string> enabledPatchIds = new List<string>();
        [SerializeField] private List<string> disabledPatchIds = new List<string>();

        internal static bool PersistentActive(EditorPatchProvider prov)
        {
            if (prov == null) return false;
            var id = prov.ID();
            return instance.enabledPatchIds.Contains(id) ||
                   (prov.ActiveByDefault && !instance.disabledPatchIds.Contains(id));
        }

        internal static bool PersistentActive(string id)
        {
            // foreach (var entry in instance.enabledPatchIds) Debug.Log("current active " + entry);
            return instance.enabledPatchIds.Contains(id); 
        }

        internal static void Clear(bool save)
        {
            instance.enabledPatchIds.Clear();
            instance.disabledPatchIds.Clear();
            if(save) instance.Save();
        }

        internal static void SetPersistentActive(string id, bool active)
        {
            // Debug.Log(id + " -> " + active);
            if (active) 
            {
                if (instance.disabledPatchIds.Contains(id)) instance.disabledPatchIds.Remove(id);
                if (!instance.enabledPatchIds.Contains(id)) instance.enabledPatchIds.Add(id);
            } 
            else
            {
                if (instance.enabledPatchIds.Contains(id)) instance.enabledPatchIds.Remove(id); 
                if (!instance.disabledPatchIds.Contains(id)) instance.disabledPatchIds.Add(id);
            }

            instance.Save();
        }

        public void Save() => Save(true);
    }

#if !UNITY_2020_1_OR_NEWER
    public class ScriptableSingleton<T> : ScriptableObject where T : ScriptableObject
    {
        private static T s_Instance;

        public static T instance
        {
            get
            {
                if (!s_Instance) CreateAndLoad();
                return s_Instance;
            }
        }

        protected ScriptableSingleton()
        {
            if (s_Instance)
                Debug.LogError("ScriptableSingleton already exists. Did you query the singleton in a constructor?");
            else
            { 
                object casted = this;
                s_Instance = casted as T;
                System.Diagnostics.Debug.Assert(s_Instance != null); 
            }
        }

        private static void CreateAndLoad()
        {
            System.Diagnostics.Debug.Assert(s_Instance == null);

            // Load
            var filePath = GetFilePath();
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(filePath))
                InternalEditorUtility.LoadSerializedFileAndForget(filePath);
#endif
            if (s_Instance == null)
            {
                var t = CreateInstance<T>();
                t.hideFlags = HideFlags.HideAndDontSave;
            }

            System.Diagnostics.Debug.Assert(s_Instance != null);
        }

        protected virtual void Save(bool saveAsText)
        {
            if (!s_Instance)
            {
                Debug.Log("Cannot save ScriptableSingleton: no instance!");
                return;
            }

            var filePath = GetFilePath();
            if (string.IsNullOrEmpty(filePath)) return;
            var folderPath = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(folderPath) && !string.IsNullOrEmpty(folderPath))
                Directory.CreateDirectory(folderPath);
#if UNITY_EDITOR
            InternalEditorUtility.SaveToSerializedFileAndForget(new[] {s_Instance}, filePath, saveAsText);
#endif
        }

        private static string GetFilePath()
        {
            var type = typeof(T);
            var attributes = type.GetCustomAttributes(true);
            return attributes.OfType<FilePathAttribute>()
                .Select(f => f.filepath)
                .FirstOrDefault();
        }
    }

    [System.AttributeUsage(System.AttributeTargets.Class)]
    internal sealed class FilePathAttribute : System.Attribute
    {
        public enum Location
        {
            PreferencesFolder,
            ProjectFolder
        }

        public string filepath { get; set; }

        public FilePathAttribute(string relativePath, FilePathAttribute.Location location)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                Debug.LogError("Invalid relative path! (its null or empty)");
                return;
            }

            if (relativePath[0] == '/')
                relativePath = relativePath.Substring(1);
#if UNITY_EDITOR
            if (location == FilePathAttribute.Location.PreferencesFolder)
                this.filepath = InternalEditorUtility.unityPreferencesFolder + "/" + relativePath;
            else
#endif
                this.filepath = relativePath;
        }
    }
#endif
}