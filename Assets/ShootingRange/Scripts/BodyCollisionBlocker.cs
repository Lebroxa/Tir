using UnityEngine;

/// <summary>
/// Не даёт игроку пройти сквозь стены, стол и барьер.
/// Character Controller останавливает только движение стиком, а движение самой
/// головы (реальная ходьба в шлеме или WASD в XR Device Simulator) его обходит.
/// Поэтому каждый кадр проверяем капсулу "тела" под головой и, если она залезла
/// в коллайдер, сдвигаем весь XR Origin назад.
/// Вешается на объект с компонентом XR Origin.
/// </summary>
public class BodyCollisionBlocker : MonoBehaviour
{
    [Tooltip("Камера игрока (голова)")]
    public Transform head;
    [Tooltip("Радиус тела, м")]
    public float radius = 0.15f;
    [Tooltip("Высота от пола, ниже которой препятствия не мешают (ступеньки, пол)")]
    public float stepHeight = 0.3f;
    public LayerMask obstacleMask = ~0;

    CapsuleCollider probe;
    readonly Collider[] overlaps = new Collider[16];

    void Start()
    {
        if (head == null)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam != null) head = cam.transform;
        }

        int playerLayer = LayerMask.NameToLayer(RangePrimitives.PlayerLayerName);
        int grabLayer = LayerMask.NameToLayer(RangePrimitives.GrabbableLayerName);
        obstacleMask &= ~(1 << 2); // Ignore Raycast
        obstacleMask &= ~(1 << 5); // UI
        if (playerLayer >= 0) obstacleMask &= ~(1 << playerLayer);
        if (grabLayer >= 0) obstacleMask &= ~(1 << grabLayer);

        // Отдельный объект-зонд (не дочерний, чтобы не мешать XR Origin)
        var go = new GameObject("BodyCollisionProbe");
        if (playerLayer >= 0) go.layer = playerLayer;
        probe = go.AddComponent<CapsuleCollider>();
        probe.isTrigger = true;
        probe.direction = 1;
        probe.radius = radius;
    }

    void LateUpdate()
    {
        if (head == null || probe == null) return;

        Vector3 h = head.position;
        float bottom = transform.position.y + stepHeight;
        float top = Mathf.Max(h.y, bottom + radius * 2f + 0.01f);
        float height = top - bottom;

        probe.radius = radius;
        probe.height = height;
        probe.transform.SetPositionAndRotation(new Vector3(h.x, bottom + height * 0.5f, h.z), Quaternion.identity);

        Vector3 p1 = new Vector3(h.x, bottom + radius, h.z);
        Vector3 p2 = new Vector3(h.x, top - radius, h.z);
        int count = Physics.OverlapCapsuleNonAlloc(p1, p2, radius, overlaps, obstacleMask, QueryTriggerInteraction.Ignore);

        Vector3 push = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            var col = overlaps[i];
            if (col == null || col == probe || col.isTrigger) continue;
            if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue; // мелкие физические предметы не держат игрока
            if (col.transform.IsChildOf(transform)) continue;

            if (Physics.ComputePenetration(probe, probe.transform.position, probe.transform.rotation,
                    col, col.transform.position, col.transform.rotation, out Vector3 direction, out float distance))
            {
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f) continue;
                push += direction.normalized * distance;
            }
        }

        if (push.sqrMagnitude > 0f)
        {
            transform.position += push;
            Physics.SyncTransforms();
        }
    }

    void OnDestroy()
    {
        if (probe != null) Destroy(probe.gameObject);
    }
}
