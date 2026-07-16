using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TideStiltHouseFirstSliceSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Tide_StiltHouse_FirstSlice.unity";
    private const string PrefabPath = "Assets/Prefabs/TideStiltHouseFirstSlice.prefab";
    private static readonly string[] V40WreckPaths =
    {
        "Assets/Art/GeneratedAI/ProductionShipwreckOriginV40/Runtime/Wrecks/TideShipwreckV40_BrokenMast.png",
        "Assets/Art/GeneratedAI/ProductionShipwreckOriginV40/Runtime/Wrecks/TideShipwreckV40_CopperHullPlank.png",
        "Assets/Art/GeneratedAI/ProductionShipwreckOriginV40/Runtime/Wrecks/TideShipwreckV40_PartialKeelRib.png",
        "Assets/Art/GeneratedAI/ProductionShipwreckOriginV40/Runtime/Wrecks/TideShipwreckV40_RopeWrappedBeam.png",
        "Assets/Art/GeneratedAI/ProductionShipwreckOriginV40/Runtime/Wrecks/TideShipwreckV40_TornSailBundle.png",
        "Assets/Art/GeneratedAI/ProductionShipwreckOriginV40/Runtime/Wrecks/TideShipwreckV40_ProvisionCask.png"
    };
    // V67 is the approved Balanced V39/V52 atomic set. It preserves V46 geometry
    // while cleaning the bright/dark edge matte; every V39 layer and V52 owner must
    // come from this same root so one frame never mixes two different edge treatments.
    private const string V39BalancedRoot =
        "Assets/Art/GeneratedAI/ProductionBoatEdgeMatteRefinementV67/Runtime/Balanced/V39";
    private static readonly string[] V39DamagedBoatLayerPaths =
    {
        V39BalancedRoot + "/ServiceableDamaged/TideBoatV39_ServiceableDamaged_BackRig.png",
        V39BalancedRoot + "/ServiceableDamaged/TideBoatV39_ServiceableDamaged_SailRest.png",
        V39BalancedRoot + "/ServiceableDamaged/TideBoatV39_ServiceableDamaged_BackHull.png",
        V39BalancedRoot + "/ServiceableDamaged/TideBoatV39_ServiceableDamaged_CockpitFloor.png",
        V39BalancedRoot + "/ServiceableDamaged/TideBoatV39_ServiceableDamaged_FrontGunwale.png",
        V39BalancedRoot + "/ServiceableDamaged/TideBoatV39_ServiceableDamaged_RudderRest.png"
    };
    private static readonly string[] V39RepairedBoatLayerPaths =
    {
        V39BalancedRoot + "/Repaired/TideBoatV39_Repaired_BackRig.png",
        V39BalancedRoot + "/Repaired/TideBoatV39_Repaired_SailRest.png",
        V39BalancedRoot + "/Repaired/TideBoatV39_Repaired_BackHull.png",
        V39BalancedRoot + "/Repaired/TideBoatV39_Repaired_CockpitFloor.png",
        V39BalancedRoot + "/Repaired/TideBoatV39_Repaired_FrontGunwale.png",
        V39BalancedRoot + "/Repaired/TideBoatV39_Repaired_RudderRest.png"
    };
    [MenuItem("Tide/Create Stilt House First Slice Scene")]
    public static void CreateSceneAndPrefab()
    {
        EnsureFolder("Assets/Scenes");
        EnsureFolder("Assets/Prefabs");

        GameObject prefabRoot = CreateSliceRoot();
        PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
        Object.DestroyImmediate(prefabRoot);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "Tide_StiltHouse_FirstSlice";

        Camera camera = new GameObject("StiltFirstSliceCamera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.orthographicSize = 3.85f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.backgroundColor = new Color(0.045f, 0.095f, 0.105f, 1f);
        camera.clearFlags = CameraClearFlags.SolidColor;

        GameObject root = CreateSliceRoot();
        root.name = "TideStiltHouseFirstSliceRoot";

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings(ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Created {ScenePath} and {PrefabPath}");
    }

    [MenuItem("Tide/Integrate V40 Shipwreck Origin Resources")]
    public static void IntegrateV40ShipwreckOriginResources()
    {
        Sprite[] sprites = LoadV40WreckSprites();

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            TideStiltHouseFirstSliceController prefabController =
                prefabRoot.GetComponent<TideStiltHouseFirstSliceController>();
            if (prefabController == null)
            {
                throw new System.InvalidOperationException("Tide first-slice prefab has no controller.");
            }

            prefabController.ConfigureV40ShipwreckOriginSpritesForEditor(sprites);
            EditorUtility.SetDirty(prefabController);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TideStiltHouseFirstSliceController sceneController =
            Object.FindFirstObjectByType<TideStiltHouseFirstSliceController>();
        if (sceneController == null)
        {
            throw new System.InvalidOperationException("Tide first-slice scene has no controller.");
        }

        sceneController.ConfigureV40ShipwreckOriginSpritesForEditor(sprites);
        sceneController.RebuildGeneratedHierarchyForEditor();
        EditorUtility.SetDirty(sceneController);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("Integrated six V40 shipwreck-origin sprites into the first-slice scene and prefab.");
    }

    [MenuItem("Tide/Integrate V39 Boat Semantic Resources")]
    public static void IntegrateV39BoatSemanticResources()
    {
        Sprite[] damagedLayers = LoadSprites(V39DamagedBoatLayerPaths, "V39 damaged boat layer");
        Sprite[] repairedLayers = LoadSprites(V39RepairedBoatLayerPaths, "V39 repaired boat layer");

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            TideStiltHouseFirstSliceController prefabController =
                prefabRoot.GetComponent<TideStiltHouseFirstSliceController>();
            if (prefabController == null)
            {
                throw new System.InvalidOperationException("Tide first-slice prefab has no controller.");
            }

            prefabController.ConfigureV39BoatLayersForEditor(damagedLayers, repairedLayers);
            prefabController.RebuildGeneratedHierarchyForEditor();
            EditorUtility.SetDirty(prefabController);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TideStiltHouseFirstSliceController sceneController =
            Object.FindFirstObjectByType<TideStiltHouseFirstSliceController>();
        if (sceneController == null)
        {
            throw new System.InvalidOperationException("Tide first-slice scene has no controller.");
        }

        sceneController.ConfigureV39BoatLayersForEditor(damagedLayers, repairedLayers);
        sceneController.RebuildGeneratedHierarchyForEditor();
        EditorUtility.SetDirty(sceneController);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("Integrated twelve V39 semantic boat layers into the first-slice scene and prefab.");
    }

    [MenuItem("Tide/Integrate V67 V52 Boat Repair Resources")]
    public static void IntegrateV67V52BoatRepairResources()
    {
        TideV52BoatRepairRuntimeAssetBuilder.RebuildCatalog();
        IntegrateV39BoatSemanticResources();
        Debug.Log("Integrated the atomic V67 Balanced V39/V52 boat-repair set.");
    }

    private static GameObject CreateSliceRoot()
    {
        GameObject root = new GameObject("TideStiltHouseFirstSliceRoot");
        root.transform.position = Vector3.zero;
        TideStiltHouseFirstSliceController controller = root.AddComponent<TideStiltHouseFirstSliceController>();
        controller.ConfigureV40ShipwreckOriginSpritesForEditor(LoadV40WreckSprites());
        controller.ConfigureV39BoatLayersForEditor(
            LoadSprites(V39DamagedBoatLayerPaths, "V39 damaged boat layer"),
            LoadSprites(V39RepairedBoatLayerPaths, "V39 repaired boat layer"));
        controller.RebuildGeneratedHierarchyForEditor();
        return root;
    }

    private static Sprite[] LoadV40WreckSprites()
    {
        Sprite[] sprites = new Sprite[V40WreckPaths.Length];
        for (int i = 0; i < V40WreckPaths.Length; i++)
        {
            sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(V40WreckPaths[i]);
            if (sprites[i] == null)
            {
                throw new System.IO.FileNotFoundException("Missing V40 wreck sprite", V40WreckPaths[i]);
            }
        }

        return sprites;
    }

    private static Sprite[] LoadSprites(string[] paths, string identity)
    {
        Sprite[] sprites = new Sprite[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(paths[i]);
            if (sprites[i] == null)
            {
                throw new FileNotFoundException("Missing " + identity, paths[i]);
            }
        }

        return sprites;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folder)?.Replace("\\", "/");
        string name = Path.GetFileName(folder);
        if (string.IsNullOrEmpty(parent) || AssetDatabase.IsValidFolder(parent))
        {
            AssetDatabase.CreateFolder(string.IsNullOrEmpty(parent) ? "Assets" : parent, name);
        }
    }

    private static void AddSceneToBuildSettings(string scenePath)
    {
        EditorBuildSettingsScene[] currentScenes = EditorBuildSettings.scenes;
        for (int i = 0; i < currentScenes.Length; i++)
        {
            if (currentScenes[i].path == scenePath)
            {
                currentScenes[i].enabled = true;
                EditorBuildSettings.scenes = currentScenes;
                return;
            }
        }

        EditorBuildSettingsScene[] nextScenes = new EditorBuildSettingsScene[currentScenes.Length + 1];
        for (int i = 0; i < currentScenes.Length; i++)
        {
            nextScenes[i] = currentScenes[i];
        }

        nextScenes[nextScenes.Length - 1] = new EditorBuildSettingsScene(scenePath, true);
        EditorBuildSettings.scenes = nextScenes;
    }
}
