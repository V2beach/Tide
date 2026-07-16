using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// JsonUtility fills these DTOs through reflection.
#pragma warning disable 0649

/// <summary>
/// Creates a dedicated, non-gameplay scene for validating the approved R03/R06
/// production sprites at their V7 world scale. Existing scenes and prefabs are
/// never modified by this tool.
/// </summary>
public static class TideProductionArtPreviewSceneBuilder
{
    private const string SpecPath =
        "Assets/Art/GeneratedAI/ProductionR03R06V8/preview-scene-spec.json";
    private const string RequiredScenePath =
        "Assets/Scenes/ProductionArt/R03R06ProductionPreview.unity";
    private const string RequiredMaterialRoot =
        "Assets/Art/GeneratedAI/ProductionR03R06V8/Unity/Materials";

    [Serializable]
    private sealed class PreviewSpec
    {
        public int version;
        public string scenePath;
        public string materialPath;
        public CameraDefinition camera;
        public float waterlineY;
        public SubjectDefinition[] subjects;
    }

    [Serializable]
    private sealed class CameraDefinition
    {
        public float[] position;
        public float orthographicSize;
        public float[] background;
    }

    [Serializable]
    private sealed class SubjectDefinition
    {
        public string name;
        public string spritePath;
        public float[] worldSize;
        public float[] position;
        public int sortingOrder;
    }

    [MenuItem("Tide/Production Art/Build R03 R06 Preview Scene")]
    public static void BuildPreviewScene()
    {
        PreviewSpec spec = LoadSpec();
        List<string> failures = Validate(spec);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Tide production-art preview validation failed:\n- " +
                string.Join("\n- ", failures));
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("Tide production-art preview build was cancelled before changing scenes.");
            return;
        }

        EnsureAssetFolder(Path.GetDirectoryName(spec.scenePath)?.Replace('\\', '/'));
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "R03R06ProductionPreview";

        GameObject root = new GameObject("TideProductionArtPreviewRoot");
        CreateCamera(spec.camera, root.transform);
        for (int i = 0; i < spec.subjects.Length; i++)
        {
            CreateSubject(spec.subjects[i], root.transform);
        }

        CreateWaterline(spec, root.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, spec.scenePath))
        {
            throw new InvalidOperationException("Unity failed to save " + spec.scenePath + ".");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = root;
        Debug.Log("Built isolated Tide production-art preview scene at " + spec.scenePath + ".");
    }

    private static PreviewSpec LoadSpec()
    {
        if (!File.Exists(SpecPath))
        {
            throw new FileNotFoundException("Tide V8 preview specification is missing.", SpecPath);
        }

        PreviewSpec spec = JsonUtility.FromJson<PreviewSpec>(File.ReadAllText(SpecPath));
        if (spec == null)
        {
            throw new InvalidOperationException("Unity could not parse " + SpecPath + ".");
        }

        return spec;
    }

    private static List<string> Validate(PreviewSpec spec)
    {
        List<string> failures = new List<string>();
        if (spec.version != 8)
        {
            failures.Add("Expected preview spec version 8, found " + spec.version + ".");
        }

        if (!string.Equals(spec.scenePath, RequiredScenePath, StringComparison.Ordinal))
        {
            failures.Add("Preview scene path is outside the dedicated output: " + spec.scenePath);
        }

        if (string.IsNullOrWhiteSpace(spec.materialPath) ||
            !spec.materialPath.StartsWith(RequiredMaterialRoot + "/", StringComparison.Ordinal))
        {
            failures.Add("Preview material path is outside the dedicated V8 folder.");
        }

        if (spec.camera == null || spec.camera.position == null || spec.camera.position.Length != 3 ||
            spec.camera.background == null || spec.camera.background.Length != 4 ||
            spec.camera.orthographicSize <= 0f)
        {
            failures.Add("Camera definition is incomplete.");
        }

        if (spec.subjects == null || spec.subjects.Length != 3)
        {
            failures.Add("Preview must contain exactly the house, character, and boat.");
            return failures;
        }

        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < spec.subjects.Length; i++)
        {
            SubjectDefinition subject = spec.subjects[i];
            if (subject == null || string.IsNullOrWhiteSpace(subject.name) || !names.Add(subject.name))
            {
                failures.Add("A preview subject is null, unnamed, or duplicated.");
                continue;
            }

            if (subject.worldSize == null || subject.worldSize.Length != 2 ||
                subject.worldSize[0] <= 0f || subject.worldSize[1] <= 0f ||
                subject.position == null || subject.position.Length != 3)
            {
                failures.Add(subject.name + " has invalid size or position.");
                continue;
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(subject.spritePath);
            if (sprite == null)
            {
                failures.Add(subject.name + " is not imported as a Sprite: " + subject.spritePath);
                continue;
            }

            float scaleX = subject.worldSize[0] / sprite.bounds.size.x;
            float scaleY = subject.worldSize[1] / sprite.bounds.size.y;
            if (Mathf.Abs(scaleX - scaleY) > 0.001f)
            {
                failures.Add(
                    subject.name + " would be non-uniformly scaled: " +
                    scaleX.ToString("F5") + " vs " + scaleY.ToString("F5"));
            }
        }

        return failures;
    }

    private static void CreateCamera(CameraDefinition definition, Transform parent)
    {
        GameObject cameraObject = new GameObject("ProductionPreviewCamera");
        cameraObject.transform.SetParent(parent, false);
        cameraObject.transform.position = new Vector3(
            definition.position[0], definition.position[1], definition.position[2]);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = definition.orthographicSize;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(
            definition.background[0], definition.background[1],
            definition.background[2], definition.background[3]);
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 50f;
        cameraObject.tag = "MainCamera";
    }

    private static void CreateSubject(SubjectDefinition definition, Transform parent)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(definition.spritePath);
        GameObject subject = new GameObject(definition.name);
        subject.transform.SetParent(parent, false);
        subject.transform.position = new Vector3(
            definition.position[0], definition.position[1], definition.position[2]);
        float scale = definition.worldSize[0] / sprite.bounds.size.x;
        subject.transform.localScale = new Vector3(scale, scale, 1f);

        SpriteRenderer renderer = subject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = Color.white;
        renderer.sortingOrder = definition.sortingOrder;
    }

    private static void CreateWaterline(PreviewSpec spec, Transform parent)
    {
        Material material = LoadOrCreateWaterlineMaterial(spec.materialPath);
        GameObject waterline = new GameObject("ProductionPreviewWaterline");
        waterline.transform.SetParent(parent, false);
        LineRenderer line = waterline.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, new Vector3(-10f, spec.waterlineY, -0.1f));
        line.SetPosition(1, new Vector3(10f, spec.waterlineY, -0.1f));
        line.startWidth = 0.035f;
        line.endWidth = 0.035f;
        line.startColor = new Color(0.36f, 0.62f, 0.65f, 0.78f);
        line.endColor = line.startColor;
        line.material = material;
        line.sortingOrder = 8;
    }

    private static Material LoadOrCreateWaterlineMaterial(string assetPath)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            throw new InvalidOperationException("Unity could not find the Sprites/Default shader.");
        }

        EnsureAssetFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
        material = new Material(shader) { name = "TidePreviewWaterline" };
        AssetDatabase.CreateAsset(material, assetPath);
        return material;
    }

    private static void EnsureAssetFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string name = Path.GetFileName(folder);
        EnsureAssetFolder(parent);
        AssetDatabase.CreateFolder(string.IsNullOrEmpty(parent) ? "Assets" : parent, name);
    }
}

#pragma warning restore 0649
