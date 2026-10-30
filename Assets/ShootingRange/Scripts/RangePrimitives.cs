using UnityEngine;

/// <summary>
/// Вспомогательные методы для создания простых объектов из примитивов
/// (используются и в рантайме, и в editor-скрипте сборки сцены).
/// </summary>
public static class RangePrimitives
{
    public const string GrabbableLayerName = "Grabbable";
    public const string PlayerLayerName = "Player";

    public static GameObject Create(PrimitiveType type, string name, Transform parent,
        Vector3 localPosition, Vector3 localEuler, Vector3 localScale,
        Material material, bool keepCollider = false, int layer = -1)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (layer >= 0) go.layer = layer;
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(localEuler);
        go.transform.localScale = localScale;

        if (material != null)
            go.GetComponent<Renderer>().sharedMaterial = material;

        if (!keepCollider)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }
        }
        return go;
    }

    public static void SetLayerRecursively(GameObject go, int layer)
    {
        if (layer < 0) return;
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
