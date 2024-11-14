using UnityEngine;
using UnityEngine.XR.MagicLeap;
using InputDevice = UnityEngine.XR.InputDevice;

public class HandTrackingBehavior : MonoBehaviour
{
    private InputDevice leftHandDevice;
    private InputDevice rightHandDevice;
    void Start()
    {
        if (MLPermissions.CheckPermission(MLPermission.HandTracking).IsOk)
        {
            Debug.Log("MLPermission for hand tracking was enabled");
            InputSubsystem.Extensions.MLHandTracking.StartTracking();
        }
        else
            Debug.Log("MLPermission for hand tracking is missing");
    }


    void Update()
    {
        if (!leftHandDevice.isValid)
            leftHandDevice = InputSubsystem.Utils.FindMagicLeapDevice(UnityEngine.XR.InputDeviceCharacteristics.HandTracking | UnityEngine.XR.InputDeviceCharacteristics.Left);
        if (!rightHandDevice.isValid)
            rightHandDevice = InputSubsystem.Utils.FindMagicLeapDevice(UnityEngine.XR.InputDeviceCharacteristics.HandTracking | UnityEngine.XR.InputDeviceCharacteristics.Right);
    }
}
