using UnityEngine;


/// <summary>
/// Кнопка "Сброс" на столе: обнуляет табло и пополняет все магазины.
/// Нажимается захватом (Grip) рукой или лучом.
/// </summary>
[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable))]
public class ResetButton : MonoBehaviour
{
    public Transform buttonCap;
    public float pressDepth = 0.01f;

    AudioSource audioSource;
    Vector3 capBasePosition;
    float releaseAt;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (buttonCap != null) capBasePosition = buttonCap.localPosition;
        GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>().selectEntered.AddListener(_ => Press());
    }

    public void Press()
    {
        if (ScoreBoard.Instance != null) ScoreBoard.Instance.ResetScore();
        foreach (var magazine in FindObjectsOfType<Magazine>()) magazine.Refill();
        foreach (var rifle in FindObjectsOfType<AKRifle>()) rifle.Rechamber();

        if (audioSource != null) audioSource.PlayOneShot(ProceduralAudio.Button);
        if (buttonCap != null) buttonCap.localPosition = capBasePosition + Vector3.down * pressDepth;
        releaseAt = Time.time + 0.2f;
    }

    void Update()
    {
        if (buttonCap != null && releaseAt > 0f && Time.time >= releaseAt)
        {
            buttonCap.localPosition = capBasePosition;
            releaseAt = 0f;
        }
    }
}
