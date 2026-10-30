using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Общие настройки тира при старте:
/// - тело игрока (Character Controller) не сталкивается с автоматом, магазинами
///   и прицелами, чтобы оружие в руках не "толкало" игрока;
/// - добавляет помощника захвата для ПК (SimulatorGrabAssist);
/// - пишет в консоль, что нашлось в сцене (для поиска проблем).
/// </summary>
public class RangeBootstrap : MonoBehaviour
{
    void Awake()
    {
        int grabLayer = LayerMask.NameToLayer(RangePrimitives.GrabbableLayerName);
        int playerLayer = LayerMask.NameToLayer(RangePrimitives.PlayerLayerName);
        if (grabLayer >= 0 && playerLayer >= 0)
            Physics.IgnoreLayerCollision(grabLayer, playerLayer, true);

        if (GetComponent<SimulatorGrabAssist>() == null)
            gameObject.AddComponent<SimulatorGrabAssist>();
    }

    void Start()
    {
        var managers = FindObjectsOfType<XRInteractionManager>();
        var controllers = FindObjectsOfType<XRBaseController>();
        var interactors = FindObjectsOfType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInputInteractor>();
        var grabbables = FindObjectsOfType<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

        var names = new System.Text.StringBuilder();
        foreach (var i in interactors)
            names.Append(i.name).Append(i.isActiveAndEnabled ? "" : "(выкл)").Append(", ");

        Debug.Log("[Тир VR] В сцене: XR Interaction Manager — " + managers.Length +
                  ", контроллеров — " + controllers.Length +
                  ", рук-интеракторов — " + interactors.Length + " [" + names + "]" +
                  ", предметов для захвата — " + grabbables.Length);

        if (managers.Length > 1)
            Debug.LogWarning("[Тир VR] XR Interaction Manager больше одного — предметы и руки могут попасть в разные менеджеры и не видеть друг друга. Оставь один.");
        if (interactors.Length == 0)
            Debug.LogWarning("[Тир VR] В сцене нет рук-интеракторов (Direct/Ray Interactor) — игрок собран не из Starter Assets. На ПК G всё равно прикрепит предмет к руке/камере.");
    }
}
