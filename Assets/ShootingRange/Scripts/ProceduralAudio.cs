using UnityEngine;

/// <summary>
/// Звуки генерируются кодом один раз и кэшируются — в проект не нужно
/// добавлять никаких аудиофайлов.
/// </summary>
public static class ProceduralAudio
{
    const int SampleRate = 44100;

    static AudioClip gunshot, dryClick, magazineClick, ding, button;

    public static AudioClip Gunshot => gunshot != null ? gunshot : (gunshot = Make("Gunshot", 0.35f, GunshotSample));
    public static AudioClip DryClick => dryClick != null ? dryClick : (dryClick = Make("DryClick", 0.05f, ClickSample));
    public static AudioClip MagazineClick => magazineClick != null ? magazineClick : (magazineClick = Make("MagazineClick", 0.09f, MagSample));
    public static AudioClip Ding => ding != null ? ding : (ding = Make("Ding", 0.6f, DingSample));
    public static AudioClip Button => button != null ? button : (button = Make("Button", 0.08f, ButtonSample));

    delegate float SampleFunc(float t, System.Random rnd);

    static AudioClip Make(string name, float seconds, SampleFunc func)
    {
        int count = Mathf.CeilToInt(SampleRate * seconds);
        var data = new float[count];
        var rnd = new System.Random(12345);
        for (int i = 0; i < count; i++)
            data[i] = Mathf.Clamp(func(i / (float)SampleRate, rnd), -1f, 1f);

        var clip = AudioClip.Create(name, count, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float Noise(System.Random rnd) => (float)(rnd.NextDouble() * 2.0 - 1.0);

    static float GunshotSample(float t, System.Random rnd)
    {
        float crack = Noise(rnd) * Mathf.Exp(-t * 30f);
        float boom = Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 14f);
        float tail = Noise(rnd) * 0.25f * Mathf.Exp(-t * 6f);
        return crack * 0.9f + boom * 0.8f + tail;
    }

    static float ClickSample(float t, System.Random rnd)
    {
        return Noise(rnd) * Mathf.Exp(-t * 250f) * 0.8f + Mathf.Sin(2f * Mathf.PI * 2400f * t) * Mathf.Exp(-t * 300f) * 0.4f;
    }

    static float MagSample(float t, System.Random rnd)
    {
        float first = Noise(rnd) * Mathf.Exp(-t * 200f);
        float t2 = Mathf.Max(0f, t - 0.04f);
        float second = t > 0.04f ? Noise(rnd) * Mathf.Exp(-t2 * 220f) : 0f;
        return (first + second) * 0.7f + Mathf.Sin(2f * Mathf.PI * 900f * t) * Mathf.Exp(-t * 80f) * 0.3f;
    }

    static float DingSample(float t, System.Random rnd)
    {
        float env = Mathf.Exp(-t * 6f);
        return (Mathf.Sin(2f * Mathf.PI * 1320f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 2640f * t) * 0.2f) * env;
    }

    static float ButtonSample(float t, System.Random rnd)
    {
        return Mathf.Sin(2f * Mathf.PI * 600f * t) * Mathf.Exp(-t * 60f) * 0.6f;
    }
}
