using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// JsonUtility populates these manifest DTO fields through reflection, so the
// C# compiler cannot observe their assignments even though Unity performs them.
#pragma warning disable 0649

/// <summary>
/// Builds deterministic AnimationClip assets from the audited V5 JSON manifest.
/// It deliberately does not modify scenes, prefabs, AnimatorControllers, or the
/// current prototype controller, which still owns its SpriteRenderer at runtime.
/// </summary>
public static class TideProductionAnimationBuilder
{
    private const string SpecPath =
        "Assets/Art/GeneratedAI/ProductionR03R06V5/animation-build-spec.json";
    private const string RequiredOutputRoot =
        "Assets/Art/GeneratedAI/ProductionR03R06V5/Unity/Animation";

    [Serializable]
    private sealed class AnimationBuildSpec
    {
        public int version;
        public string outputRoot;
        public SpriteClipDefinition[] spriteClips;
        public TransformClipDefinition[] transformClips;
    }

    [Serializable]
    private sealed class SpriteClipDefinition
    {
        public string name;
        public string outputPath;
        public string bindingPath;
        public float frameDurationSeconds;
        public float sampleRate;
        public bool loop;
        public float[] pivot;
        public int[] canvas;
        public string[] frames;
    }

    [Serializable]
    private sealed class TransformClipDefinition
    {
        public string name;
        public string outputPath;
        public float durationSeconds;
        public float sampleRate;
        public bool loop;
        public CurveDefinition[] curves;
    }

    [Serializable]
    private sealed class CurveDefinition
    {
        public string path;
        public string property;
        public FloatKeyDefinition[] keys;
    }

    [Serializable]
    private sealed class FloatKeyDefinition
    {
        public float time;
        public float value;
    }

    [MenuItem("Tide/Production Art/Validate R03 R06 Animation Sources")]
    public static void ValidateSources()
    {
        AnimationBuildSpec spec = LoadSpec();
        List<string> failures = Validate(spec, true);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Tide animation source validation failed:\n- " + string.Join("\n- ", failures));
        }

