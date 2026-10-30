using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Сам выбирает режим при запуске:
/// VR-шлем подключён — работаем со шлемом (симулятор убирается, чтобы не мешал);
/// шлема нет — создаётся XR Device Simulator и игрок управляется клавиатурой и мышью.
/// Работает и в редакторе, и в собранной игре.
/// </summary>
public class XRModeSelector : MonoBehaviour
{
    [Tooltip("Префаб XR Device Simulator из Samples XR Interaction Toolkit")]
    public GameObject simulatorPrefab;
    [Tooltip("Сколько секунд ждать, пока OpenXR найдёт шлем")]
    public float headsetWaitTime = 1.5f;
    [Tooltip("Всегда использовать симулятор, даже если шлем подключён")]
    public bool forceSimulator;

    public static bool UsingSimulator { get; private set; }

    static readonly List<InputDevice> devices = new List<InputDevice>();

    IEnumerator Start()
    {
        bool headset = false;
        float until = Time.realtimeSinceStartup + headsetWaitTime;
        while (!forceSimulator && Time.realtimeSinceStartup < until)
        {
            if (HeadsetConnected())
            {
                headset = true;
                break;
            }
            yield return null;
        }

        UsingSimulator = !headset;
        if (headset)
        {
            RemoveSimulators();
            Debug.Log("Тир VR: найден VR-шлем — управление со шлема и контроллеров.");
        }
        else
        {
            if (FindSimulator() == null)
            {
                if (simulatorPrefab != null) Instantiate(simulatorPrefab);
                else Debug.LogWarning("Тир VR: шлем не найден, а префаб XR Device Simulator не назначен (импортируй sample XR Device Simulator).");
            }
            Debug.Log("Тир VR: шлем не найден — включён XR Device Simulator (клавиатура + мышь).");
        }
    }

    static bool HeadsetConnected()
    {
        if (!XRSettings.isDeviceActive) return false;
        devices.Clear();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, devices);
        return devices.Count > 0;
    }

    static GameObject FindSimulator()
    {
        foreach (var mb in FindObjectsOfType<MonoBehaviour>())
            if (mb != null && mb.GetType().Name == "XRDeviceSimulator")
                return mb.gameObject;
        return null;
    }

    static void RemoveSimulators()
    {
        foreach (var mb in FindObjectsOfType<MonoBehaviour>())
            if (mb != null && mb.GetType().Name == "XRDeviceSimulator")
                Destroy(mb.gameObject);
    }
}
