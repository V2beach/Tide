using UnityEngine;

/// <summary>
/// 第一切片的轻量程序化声音层。它只消费世界状态，不拥有潮汐、天气或移动规则，
/// 因而不会把音效节奏反向写进玩法。短合成片段避免为原型常驻大批音频文件。
/// </summary>
[DisallowMultipleComponent]
public sealed class TideAudioController : MonoBehaviour
{
    private static AudioClip seaLoopClip;
    private static AudioClip stepClip;
    private static AudioClip pickupClip;
    private static AudioClip netLoadClip;
    private static AudioClip ropeBreakClip;
    private static AudioClip stormShelfBreakClip;

    private AudioSource seaSource;
    private AudioSource locomotionSource;
    private AudioSource cueSource;
    private AudioLowPassFilter seaLowPass;
    private float locomotionSpeed01;
    private float submersion01;
    private bool swimming;
    private bool climbing;
    private float stepClock;

    public void ConfigureStandalone(float warmth01, float unease01, float muffle01, float cueBrightness01)
    {
        EnsureSources();
        seaSource.volume = Mathf.Lerp(0.035f, 0.11f, Mathf.Clamp01(unease01));
        seaLowPass.cutoffFrequency = Mathf.Lerp(7600f, 1800f, Mathf.Clamp01(muffle01));
        cueSource.pitch = Mathf.Lerp(0.92f, 1.06f, Mathf.Clamp01(cueBrightness01));
        cueSource.volume = Mathf.Lerp(0.24f, 0.38f, Mathf.Clamp01(warmth01));
    }

    public void SetStandaloneTideState(float tideStrength01, float tideHeight01, float storm01)
    {
        EnsureSources();
        float energy01 = Mathf.Clamp01(tideStrength01 * 0.45f + tideHeight01 * 0.2f + storm01 * 0.65f);
        seaSource.pitch = Mathf.Lerp(0.84f, 1.13f, energy01);
        seaSource.volume = Mathf.Max(seaSource.volume, Mathf.Lerp(0.03f, 0.14f, energy01));
    }

    public void SetStandaloneLocomotionState(
        float speed01,
        float playerSubmersion01,
        bool playerSwimming,
        bool playerClimbing)
    {
        locomotionSpeed01 = Mathf.Clamp01(speed01);
        submersion01 = Mathf.Clamp01(playerSubmersion01);
        swimming = playerSwimming;
        climbing = playerClimbing;
    }

    public static void PlayPickupCueInScene(float intensity01)
    {
        TideAudioController controller = FindFirstObjectByType<TideAudioController>();
        controller?.PlayCue(GetPickupClip(), Mathf.Lerp(0.18f, 0.42f, Mathf.Clamp01(intensity01)), 1f);
    }

    public static void PlayNetLoadCueInScene(int loadTier, bool broke)
    {
        TideAudioController controller = FindFirstObjectByType<TideAudioController>();
        if (controller == null)
        {
            return;
        }

        float tier01 = Mathf.Clamp01(loadTier / 3f);
        controller.PlayCue(
            broke ? GetRopeBreakClip() : GetNetLoadClip(),
            broke ? 0.52f : Mathf.Lerp(0.22f, 0.4f, tier01),
            broke ? 0.86f : Mathf.Lerp(1.08f, 0.9f, tier01));
    }

    public static void PlayStormShelfBreakCueInScene(float impact01)
    {
        TideAudioController controller = FindFirstObjectByType<TideAudioController>();
        controller?.PlayCue(
            GetStormShelfBreakClip(),
            Mathf.Lerp(0.34f, 0.58f, Mathf.Clamp01(impact01)),
            Mathf.Lerp(0.94f, 0.82f, Mathf.Clamp01(impact01)));
    }

    private void Update()
    {
        EnsureSources();
        if (locomotionSpeed01 <= 0.08f)
        {
            stepClock = 0f;
            return;
        }

        float cadenceSeconds = Mathf.Lerp(0.72f, 0.34f, locomotionSpeed01);
        if (swimming)
        {
            cadenceSeconds *= 1.55f;
        }
        else if (climbing)
        {
            cadenceSeconds *= 1.18f;
        }

        stepClock += Time.unscaledDeltaTime;
        if (stepClock < cadenceSeconds)
        {
            return;
        }

        stepClock = 0f;
        locomotionSource.pitch = swimming ? 0.72f : climbing ? 0.9f : Mathf.Lerp(0.92f, 1.08f, locomotionSpeed01);
        locomotionSource.volume = Mathf.Lerp(0.05f, 0.17f, locomotionSpeed01) * Mathf.Lerp(1f, 0.62f, submersion01);
        locomotionSource.PlayOneShot(GetStepClip());
    }

    private void PlayCue(AudioClip clip, float volume, float pitch)
    {
        EnsureSources();
        cueSource.pitch = pitch;
        cueSource.PlayOneShot(clip, volume);
    }

