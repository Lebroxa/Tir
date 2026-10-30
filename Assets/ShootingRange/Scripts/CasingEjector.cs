using UnityEngine;

/// <summary>
/// Выброс гильз. Гильзы НЕ используют физику (нет Rigidbody и коллайдеров),
/// не создаются через Instantiate и не накапливаются: небольшой пул объектов,
/// гильза видна долю секунды и сразу пропадает.
/// lifetime = 0 — гильзы не появляются вообще.
/// </summary>
public class CasingEjector : MonoBehaviour
{
    public Transform ejectionPort;
    public Material casingMaterial;

    [Tooltip("Сколько секунд гильза видна до удаления. 0 = гильзы отключены")]
    public float lifetime = 0.12f;
    public float ejectSpeed = 2.5f;
    public int poolSize = 4;

    class Casing
    {
        public Transform transform;
        public Vector3 velocity;
        public float hideAt;
        public bool active;
    }

    Casing[] pool;
    Transform poolRoot;
    int next;
    int activeCount;

    void InitPool()
    {
        poolRoot = new GameObject("CasingPool").transform;
        pool = new Casing[Mathf.Max(1, poolSize)];
        for (int i = 0; i < pool.Length; i++)
        {
            var go = RangePrimitives.Create(PrimitiveType.Cylinder, "Casing", poolRoot, Vector3.zero, Vector3.zero,
                new Vector3(0.011f, 0.0195f, 0.011f), casingMaterial);
            go.SetActive(false);
            pool[i] = new Casing { transform = go.transform };
        }
    }

    public void Eject()
    {
        if (lifetime <= 0f || ejectionPort == null) return;
        if (pool == null) InitPool();

        var c = pool[next];
        next = (next + 1) % pool.Length;
        if (!c.active) activeCount++;

        c.active = true;
        c.hideAt = Time.time + lifetime;
        c.velocity = ejectionPort.right * ejectSpeed + ejectionPort.up * 1.2f + Random.insideUnitSphere * 0.3f;
        c.transform.SetPositionAndRotation(ejectionPort.position, ejectionPort.rotation * Quaternion.Euler(90f, 0f, 0f));
        c.transform.gameObject.SetActive(true);
    }

    void Update()
    {
        if (activeCount == 0) return;

        float dt = Time.deltaTime;
        for (int i = 0; i < pool.Length; i++)
        {
            var c = pool[i];
            if (!c.active) continue;

            if (Time.time >= c.hideAt)
            {
                c.active = false;
                activeCount--;
                c.transform.gameObject.SetActive(false);
                continue;
            }

            c.velocity += Physics.gravity * dt;
            c.transform.position += c.velocity * dt;
            c.transform.Rotate(720f * dt, 0f, 360f * dt, Space.Self);
        }
    }

    void OnDestroy()
    {
        if (poolRoot != null) Destroy(poolRoot.gameObject);
    }
}
