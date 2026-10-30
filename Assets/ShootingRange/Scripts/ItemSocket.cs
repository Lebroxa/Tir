

/// <summary>
/// Слот на автомате (XR Socket Interactor), который принимает только
/// определённый тип предметов и только из руки игрока.
/// </summary>
public abstract class ItemSocket : UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor
{
    protected abstract bool Accepts(SocketableItem item);

    bool IsCandidate(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable interactable)
    {
        if (interactable == null) return false;
        var item = interactable.transform.GetComponent<SocketableItem>();
        return item != null && Accepts(item) && item.CanBeSocketed;
    }

    public override bool CanHover(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRHoverInteractable interactable)
    {
        bool alreadyHolding = interactable is UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable selectable && IsSelecting(selectable);
        return base.CanHover(interactable) && (alreadyHolding || IsCandidate(interactable));
    }

    public override bool CanSelect(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable interactable)
    {
        return base.CanSelect(interactable) && (IsSelecting(interactable) || IsCandidate(interactable));
    }
}
