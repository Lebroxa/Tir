using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Захват и стрельба на ПК (XR Device Simulator). Один на сцену: его добавляет
/// RangeBootstrap. Если такой компонент висит и на руках, работает только один.
///
/// G — рука, которой сейчас управляешь в симуляторе (Tab), берёт ближайший предмет
///     в радиусе grabRadius. Повторное G — отпустить.
/// T или ЛКМ — спуск, если в руке автомат.
///
/// Сначала пробует штатный ручной захват XRI (StartManualInteraction).
/// Если XRI его сбросил или рук-интеракторов в сцене нет, предмет просто
/// прикрепляется к руке (или к камере, если рук нет вообще).
/// Со шлемом не работает (там обычный хват), если не выключить onlyInSimulator.
/// </summary>
public class SimulatorGrabAssist : MonoBehaviour
{
    public float grabRadius = 0.8f;
    public bool onlyInSimulator = true;
    [Tooltip("Писать в консоль, что происходит при нажатии G")]
    public bool logToConsole = true;

    class Hand
    {
        public Transform transform;
        public UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor interactor;
        public bool isLeft, isRight;
    }

    class Hold
    {
        public UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable item;
        public UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor interactor; // штатный ручной захват XRI
        public Transform follow;            // запасной режим: просто следуем за рукой
        public Vector3 localPosition;
        public Quaternion localRotation;
        public bool wasKinematic;
        public int verifyFrame = -1;
    }

    static int processedFrame = -1;
    static float simulatorCheckTime;
    static bool simulatorPresent;
    static MonoBehaviour simulator;
    static readonly Dictionary<Transform, Hold> holds = new Dictionary<Transform, Hold>();
    static bool triggerWasDown;
    static bool reportedStart;

    // Страховка: если в сцене тира помощника нет (например нет RangeManager) — создаём сами
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindObjectOfType<SimulatorGrabAssist>() != null) return;
        if (FindObjectOfType<AKRifle>() == null) return; // только в сцене тира
        new GameObject("PC Grab Assist").AddComponent<SimulatorGrabAssist>();
    }

    void LateUpdate()
    {
        // Работает один экземпляр за кадр, даже если компонент висит на нескольких объектах
        if (processedFrame == Time.frameCount) return;
        processedFrame = Time.frameCount;

        bool sim = SimulatorActive();
        if (onlyInSimulator && !sim) return;

        if (!reportedStart)
        {
            reportedStart = true;
            Log("помощник захвата работает. G — взять/отпустить, T или ЛКМ — огонь.");
        }

        VerifyHolds();
        FollowHolds();
        HandleTrigger();

        if (KeyPressed(KeyKind.Grab)) OnGrabKey();
    }

    // ------------------------------------------------------------ ввод

    enum KeyKind { Grab, Trigger }

    static bool KeyPressed(KeyKind kind)
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        return kind == KeyKind.Grab && k != null && k.gKey.wasPressedThisFrame;
#else
        return kind == KeyKind.Grab && Input.GetKeyDown(KeyCode.G);
