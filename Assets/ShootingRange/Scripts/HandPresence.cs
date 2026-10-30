using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Простая модель руки из примитивов (ладонь + пальцы по суставам),
/// заменяет модель контроллера. Пальцы сгибаются от кнопок:
/// Grip — средний, безымянный, мизинец и большой; Trigger — указательный.
/// Вешается на объект Left/Right Controller (XR Controller).
/// </summary>
public class HandPresence : MonoBehaviour
{
    public bool isLeftHand;
    public Material skinMaterial;
    public bool hideControllerModel = true;
    public Vector3 handOffset = new Vector3(0f, -0.01f, -0.04f);
    public float animationSpeed = 10f;
    [Range(0f, 1f)] public float restCurl = 0.12f;

    static readonly float[] JointCurl = { 55f, 85f, 60f };

    XRBaseController controller;
    readonly Transform[][] fingers = new Transform[4][];
    Transform[] thumb;
    float side;
    float grip, trigger;

    void Start()
    {
        controller = GetComponent<XRBaseController>();
        if (controller == null) controller = GetComponentInParent<XRBaseController>();
        if (controller != null && hideControllerModel) controller.hideControllerModel = true;
        BuildHand();
    }

    void BuildHand()
    {
        int layer = LayerMask.NameToLayer(RangePrimitives.PlayerLayerName);
        side = isLeftHand ? 1f : -1f; // сторона большого пальца в позе "ладонь вниз"

        var handRoot = new GameObject(isLeftHand ? "LeftHandModel" : "RightHandModel").transform;
        if (layer >= 0) handRoot.gameObject.layer = layer;
        handRoot.SetParent(transform, false);
        handRoot.localPosition = handOffset;
        // Поворачиваем ладонь внутрь, большой палец вверх
        handRoot.localRotation = Quaternion.Euler(0f, 0f, 90f * side);

        RangePrimitives.Create(PrimitiveType.Cube, "Palm", handRoot, Vector3.zero, Vector3.zero,
            new Vector3(0.08f, 0.026f, 0.09f), skinMaterial, false, layer);
        RangePrimitives.Create(PrimitiveType.Cube, "Wrist", handRoot, new Vector3(0f, 0f, -0.065f), Vector3.zero,
            new Vector3(0.058f, 0.034f, 0.05f), skinMaterial, false, layer);

        float[] xs = { 0.028f, 0.009f, -0.01f, -0.028f };
        float[] scale = { 1f, 1.08f, 1f, 0.82f };
        for (int f = 0; f < 4; f++)
        {
            fingers[f] = BuildChain(handRoot, new Vector3(xs[f] * side, 0f, 0.045f), Quaternion.identity,
                new[] { 0.042f * scale[f], 0.028f * scale[f], 0.022f * scale[f] }, 0.017f, layer);
        }

        thumb = BuildChain(handRoot, new Vector3(0.036f * side, -0.006f, -0.02f), Quaternion.Euler(0f, 50f * side, 0f),
            new[] { 0.036f, 0.03f }, 0.019f, layer);
    }

    Transform[] BuildChain(Transform parent, Vector3 start, Quaternion baseRotation, float[] lengths, float width, int layer)
    {
        var joints = new Transform[lengths.Length];
        var basePivot = new GameObject("Finger").transform;
        basePivot.SetParent(parent, false);
        basePivot.localPosition = start;
        basePivot.localRotation = baseRotation;

        Transform current = basePivot;
        for (int j = 0; j < lengths.Length; j++)
        {
            var joint = new GameObject("Joint" + j).transform;
            joint.SetParent(current, false);
            joint.localPosition = j == 0 ? Vector3.zero : new Vector3(0f, 0f, lengths[j - 1]);
            joints[j] = joint;

            RangePrimitives.Create(PrimitiveType.Cube, "Bone", joint, new Vector3(0f, 0f, lengths[j] * 0.5f), Vector3.zero,
                new Vector3(width, width, lengths[j] * 1.05f), skinMaterial, false, layer);
            current = joint;
        }
        return joints;
    }

    void Update()
    {
        if (thumb == null) return;

        float targetGrip = 0f, targetTrigger = 0f;
        if (controller != null)
        {
            var state = controller.currentControllerState;
            if (state != null)
            {
                targetGrip = state.selectInteractionState.value;
                targetTrigger = state.activateInteractionState.value;
            }
        }

        float step = Time.deltaTime * animationSpeed;
        grip = Mathf.MoveTowards(grip, targetGrip, step);
        trigger = Mathf.MoveTowards(trigger, targetTrigger, step);

        for (int f = 0; f < 4; f++)
        {
            float curl = f == 0 ? Mathf.Max(trigger, grip * 0.4f) : grip;
            curl = Mathf.Lerp(restCurl, 1f, curl);
            for (int j = 0; j < fingers[f].Length; j++)
                fingers[f][j].localRotation = Quaternion.Euler(curl * JointCurl[j], 0f, 0f);
        }

        float thumbCurl = Mathf.Lerp(restCurl, 1f, grip);
        thumb[0].localRotation = Quaternion.Euler(thumbCurl * 25f, -side * thumbCurl * 35f, 0f);
        thumb[1].localRotation = Quaternion.Euler(thumbCurl * 30f, 0f, 0f);
    }
}
