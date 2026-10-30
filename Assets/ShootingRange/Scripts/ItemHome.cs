using UnityEngine;


/// <summary>
/// Возвращает предмет на стол, если его уронили за пределы тира
/// или он провалился под пол.
/// </summary>
public class ItemHome : MonoBehaviour
{
    public float fallLimitY = -2f;
    public float maxDistance = 60f;

    Vector3 homePosition;
    Quaternion homeRotation;
    Rigidbody body;
    UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable interactable;

    void Start()
    {
        homePosition = transform.position;
        homeRotation = transform.rotation;
        body = GetComponent<Rigidbody>();
        interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
    }

    void Update()
    {
        if (interactable != null && interactable.isSelected) return;
        if (transform.position.y < fallLimitY || (transform.position - homePosition).sqrMagnitude > maxDistance * maxDistance)
            ReturnHome();
    }

    public void ReturnHome()
    {
        if (body != null)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = homePosition;
            body.rotation = homeRotation;
        }
        transform.SetPositionAndRotation(homePosition, homeRotation);
    }
}