#endif
    }

    static bool TriggerHeld()
    {
#if ENABLE_INPUT_SYSTEM
        bool key = Keyboard.current != null && Keyboard.current.tKey.isPressed;
        bool mouse = Mouse.current != null && Mouse.current.leftButton.isPressed;
        return key || mouse;
#else
        return Input.GetKey(KeyCode.T) || Input.GetMouseButton(0);
#endif
    }

    static bool SimulatorActive()
    {
        if (Time.unscaledTime >= simulatorCheckTime)
        {
            simulatorCheckTime = Time.unscaledTime + 1f;
            simulator = null;
            foreach (var mb in FindObjectsOfType<MonoBehaviour>())
            {
                if (mb != null && mb.GetType().Name == "XRDeviceSimulator")
                {
                    simulator = mb;
                    break;
                }
            }
            simulatorPresent = simulator != null;
        }
        return XRModeSelector.UsingSimulator || simulatorPresent;
    }

    /// <summary>Какой рукой сейчас управляют в симуляторе (Tab). null — неизвестно.</summary>
    static bool? ActiveHandIsLeft()
    {
        if (simulator == null) return null;
        bool? left = ReadBool(simulator, "manipulatingLeftController");
        bool? right = ReadBool(simulator, "manipulatingRightController");
        if (left == true && right != true) return true;
        if (right == true && left != true) return false;
        return null;
    }

    static bool? ReadBool(object target, string propertyName)
    {
        var p = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (p == null || p.PropertyType != typeof(bool)) return null;
        return (bool)p.GetValue(target);
    }

    // ------------------------------------------------------------ захват

    void OnGrabKey()
    {
        bool? activeLeft = ActiveHandIsLeft();
        var hands = CollectHands();

        // Если управляем конкретной рукой — работаем только с ней
        if (activeLeft.HasValue)
        {
            hands.RemoveAll(h => activeLeft.Value ? h.isRight && !h.isLeft : h.isLeft && !h.isRight);
        }

        // Рука уже что-то держит — отпускаем
        foreach (var h in hands)
        {
            if (holds.ContainsKey(h.transform))
            {
                Release(h.transform);
                return;
            }
        }
        if (!activeLeft.HasValue && holds.Count > 0)
        {
            foreach (var key in new List<Transform>(holds.Keys))
            {
                Release(key);
                return;
            }
        }

        // Обычный захват XRI уже сработал этой рукой — не мешаем
        foreach (var h in hands)
        {
            if (h.interactor != null && h.interactor.hasSelection && !holds.ContainsKey(h.transform))
            {
                Log("рука " + h.transform.name + " уже держит предмет штатным захватом XRI.");
                return;
            }
        }

        if (hands.Count == 0)
        {
            Log("не найдено ни одной руки/камеры — брать нечем.");
            return;
        }

        Hand bestHand = null;
        UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable bestItem = null;
        float bestDistance = float.MaxValue;
        foreach (var h in hands)
        {
            float radius = h.interactor == null && h.transform.GetComponent<Camera>() != null ? grabRadius * 2f : grabRadius;
            var item = FindNearest(h.transform.position, out float d);
            if (item == null) continue;
            if (h.interactor is UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor) d -= 0.02f; // при равенстве — прямой хват
            if (d < bestDistance && d <= radius)
            {
                bestDistance = d;
                bestItem = item;
                bestHand = h;
            }
        }

        if (bestItem == null)
        {
            var item = FindNearest(hands[0].transform.position, out float d);
            Log("в радиусе " + grabRadius + " м от руки нечего взять. " +
                (item != null ? "Ближайший предмет: " + item.name + " — " + d.ToString("0.00") + " м. Опусти руку ближе к столу." : "Предметов в сцене нет."));
            return;
        }

        Grab(bestHand, bestItem, Mathf.Max(0f, bestDistance));
    }

    void Grab(Hand hand, UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable item, float distance)
    {
        var hold = new Hold { item = item };
        holds[hand.transform] = hold;

        if (hand.interactor != null && hand.interactor.interactionManager != null)
        {
            hand.interactor.StartManualInteraction((UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)item);
            hold.interactor = hand.interactor;
            hold.verifyFrame = Time.frameCount + 2;
            Log(hand.transform.name + ": взял " + item.name + " (" + distance.ToString("0.00") + " м). G — отпустить.");
        }
        else
        {
            StartFollow(hold, hand.transform);
            Log(hand.transform.name + ": прикрепил " + item.name + " к руке (" + distance.ToString("0.00") + " м). G — отпустить.");
        }
    }

    static void StartFollow(Hold hold, Transform hand)
    {
        var item = hold.item;

        // Достаём предмет из слота (например магазин из автомата)
        if (item.interactionManager != null)
        {
            foreach (var interactor in new List<UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor>(item.interactorsSelecting))
                item.interactionManager.SelectExit(interactor, (UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)item);
        }

        var rb = item.GetComponent<Rigidbody>();
        if (rb != null)
        {
            hold.wasKinematic = rb.isKinematic;
            rb.isKinematic = true;
        }

        var attach = item.attachTransform != null ? item.attachTransform : item.transform;
        hold.localRotation = Quaternion.Inverse(item.transform.rotation) * attach.rotation;
        hold.localPosition = item.transform.InverseTransformPoint(attach.position);
        hold.follow = hand;

        var socketable = item.GetComponent<SocketableItem>();
        if (socketable != null) socketable.SetExternallyHeld(true);
    }

    void Release(Transform handKey)
    {
        if (!holds.TryGetValue(handKey, out var hold)) return;
        holds.Remove(handKey);
        if (hold.item == null) return;

        if (hold.interactor != null)
        {
            hold.interactor.EndManualInteraction();
        }
        else if (hold.follow != null)
        {
            var rb = hold.item.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = hold.wasKinematic;
                if (!rb.isKinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
            var socketable = hold.item.GetComponent<SocketableItem>();
            if (socketable != null) socketable.SetExternallyHeld(false);
        }

        var rifle = hold.item.GetComponent<AKRifle>();
        if (rifle != null) rifle.SetTrigger(false);
        Log(handKey.name + ": отпустил " + hold.item.name + ".");
    }

    // Через пару кадров проверяем, что XRI не сбросил захват; если сбросил — прикрепляем сами
    void VerifyHolds()
    {
        foreach (var pair in holds)
        {
            var hold = pair.Value;
            if (hold.verifyFrame < 0 || Time.frameCount < hold.verifyFrame) continue;
            hold.verifyFrame = -1;
            if (hold.interactor == null || hold.item == null) continue;
            if (hold.interactor.IsSelecting((UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)hold.item)) continue;

            Debug.LogWarning("[Тир VR] XRI сбросил захват " + hold.item.name + " рукой " + hold.interactor.name +
                             " — включаю простое прикрепление к руке.");
            hold.interactor.EndManualInteraction();
            var hand = hold.interactor.attachTransform != null ? hold.interactor.attachTransform : hold.interactor.transform;
            hold.interactor = null;
            StartFollow(hold, hand);
        }
    }

    static void FollowHolds()
    {
        foreach (var pair in holds)
        {
            var hold = pair.Value;
            if (hold.follow == null || hold.item == null) continue;

            var rotation = hold.follow.rotation * Quaternion.Inverse(hold.localRotation);
            var position = hold.follow.position - rotation * hold.localPosition;
            hold.item.transform.SetPositionAndRotation(position, rotation);
        }
    }

    static void HandleTrigger()
    {
        bool down = TriggerHeld();
        if (down == triggerWasDown) return;
        triggerWasDown = down;

        foreach (var pair in holds)
        {
            if (pair.Value.item == null) continue;
            var rifle = pair.Value.item.GetComponent<AKRifle>();
            if (rifle != null) rifle.SetTrigger(down);
        }
    }

    // ------------------------------------------------------------ поиск

    static List<Hand> CollectHands()
    {
        var hands = new List<Hand>();
        foreach (var i in FindObjectsOfType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInputInteractor>())
        {
            if (!i.isActiveAndEnabled) continue;
            string n = i.name.ToLower();
            if (n.Contains("teleport") || n.Contains("poke")) continue;
            hands.Add(MakeHand(i.attachTransform != null ? i.attachTransform : i.transform, i));
        }
        if (hands.Count == 0)
        {
            foreach (var c in FindObjectsOfType<XRBaseController>())
                if (c.isActiveAndEnabled) hands.Add(MakeHand(c.transform, null));
        }
        if (hands.Count == 0 && Camera.main != null)
            hands.Add(MakeHand(Camera.main.transform, null));
        return hands;
    }

    static Hand MakeHand(Transform t, UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor interactor)
    {
        var hand = new Hand { transform = t, interactor = interactor };
        for (var p = t; p != null; p = p.parent)
        {
            string n = p.name.ToLower();
            if (n.Contains("left")) { hand.isLeft = true; break; }
            if (n.Contains("right")) { hand.isRight = true; break; }
        }
        return hand;
    }

    static UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable FindNearest(Vector3 position, out float bestDistance)
    {
        UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable best = null;
        bestDistance = float.MaxValue;

        foreach (var grab in FindObjectsOfType<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>())
        {
            if (!grab.isActiveAndEnabled || IsHeld(grab)) continue;

            var colliders = grab.colliders;
            for (int c = 0; c < colliders.Count; c++)
            {
                var col = colliders[c];
                if (col == null || !col.enabled || col.isTrigger) continue;
                float d = Vector3.Distance(col.ClosestPoint(position), position);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = grab;
                }
            }
        }
        return best;
    }

    static bool IsHeld(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab)
    {
        foreach (var hold in holds.Values)
            if (hold.item == grab) return true;
        foreach (var i in grab.interactorsSelecting)
            if (!(i is UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor)) return true;
        return false;
    }

    void Log(string message)
    {
        if (logToConsole) Debug.Log("[Тир VR] " + message);
    }
}