        Debug.Log(
            $"Validated Tide animation sources: {spec.spriteClips.Length} sprite clips and " +
            $"{spec.transformClips.Length} transform clips are ready to build.");
    }

    [MenuItem("Tide/Production Art/Build R03 R06 Animation Clips")]
    public static void BuildAll()
    {
        AnimationBuildSpec spec = LoadSpec();
        List<string> failures = Validate(spec, true);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Tide animation build was cancelled because validation failed:\n- " +
                string.Join("\n- ", failures));
        }

        EnsureAssetFolder(spec.outputRoot);
        for (int i = 0; i < spec.spriteClips.Length; i++)
        {
            BuildSpriteClip(spec.spriteClips[i]);
        }

        for (int i = 0; i < spec.transformClips.Length; i++)
        {
            BuildTransformClip(spec.transformClips[i]);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            $"Built {spec.spriteClips.Length + spec.transformClips.Length} Tide AnimationClips " +
            $"under {spec.outputRoot}. No scene or prefab was modified.");
    }

    private static AnimationBuildSpec LoadSpec()
    {
        if (!File.Exists(SpecPath))
        {
            throw new FileNotFoundException("Tide V5 animation build specification is missing.", SpecPath);
        }

        AnimationBuildSpec spec = JsonUtility.FromJson<AnimationBuildSpec>(File.ReadAllText(SpecPath));
        if (spec == null)
        {
            throw new InvalidOperationException("Unity could not parse " + SpecPath + ".");
        }

        return spec;
    }

    private static List<string> Validate(AnimationBuildSpec spec, bool requireImportedSprites)
    {
        List<string> failures = new List<string>();
        if (spec.version != 5)
        {
            failures.Add("Expected build specification version 5, found " + spec.version + ".");
        }

        if (!string.Equals(spec.outputRoot, RequiredOutputRoot, StringComparison.Ordinal))
        {
            failures.Add("Output root is outside the approved V5 folder: " + spec.outputRoot);
        }

        if (spec.spriteClips == null || spec.spriteClips.Length == 0)
        {
            failures.Add("No sprite clips are defined.");
        }

        if (spec.transformClips == null || spec.transformClips.Length == 0)
        {
            failures.Add("No transform clips are defined.");
        }

        HashSet<string> outputPaths = new HashSet<string>(StringComparer.Ordinal);
        if (spec.spriteClips != null)
        {
            for (int i = 0; i < spec.spriteClips.Length; i++)
            {
                ValidateSpriteClip(spec.spriteClips[i], requireImportedSprites, outputPaths, failures);
            }
        }

        if (spec.transformClips != null)
        {
            for (int i = 0; i < spec.transformClips.Length; i++)
            {
                ValidateTransformClip(spec.transformClips[i], outputPaths, failures);
            }
        }

        return failures;
    }

    private static void ValidateSpriteClip(
        SpriteClipDefinition definition,
        bool requireImportedSprites,
        HashSet<string> outputPaths,
        List<string> failures)
    {
        if (definition == null)
        {
            failures.Add("A sprite clip entry is null.");
            return;
        }

        ValidateOutputPath(definition.name, definition.outputPath, outputPaths, failures);
        if (definition.frameDurationSeconds <= 0f || definition.sampleRate <= 0f)
        {
            failures.Add(definition.name + " has invalid frame timing.");
        }

        if (definition.frames == null || definition.frames.Length == 0)
        {
            failures.Add(definition.name + " has no sprite frames.");
            return;
        }

        for (int i = 0; i < definition.frames.Length; i++)
        {
            string framePath = definition.frames[i];
            if (!File.Exists(framePath))
            {
                failures.Add(definition.name + " is missing frame " + framePath + ".");
                continue;
            }

            if (requireImportedSprites && AssetDatabase.LoadAssetAtPath<Sprite>(framePath) == null)
            {
                failures.Add(definition.name + " frame is not imported as a single Sprite: " + framePath);
            }
        }
    }

    private static void ValidateTransformClip(
        TransformClipDefinition definition,
        HashSet<string> outputPaths,
        List<string> failures)
    {
        if (definition == null)
        {
            failures.Add("A transform clip entry is null.");
            return;
        }

        ValidateOutputPath(definition.name, definition.outputPath, outputPaths, failures);
        if (definition.durationSeconds <= 0f || definition.sampleRate <= 0f)
        {
            failures.Add(definition.name + " has invalid duration or sample rate.");
        }

        if (definition.curves == null || definition.curves.Length == 0)
        {
            failures.Add(definition.name + " has no transform curves.");
            return;
        }

        for (int i = 0; i < definition.curves.Length; i++)
        {
            CurveDefinition curve = definition.curves[i];
            if (curve == null || curve.keys == null || curve.keys.Length < 2)
            {
                failures.Add(definition.name + " contains a curve with fewer than two keys.");
                continue;
            }

            float previousTime = -1f;
            for (int keyIndex = 0; keyIndex < curve.keys.Length; keyIndex++)
            {
                if (curve.keys[keyIndex].time <= previousTime)
                {
                    failures.Add(definition.name + " curve " + curve.property + " has unordered keys.");
                    break;
                }

                previousTime = curve.keys[keyIndex].time;
            }

            FloatKeyDefinition first = curve.keys[0];
            FloatKeyDefinition last = curve.keys[curve.keys.Length - 1];
            if (Mathf.Abs(first.time) > 0.0001f ||
                Mathf.Abs(last.time - definition.durationSeconds) > 0.0001f)
            {
                failures.Add(definition.name + " curve " + curve.property + " does not span the clip.");
            }

            if (definition.loop && Mathf.Abs(first.value - last.value) > 0.0001f)
            {
                failures.Add(definition.name + " loop endpoints differ on " + curve.property + ".");
            }
        }
    }

    private static void ValidateOutputPath(
        string clipName,
        string outputPath,
        HashSet<string> outputPaths,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(clipName))
        {
            failures.Add("A clip has no name.");
        }

        if (string.IsNullOrWhiteSpace(outputPath) ||
            !outputPath.StartsWith(RequiredOutputRoot + "/", StringComparison.Ordinal) ||
            !outputPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add((clipName ?? "Unnamed clip") + " has an unsafe output path: " + outputPath);
            return;
        }

        if (!outputPaths.Add(outputPath))
        {
            failures.Add("Duplicate animation output path: " + outputPath);
        }
    }

    private static void BuildSpriteClip(SpriteClipDefinition definition)
    {
        AnimationClip clip = LoadOrCreateClip(definition.outputPath, definition.name);
        ClearClipCurves(clip);
        clip.frameRate = definition.sampleRate;

        ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[definition.frames.Length + 1];
        for (int i = 0; i < definition.frames.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe
            {
                time = i * definition.frameDurationSeconds,
                value = AssetDatabase.LoadAssetAtPath<Sprite>(definition.frames[i])
            };
        }

        // The duplicate first frame supplies the final interval without holding
        // the last drawing for one frame too few. Looping then wraps at this key.
        keys[keys.Length - 1] = new ObjectReferenceKeyframe
        {
            time = definition.frames.Length * definition.frameDurationSeconds,
            value = keys[0].value
        };

        EditorCurveBinding binding = EditorCurveBinding.PPtrCurve(
            definition.bindingPath ?? string.Empty,
            typeof(SpriteRenderer),
            "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        SetLoopSettings(clip, definition.loop);
        EditorUtility.SetDirty(clip);
    }

    private static void BuildTransformClip(TransformClipDefinition definition)
    {
        AnimationClip clip = LoadOrCreateClip(definition.outputPath, definition.name);
        ClearClipCurves(clip);
        clip.frameRate = definition.sampleRate;

        for (int curveIndex = 0; curveIndex < definition.curves.Length; curveIndex++)
        {
            CurveDefinition source = definition.curves[curveIndex];
            Keyframe[] keys = new Keyframe[source.keys.Length];
            for (int keyIndex = 0; keyIndex < source.keys.Length; keyIndex++)
            {
                keys[keyIndex] = new Keyframe(source.keys[keyIndex].time, source.keys[keyIndex].value);
            }

            AnimationCurve curve = new AnimationCurve(keys);
            for (int keyIndex = 0; keyIndex < curve.length; keyIndex++)
            {
                AnimationUtility.SetKeyLeftTangentMode(
                    curve, keyIndex, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(
                    curve, keyIndex, AnimationUtility.TangentMode.ClampedAuto);
            }

            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                source.path ?? string.Empty,
                typeof(Transform),
                source.property);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        SetLoopSettings(clip, definition.loop);
        clip.EnsureQuaternionContinuity();
        EditorUtility.SetDirty(clip);
    }

    private static AnimationClip LoadOrCreateClip(string assetPath, string clipName)
    {
        EnsureAssetFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
        if (clip != null)
        {
            clip.name = clipName;
            return clip;
        }

        clip = new AnimationClip { name = clipName };
        AssetDatabase.CreateAsset(clip, assetPath);
        return clip;
    }

    private static void ClearClipCurves(AnimationClip clip)
    {
        EditorCurveBinding[] floatBindings = AnimationUtility.GetCurveBindings(clip);
        for (int i = 0; i < floatBindings.Length; i++)
        {
            AnimationUtility.SetEditorCurve(clip, floatBindings[i], null);
        }

        EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        for (int i = 0; i < objectBindings.Length; i++)
        {
            AnimationUtility.SetObjectReferenceCurve(clip, objectBindings[i], null);
        }
    }

    private static void SetLoopSettings(AnimationClip clip, bool loop)
    {
        clip.wrapMode = loop ? WrapMode.Loop : WrapMode.Default;
        SerializedObject serializedClip = new SerializedObject(clip);
        SerializedProperty settings = serializedClip.FindProperty("m_AnimationClipSettings");
        SerializedProperty loopTime = settings?.FindPropertyRelative("m_LoopTime");
        if (loopTime == null)
        {
            throw new InvalidOperationException("Unity did not expose m_LoopTime for " + clip.name + ".");
        }

        loopTime.boolValue = loop;
        serializedClip.ApplyModifiedPropertiesWithoutUndo();
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
