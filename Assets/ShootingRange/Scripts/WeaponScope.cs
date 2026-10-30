using UnityEngine;

/// <summary>
/// Оптический прицел (2x / 4x). Внутри прицела стоит камера, которая рендерит
/// в Render Texture (как зеркало из лабораторной №3), а линза показывает эту
/// текстуру через шейдер ShootingRange/ScopeLens с сеткой.
/// Камера включается только когда прицел установлен на автомат или в руке —
/// в остальное время она ничего не рендерит и не грузит видеокарту.
/// </summary>
public class WeaponScope : SocketableItem
{
    [Header("Оптика")]
    public float magnification = 2f;
    [Tooltip("Угол обзора, который делится на кратность (24 / 4x = 6°)")]
    public float baseFieldOfView = 24f;
    public int textureSize = 512;

    [Header("Ссылки")]
    public Camera scopeCamera;
    public Renderer lensRenderer;

    RenderTexture renderTexture;
    Material lensMaterial;
    bool mounted;
    bool lastActive = true;

    public bool IsMounted => mounted;
    public Transform OpticalOrigin => scopeCamera != null ? scopeCamera.transform : transform;

    protected override void Awake()
    {
        base.Awake();
        if (scopeCamera == null) return;

        renderTexture = new RenderTexture(textureSize, textureSize, 16, RenderTextureFormat.Default)
        {
            name = name + "_RT",
            antiAliasing = 1
        };
        renderTexture.Create();

        scopeCamera.targetTexture = renderTexture;
        scopeCamera.stereoTargetEye = StereoTargetEyeMask.None; // обычная (не VR) камера
        scopeCamera.fieldOfView = baseFieldOfView / Mathf.Max(1f, magnification);
        scopeCamera.allowHDR = false;
        scopeCamera.allowMSAA = false;

        // Не рисуем в прицеле сам автомат и руки игрока
        int grabLayer = LayerMask.NameToLayer(RangePrimitives.GrabbableLayerName);
        int playerLayer = LayerMask.NameToLayer(RangePrimitives.PlayerLayerName);
        if (grabLayer >= 0) scopeCamera.cullingMask &= ~(1 << grabLayer);
        if (playerLayer >= 0) scopeCamera.cullingMask &= ~(1 << playerLayer);

        if (lensRenderer != null)
        {
            lensMaterial = lensRenderer.material;
            lensMaterial.SetTexture("_MainTex", renderTexture);
        }
    }

    public void SetMounted(bool value)
    {
        mounted = value;
    }

    void LateUpdate()
    {
        if (scopeCamera == null) return;
        bool active = mounted || IsHeldByHand;
        if (active == lastActive) return;

        lastActive = active;
        scopeCamera.enabled = active;
        if (lensMaterial != null) lensMaterial.SetFloat("_Active", active ? 1f : 0f);
    }

    void OnDestroy()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
        if (lensMaterial != null) Destroy(lensMaterial);
    }
}
