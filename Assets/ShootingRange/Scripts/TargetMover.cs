using UnityEngine;

/// <summary>Движение мишени туда-обратно вдоль вектора travel.</summary>
public class TargetMover : MonoBehaviour
{
    public enum Pattern { Smooth, Linear }

    [Tooltip("Максимальное смещение от стартовой точки в обе стороны, м")]
    public Vector3 travel = new Vector3(3f, 0f, 0f);
    [Tooltip("Скорость (рад/с для Smooth, проходов/с для Linear)")]
    public float speed = 1f;
    public float phase;
    public Pattern pattern = Pattern.Smooth;

    Vector3 origin;

    void Start()
    {
        origin = transform.position;
    }

    void Update()
    {
        float t = Time.time * speed + phase;
        float s = pattern == Pattern.Smooth ? Mathf.Sin(t) : Mathf.PingPong(t, 2f) - 1f;
        transform.position = origin + travel * s;
    }
}
