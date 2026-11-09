using UnityEngine;

/// <summary>
/// Sound effects made in code (no audio files): hit, bounce, goal boom, countdown beeps and the ball's hum.
/// </summary>
public static class KeeperSfx
{
    const int Rate = 44100;

    static AudioClip hit, bounce, goal, beep, go, hum;

    public static AudioClip Hit => hit != null ? hit : hit = Make("KeeperHit", 0.22f, HitSample);
    public static AudioClip Bounce => bounce != null ? bounce : bounce = Make("KeeperBounce", 0.08f, BounceSample);
    public static AudioClip Goal => goal != null ? goal : goal = Make("KeeperGoal", 1.8f, GoalSample);
    public static AudioClip Beep => beep != null ? beep : beep = Make("KeeperBeep", 0.16f, t => Tone(t, 880f, 0.16f));
    public static AudioClip Go => go != null ? go : go = Make("KeeperGo", 0.4f, t => Tone(t, 1320f, 0.4f));
    public static AudioClip Hum => hum != null ? hum : hum = Make("KeeperHum", 1f, HumSample);

    static uint noiseState = 22222u;

    static float Noise()
    {
        noiseState ^= noiseState << 13;
        noiseState ^= noiseState >> 17;
        noiseState ^= noiseState << 5;
        return (noiseState & 0xFFFF) / 32767.5f - 1f;
    }

    static AudioClip Make(string name, float seconds, System.Func<float, float> sample)
    {
        int n = Mathf.CeilToInt(seconds * Rate);
        float[] data = new float[n];
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i / (float)Rate), -1f, 1f);
        AudioClip clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float HitSample(float t)
    {
        float f = Mathf.Lerp(160f, 520f, Mathf.Exp(-t * 30f));
        float body = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 16f);
        float crack = Noise() * Mathf.Exp(-t * 55f);
        return body * 0.7f + crack * 0.5f;
    }

    static float BounceSample(float t) =>
        Mathf.Sin(2f * Mathf.PI * 950f * t) * Mathf.Exp(-t * 70f) * 0.6f + Noise() * Mathf.Exp(-t * 110f) * 0.3f;

    static float lowNoise;

    static float GoalSample(float t)
    {
        lowNoise = Mathf.Lerp(lowNoise, Noise(), 0.08f);
        float boom = Mathf.Sin(2f * Mathf.PI * (55f + 50f * Mathf.Exp(-t * 7f)) * t) * Mathf.Exp(-t * 2.6f);
        float rumble = lowNoise * 2.2f * Mathf.Exp(-t * 2f);
        float env = Mathf.Clamp01(t * 6f) * Mathf.Clamp01((1.8f - t) * 1.5f);
        float chord = (Mathf.Sin(2f * Mathf.PI * 523.25f * t) + Mathf.Sin(2f * Mathf.PI * 659.25f * t) + Mathf.Sin(2f * Mathf.PI * 783.99f * t)) * 0.12f * env;
        return boom * 0.8f + rumble * 0.5f + chord;
    }

    static float Tone(float t, float f, float length)
    {
        float env = Mathf.Clamp01(t * 200f) * Mathf.Clamp01((length - t) * 30f);
        return (Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * f * t)) * 0.4f * env;
    }

    // whole-number frequencies so the 1 second clip loops without a click
    static float HumSample(float t) =>
        Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.18f + Mathf.Sin(2f * Mathf.PI * 331f * t) * 0.08f;

    public static void PlayAt(AudioClip clip, Vector3 pos, float volume, float pitch)
    {
        if (clip == null) return;
        GameObject go = new GameObject("KeeperSfx");
        go.transform.position = pos;
        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume;
        src.pitch = pitch;
        src.spatialBlend = 1f;
        src.minDistance = 1.5f;
        src.maxDistance = 40f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
    }

    public static void Play2D(AudioClip clip, float volume)
    {
        if (clip == null) return;
        AudioSource src = KeeperFx.Instance != null ? KeeperFx.Instance.uiAudio : null;
        if (src != null) src.PlayOneShot(clip, volume);
        else if (Camera.main != null) PlayAt(clip, Camera.main.transform.position, volume, 1f);
    }
}
