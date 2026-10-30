using UnityEngine;
using UnityEngine.UI;

/// <summary>Табло над тиром (World Space Canvas).</summary>
public class ScoreBoard : MonoBehaviour
{
    public static ScoreBoard Instance { get; private set; }

    public Text centerHitsText;
    public Text statsText;

    public int CenterHits { get; private set; }
    public int Hits { get; private set; }
    public int Shots { get; private set; }

    void Awake()
    {
        Instance = this;
        Refresh();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void RegisterShot()
    {
        Shots++;
        Refresh();
    }

    public void RegisterHit(bool center)
    {
        Hits++;
        if (center) CenterHits++;
        Refresh();
    }

    public void ResetScore()
    {
        CenterHits = Hits = Shots = 0;
        Refresh();
    }

    void Refresh()
    {
        if (centerHitsText != null)
            centerHitsText.text = CenterHits.ToString();
        if (statsText != null)
        {
            int accuracy = Shots > 0 ? Mathf.RoundToInt(100f * Hits / Shots) : 0;
            statsText.text = "Попаданий: " + Hits + "    Выстрелов: " + Shots + "    Точность: " + accuracy + "%";
        }
    }
}
