using UnityEngine;

/// <summary>
/// Круглая мишень. Попадание считается по расстоянию от центра мишени
/// в её локальной плоскости XY. В табло идут попадания в красный центр.
/// </summary>
public class ShootingTarget : MonoBehaviour
{
    [Tooltip("Радиус всей мишени, м")]
    public float faceRadius = 0.3f;
    [Tooltip("Радиус центра (яблочка), м")]
    public float bullseyeRadius = 0.06f;

    public Renderer bullseyeRenderer;
    public Color hitFlashColor = new Color(1f, 0.95f, 0.2f);
    public float flashDuration = 0.25f;

    AudioSource audioSource;
    MaterialPropertyBlock propertyBlock;
    float flashUntil;
    bool flashing;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        propertyBlock = new MaterialPropertyBlock();
    }

    public void RegisterHit(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        float distance = new Vector2(local.x, local.y).magnitude;
        if (distance > faceRadius) return; // попали в стойку/раму

        bool center = distance <= bullseyeRadius;
        if (ScoreBoard.Instance != null) ScoreBoard.Instance.RegisterHit(center);

        if (center)
        {
            Flash();
            if (audioSource != null) audioSource.PlayOneShot(ProceduralAudio.Ding);
        }
    }

    void Flash()
    {
        if (bullseyeRenderer == null) return;
        propertyBlock.Clear();
        propertyBlock.SetColor("_BaseColor", hitFlashColor); // URP
        propertyBlock.SetColor("_Color", hitFlashColor);     // Built-in
        bullseyeRenderer.SetPropertyBlock(propertyBlock);
        flashUntil = Time.time + flashDuration;
        flashing = true;
    }

    void Update()
    {
        if (!flashing || Time.time < flashUntil) return;
        flashing = false;
        propertyBlock.Clear();
        bullseyeRenderer.SetPropertyBlock(propertyBlock);
    }
}
