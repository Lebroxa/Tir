using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Базовый класс для предметов, которые вставляются в слоты автомата
/// (магазин, прицел). Слот принимает предмет, только если игрок держит его
/// в руке или только что отпустил — так лежащие на столе предметы
/// не "засасываются" в автомат сами.
/// </summary>
[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public abstract class SocketableItem : MonoBehaviour
{
    [Tooltip("Сколько секунд после того как предмет отпустили, его ещё можно вставить в слот")]
    public float socketGraceTime = 0.5f;

    protected UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab;
    float lastHandReleaseTime = -100f;

    public UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable Grab => grab;

    protected virtual void Awake()
    {
        grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        grab.selectExited.AddListener(OnSelectExited);
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (!(args.interactorObject is UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor))
            lastHandReleaseTime = Time.time;
    }

    /// <summary>Предмет сейчас в руке (а не в слоте).</summary>
    public bool IsHeldByHand
    {
        get
        {
            if (grab == null) return false;
            var selecting = grab.interactorsSelecting;
            for (int i = 0; i < selecting.Count; i++)
                if (!(selecting[i] is UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor))
                    return true;
            return false;
        }
    }

    bool externallyHeld;

    /// <summary>Предмет держит запасной захват на ПК (SimulatorGrabAssist), а не XRI.</summary>
    public void SetExternallyHeld(bool value)
    {
        if (externallyHeld && !value) lastHandReleaseTime = Time.time;
        externallyHeld = value;
    }

    // Пока предмет держат "запасным" захватом, слот его не забирает — вставится, когда отпустят рядом
    public bool CanBeSocketed => IsHeldByHand || (!externallyHeld && Time.time - lastHandReleaseTime <= socketGraceTime);
}
