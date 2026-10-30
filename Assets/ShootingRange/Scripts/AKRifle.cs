using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Автомат Калашникова.
/// Grip (Select) — взять, Trigger (Activate) — стрельба (как Activate Usage из лабораторной №2).
/// Стрельба — мгновенный луч (hitscan) без снарядов, поэтому нагрузка минимальна.
/// Вторая рука может взяться за цевьё (двуручный хват).
/// </summary>
[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public class AKRifle : MonoBehaviour
{
    [Header("Ссылки")]
    public Transform muzzle;
    public Transform ironSightOrigin;
    public Transform recoilVisual;
    public MagazineSocket magazineSocket;
    public ScopeSocket scopeSocket;
    public GameObject muzzleFlash;
    public TextMesh ammoCounter;
    public CasingEjector casingEjector;
    public Material impactMaterial;

    [Header("Стрельба")]
    public bool fullAuto = true;
    public float roundsPerMinute = 600f;
    [Tooltip("Дистанция пристрелки: на ней пуля попадает точно в точку прицеливания")]
    public float zeroDistance = 20f;
    public float maxRange = 300f;
    public float spreadDegrees = 0.1f;
    public LayerMask hitMask = ~0;
    [Tooltip("Патрон уже в патроннике при старте")]
    public bool startChambered = true;

    [Header("Отдача")]
    public float recoilKickBack = 0.02f;
    public float recoilKickUp = 2.5f;
    public float recoilReturnSpeed = 18f;

    [Header("Вибрация контроллера")]
    public float hapticAmplitude = 0.7f;
    public float hapticDuration = 0.05f;

    [Header("Следы от пуль")]
    public int maxImpacts = 40;
    public float impactSize = 0.025f;

    UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab;
    AudioSource audioSource;
    Magazine currentMagazine;
    WeaponScope currentScope;

    bool chambered;
    bool triggerHeld;
    bool dryFireDone;
    float nextShotTime;

    Vector3 visualBasePosition;
    Quaternion visualBaseRotation;
    float recoilBack;
    float recoilPitch;
    float flashOffTime;

    Transform impactRoot;
    Transform[] impacts;
    int impactIndex;
    string lastCounterText;

    public Magazine CurrentMagazine => currentMagazine;
    public WeaponScope CurrentScope => currentScope;

    void Awake()
    {
        grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        audioSource = GetComponent<AudioSource>();
        chambered = startChambered;

        if (recoilVisual != null)
        {
            visualBasePosition = recoilVisual.localPosition;
            visualBaseRotation = recoilVisual.localRotation;
        }
        if (muzzleFlash != null) muzzleFlash.SetActive(false);

        grab.activated.AddListener(OnActivated);
        grab.deactivated.AddListener(OnDeactivated);
        grab.selectExited.AddListener(OnReleased);
        grab.selectEntered.AddListener(args => Debug.Log("[Тир VR] Автомат взят: " + args.interactorObject.transform.name));

        if (magazineSocket != null)
        {
            magazineSocket.selectEntered.AddListener(OnMagazineAttached);
            magazineSocket.selectExited.AddListener(OnMagazineDetached);
        }
        if (scopeSocket != null)
        {
            scopeSocket.selectEntered.AddListener(OnScopeAttached);
            scopeSocket.selectExited.AddListener(OnScopeDetached);
        }
    }

    void Start()
    {
        // Магазины и прицелы не должны физически толкать автомат, когда вставлены в него
        var ownColliders = GetComponentsInChildren<Collider>(true);
        foreach (var item in FindObjectsOfType<SocketableItem>(true))
        {
            foreach (var itemCollider in item.GetComponentsInChildren<Collider>(true))
            {
                foreach (var own in ownColliders)
                {
                    if (!own.isTrigger)
                        Physics.IgnoreCollision(own, itemCollider, true);
                }
            }
        }
        UpdateAmmoCounter();
    }

    // ---------- ввод ----------

    bool IsPrimaryHand(object interactor)
    {
        var first = grab.firstInteractorSelecting;
        return first == null || ReferenceEquals(first, interactor);
    }

    void OnActivated(ActivateEventArgs args)
    {
        if (!IsPrimaryHand(args.interactorObject)) return;
        triggerHeld = true;
        dryFireDone = false;
    }

    void OnDeactivated(DeactivateEventArgs args)
    {
        if (!IsPrimaryHand(args.interactorObject)) return;
        triggerHeld = false;
    }

    void OnReleased(SelectExitEventArgs args)
    {
        if (!grab.isSelected) triggerHeld = false;
    }

    // ---------- магазин / прицел ----------

    void OnMagazineAttached(SelectEnterEventArgs args)
    {
        currentMagazine = args.interactableObject.transform.GetComponent<Magazine>();
        // Упрощённо: при установке магазина патрон досылается автоматически
        if (!chambered && currentMagazine != null && currentMagazine.TakeRound())
            chambered = true;
        PlayClip(ProceduralAudio.MagazineClick, 0.8f);
    }

    void OnMagazineDetached(SelectExitEventArgs args)
    {
        if (currentMagazine != null && args.interactableObject.transform == currentMagazine.transform)
            currentMagazine = null;
        PlayClip(ProceduralAudio.MagazineClick, 0.6f);
    }

    void OnScopeAttached(SelectEnterEventArgs args)
    {
        currentScope = args.interactableObject.transform.GetComponent<WeaponScope>();
        if (currentScope != null) currentScope.SetMounted(true);
        PlayClip(ProceduralAudio.MagazineClick, 0.5f);
    }

    void OnScopeDetached(SelectExitEventArgs args)
    {
        var scope = args.interactableObject.transform.GetComponent<WeaponScope>();
        if (scope != null) scope.SetMounted(false);
        if (scope == currentScope) currentScope = null;
    }

    /// <summary>Спуск с клавиатуры/мыши (захват на ПК через SimulatorGrabAssist).</summary>
    public void SetTrigger(bool pressed)
    {
        if (pressed && !triggerHeld) dryFireDone = false;
        triggerHeld = pressed;
    }

    /// <summary>Вызывается кнопкой "Сброс": снова дослать патрон.</summary>
    public void Rechamber()
    {
        if (chambered) return;
        if (currentMagazine != null && currentMagazine.TakeRound())
            chambered = true;
    }

    // ---------- стрельба ----------

    void Update()
    {
        if (triggerHeld && Time.time >= nextShotTime)
            TryFire();

        float k = Time.deltaTime * recoilReturnSpeed;
        recoilBack = Mathf.Lerp(recoilBack, 0f, k);
        recoilPitch = Mathf.Lerp(recoilPitch, 0f, k);
        if (recoilVisual != null)
        {
            recoilVisual.localPosition = visualBasePosition + Vector3.back * recoilBack;
            recoilVisual.localRotation = visualBaseRotation * Quaternion.Euler(-recoilPitch, 0f, 0f);
        }

        if (muzzleFlash != null && muzzleFlash.activeSelf && Time.time >= flashOffTime)
            muzzleFlash.SetActive(false);

        UpdateAmmoCounter();
    }

    void TryFire()
    {
        nextShotTime = Time.time + 60f / Mathf.Max(1f, roundsPerMinute);

        if (!chambered)
        {
            if (!dryFireDone)
            {
                PlayClip(ProceduralAudio.DryClick, 1f);
                dryFireDone = true;
            }
            if (!fullAuto) triggerHeld = false;
            return;
        }

        Fire();
        chambered = currentMagazine != null && currentMagazine.TakeRound();
        if (!fullAuto) triggerHeld = false;
    }

    void Fire()
    {
        if (ScoreBoard.Instance != null) ScoreBoard.Instance.RegisterShot();

        // Линия прицеливания (мушка или оптика) параллельна стволу и выше него,
        // поэтому направляем выстрел так, чтобы он пересёк её на дистанции пристрелки.
        Vector3 bore = muzzle.forward;
        Transform sight = currentScope != null ? currentScope.OpticalOrigin : ironSightOrigin;
        Vector3 direction = bore;
        if (sight != null)
            direction = (sight.position + bore * zeroDistance - muzzle.position).normalized;
        if (spreadDegrees > 0f)
            direction = (direction + Random.insideUnitSphere * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad)).normalized;

        // Мишени двигаются через transform — синхронизируем коллайдеры перед лучом
        Physics.SyncTransforms();

        if (Physics.Raycast(muzzle.position, direction, out RaycastHit hit, maxRange, hitMask, QueryTriggerInteraction.Ignore))
        {
            var target = hit.collider.GetComponentInParent<ShootingTarget>();
            if (target != null) target.RegisterHit(hit.point);
            SpawnImpact(hit, target != null ? target.transform : null);

            if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                hit.rigidbody.AddForceAtPosition(direction * 2f, hit.point, ForceMode.Impulse);
        }

        PlayClip(ProceduralAudio.Gunshot, 1f);

        if (muzzleFlash != null)
        {
            muzzleFlash.SetActive(true);
            muzzleFlash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            flashOffTime = Time.time + 0.035f;
        }

        recoilBack += recoilKickBack;
        recoilPitch += recoilKickUp;

        if (casingEjector != null) casingEjector.Eject();

        SendHaptics();
    }

    void SendHaptics()
    {
        var selecting = grab.interactorsSelecting;
        for (int i = 0; i < selecting.Count; i++)
        {
            if (selecting[i] is UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInputInteractor controllerInteractor && controllerInteractor.xrController != null)
                controllerInteractor.xrController.SendHapticImpulse(hapticAmplitude, hapticDuration);
        }
    }

    void SpawnImpact(RaycastHit hit, Transform parent)
    {
        if (impactMaterial == null || maxImpacts <= 0) return;

        if (impacts == null)
        {
            impactRoot = new GameObject("BulletImpacts").transform;
            impacts = new Transform[maxImpacts];
            for (int i = 0; i < impacts.Length; i++)
            {
                var quad = RangePrimitives.Create(PrimitiveType.Quad, "Impact", impactRoot, Vector3.zero, Vector3.zero,
                    Vector3.one * impactSize, impactMaterial);
                quad.SetActive(false);
                impacts[i] = quad.transform;
            }
        }

        var t = impacts[impactIndex];
        impactIndex = (impactIndex + 1) % impacts.Length;

        t.SetParent(parent != null ? parent : impactRoot, false);
        t.SetPositionAndRotation(hit.point + hit.normal * 0.004f, Quaternion.LookRotation(-hit.normal));
        float parentScale = t.parent != null ? t.parent.lossyScale.x : 1f;
        t.localScale = Vector3.one * (impactSize / Mathf.Max(0.0001f, parentScale));
        t.gameObject.SetActive(true);
    }

    void PlayClip(AudioClip clip, float volume)
    {
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip, volume);
    }

    void UpdateAmmoCounter()
    {
        if (ammoCounter == null) return;
        string text = currentMagazine != null
            ? currentMagazine.ammo + (chambered ? "+1" : "")
            : (chambered ? "1" : "0") + "\nнет маг.";
        if (text == lastCounterText) return;
        lastCounterText = text;
        ammoCounter.text = text;
    }
}
