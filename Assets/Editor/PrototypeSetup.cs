using System;
using GuildTactics.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GuildTactics.Editor
{
    public static class PrototypeSetup
    {
        private const string ScenePath = "Assets/Scenes/TacticalPrototype.unity";

        // Run once from batch mode after a fresh project has imported its scripts.
        public static void CreateAndValidate()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                throw new InvalidOperationException("Prototype scene already exists; refusing to overwrite it.");

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 0, -10);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.06f, 0.08f);
            var bootstrap = new GameObject("Game Bootstrap", typeof(GameBootstrap)).GetComponent<GameBootstrap>();
            var serializedBootstrap = new SerializedObject(bootstrap);
            serializedBootstrap.FindProperty("gridCamera").objectReferenceValue = camera;
            serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();

            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode2D;
            EditorSettings.serializationMode = SerializationMode.ForceText;
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Could not save prototype scene.");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Validate();
        }

        public static void Validate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var bootstrapCount = 0;
            var cameraCount = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                bootstrapCount += root.GetComponentsInChildren<GameBootstrap>(true).Length;
                foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                {
                    cameraCount++;
                    if (!camera.orthographic || !camera.CompareTag("MainCamera"))
                        throw new InvalidOperationException("Expected an orthographic main camera.");
                }
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root) != 0)
                    throw new InvalidOperationException("Missing script in prototype scene.");
            }
            if (bootstrapCount != 1 || cameraCount != 1)
                throw new InvalidOperationException("Expected one bootstrap and one camera.");
            if (GraphicsSettings.defaultRenderPipeline != null)
                throw new InvalidOperationException("Foundation expects the built-in render pipeline.");
            Debug.Log("WP-00 validation passed: saved scene, bootstrap, camera, built-in pipeline.");
        }
    }
}