    private void EnsureSources()
    {
        if (seaSource != null)
        {
            return;
        }

        AudioSource[] sources = GetComponents<AudioSource>();
        seaSource = sources.Length > 0 ? sources[0] : gameObject.AddComponent<AudioSource>();
        locomotionSource = sources.Length > 1 ? sources[1] : gameObject.AddComponent<AudioSource>();
        cueSource = sources.Length > 2 ? sources[2] : gameObject.AddComponent<AudioSource>();
        seaLowPass = GetComponent<AudioLowPassFilter>();
        if (seaLowPass == null)
        {
            seaLowPass = gameObject.AddComponent<AudioLowPassFilter>();
        }

        seaSource.loop = true;
        seaSource.playOnAwake = false;
        seaSource.spatialBlend = 0f;
        seaSource.clip = GetSeaLoopClip();
        locomotionSource.playOnAwake = false;
        locomotionSource.spatialBlend = 0f;
        cueSource.playOnAwake = false;
        cueSource.spatialBlend = 0f;
        if (Application.isPlaying && !seaSource.isPlaying)
        {
            seaSource.Play();
        }
    }

    private static AudioClip GetSeaLoopClip()
    {
        return seaLoopClip ??= CreateNoiseClip("TideSeaLoop", 2f, 0.42f, 0.18f);
    }

    private static AudioClip GetStepClip()
    {
        return stepClip ??= CreatePulseClip("TideStep", 0.1f, 118f, 0.35f);
    }

    private static AudioClip GetPickupClip()
    {
        return pickupClip ??= CreatePulseClip("TidePickup", 0.16f, 420f, 0.28f);
    }

    private static AudioClip GetNetLoadClip()
    {
        return netLoadClip ??= CreatePulseClip("TideNetLoad", 0.2f, 92f, 0.38f);
    }

    private static AudioClip GetRopeBreakClip()
    {
        return ropeBreakClip ??= CreateNoiseClip("TideRopeBreak", 0.24f, 0.72f, 0.82f);
    }

    private static AudioClip GetStormShelfBreakClip()
    {
        if (stormShelfBreakClip != null)
        {
            return stormShelfBreakClip;
        }

        const int sampleRate = 22050;
        const float seconds = 0.46f;
        int sampleCount = Mathf.RoundToInt(seconds * sampleRate);
        float[] data = new float[sampleCount];
        uint noiseState = 0x9e3779b9u;
        float woodNoise = 0f;
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            noiseState = noiseState * 1664525u + 1013904223u;
            float white = ((noiseState >> 8) / 16777215f) * 2f - 1f;
            woodNoise = Mathf.Lerp(white, woodNoise, 0.68f);
            float firstCrack = Mathf.Exp(-t * 46f);
            float secondCrackTime = Mathf.Max(0f, t - 0.075f);
            float secondCrack = t >= 0.075f ? Mathf.Exp(-secondCrackTime * 58f) : 0f;
            float thudTime = Mathf.Max(0f, t - 0.11f);
            float thud = t >= 0.11f
                ? Mathf.Sin(thudTime * 68f * Mathf.PI * 2f) * Mathf.Exp(-thudTime * 9.5f)
                : 0f;
            float edge = Mathf.Min(1f, i / 96f, (sampleCount - i - 1) / 256f);
            data[i] = (woodNoise * (firstCrack * 0.62f + secondCrack * 0.44f) + thud * 0.48f) *
                Mathf.Clamp01(edge);
        }

        stormShelfBreakClip = AudioClip.Create(
            "TideStormShelfBreak",
            sampleCount,
            1,
            sampleRate,
            false);
        stormShelfBreakClip.SetData(data, 0);
        return stormShelfBreakClip;
    }

    private static AudioClip CreatePulseClip(string name, float seconds, float frequency, float gain)
    {
        const int sampleRate = 22050;
        int sampleCount = Mathf.Max(1, Mathf.RoundToInt(seconds * sampleRate));
        float[] data = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Pow(1f - i / (float)sampleCount, 2f);
            data[i] = Mathf.Sin(t * frequency * Mathf.PI * 2f) * envelope * gain;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateNoiseClip(string name, float seconds, float lowMix, float gain)
    {
        const int sampleRate = 22050;
        int sampleCount = Mathf.Max(1, Mathf.RoundToInt(seconds * sampleRate));
        float[] data = new float[sampleCount];
        uint state = 0x6d2b79f5u;
        float smoothed = 0f;
        for (int i = 0; i < sampleCount; i++)
        {
            state = state * 1664525u + 1013904223u;
            float white = ((state >> 8) / 16777215f) * 2f - 1f;
            smoothed = Mathf.Lerp(white, smoothed, Mathf.Clamp01(lowMix));
            float edge = Mathf.Min(1f, i / 512f, (sampleCount - i - 1) / 512f);
            data[i] = smoothed * gain * Mathf.Clamp01(edge);
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
