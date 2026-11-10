using System;
using UnityEngine;

/// <summary>Small generated sound effects shared by the lobby and the mini-games. No audio files needed.</summary>
public static class PartySfx
{
    const int Rate = 44100;
    static AudioClip click, hover, beep, go, win, pop, bonk, squeak, miss, tick;
    static AudioSource ui;

    public static AudioClip Click => click ??= Make("Click", 0.07f, t => Mathf.Sin(6.283f * (900f - 3000f * t) * t) * Env(t, 0.07f, 0.002f));
    public static AudioClip Hover => hover ??= Make("Hover", 0.04f, t => Mathf.Sin(6.283f * 1400f * t) * Env(t, 0.04f, 0.004f) * 0.5f);
    public static AudioClip Beep => beep ??= Make("Beep", 0.14f, t => Mathf.Sin(6.283f * 660f * t) * Env(t, 0.14f, 0.005f));
    public static AudioClip Go => go ??= Make("Go", 0.35f, t => (Mathf.Sin(6.283f * 990f * t) + 0.5f * Mathf.Sin(6.283f * 1485f * t)) * 0.6f * Env(t, 0.35f, 0.005f));
    public static AudioClip Tick => tick ??= Make("Tick", 0.05f, t => Mathf.Sin(6.283f * 1900f * t) * Env(t, 0.05f, 0.001f) * 0.7f);
    public static AudioClip Pop => pop ??= Make("Pop", 0.16f, t => Mathf.Sin(6.283f * (260f + 1900f * t) * t) * Env(t, 0.16f, 0.004f));
    public static AudioClip Squeak => squeak ??= Make("Squeak", 0.22f, t => Mathf.Sin(6.283f * (1500f - 2600f * t + 500f * Mathf.Sin(40f * t)) * t) * Env(t, 0.22f, 0.006f) * 0.8f);
    public static AudioClip Miss => miss ??= Make("Miss", 0.12f, t => Noise(t) * Env(t, 0.12f, 0.002f) * 0.5f * Mathf.Exp(-t * 30f) + Mathf.Sin(6.283f * 140f * t) * Mathf.Exp(-t * 28f) * 0.7f);
    public static AudioClip Bonk => bonk ??= Make("Bonk", 0.28f, t =>
        (Mathf.Sin(6.283f * (210f - 240f * t) * t) * Mathf.Exp(-t * 14f) + Noise(t) * Mathf.Exp(-t * 60f) * 0.6f) * 0.9f);
    public static AudioClip Win => win ??= Make("Win", 1.1f, t =>
    {
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
        int i = Mathf.Min(3, (int)(t / 0.16f));
        float lt = t - i * 0.16f;
        float len = i == 3 ? 0.6f : 0.16f;
        return (Mathf.Sin(6.283f * notes[i] * t) + 0.35f * Mathf.Sin(6.283f * notes[i] * 2f * t)) * 0.55f * Env(lt, len, 0.006f);
    });

    static float Env(float t, float len, float attack) => Mathf.Clamp01(t / attack) * Mathf.Clamp01((len - t) / (len * 0.7f));

    static float Noise(float t)
    {
        float s = Mathf.Sin(t * 91345.17f) * 43758.5453f;
        return (s - Mathf.Floor(s)) * 2f - 1f;
    }

    static AudioClip Make(string name, float seconds, Func<float, float> f)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt(seconds * Rate));
        float[] data = new float[n];
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
        AudioClip c = AudioClip.Create("Party_" + name, n, 1, Rate, false);
        c.SetData(data, 0);
        return c;
    }

    /// <summary>Plays in the head (menus, countdowns).</summary>
    public static void Play2D(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null || !Application.isPlaying) return;
        if (ui == null)
        {
            GameObject go = new GameObject("PartySfx");
            UnityEngine.Object.DontDestroyOnLoad(go);
            ui = go.AddComponent<AudioSource>();
            ui.spatialBlend = 0f;
            ui.playOnAwake = false;
        }
        ui.pitch = pitch;
        ui.PlayOneShot(clip, volume);
    }

    /// <summary>Plays at a point in the world.</summary>
    public static void PlayAt(AudioClip clip, Vector3 point, float volume = 1f, float pitch = 1f)
    {
        if (clip == null || !Application.isPlaying) return;
        GameObject go = new GameObject("PartySfx_OneShot");
        go.transform.position = point;
        AudioSource s = go.AddComponent<AudioSource>();
        s.clip = clip;
        s.volume = volume;
        s.pitch = pitch;
        s.spatialBlend = 1f;
        s.minDistance = 0.6f;
        s.maxDistance = 12f;
        s.rolloffMode = AudioRolloffMode.Linear;
        s.Play();
        UnityEngine.Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
    }
}
