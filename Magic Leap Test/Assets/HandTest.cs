using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// This script shows how to create an Input Action at runtime.
/// </summary>
public class HandTest : MonoBehaviour
{
    //The input action that will log it's Vector3 value every frame.
    [SerializeField]
    private InputAction positionInputAction =
        new InputAction(binding: "<HandInteraction>{LeftHand}/pointerPosition", expectedControlType: "Vector3");

    //The input action that will log the grasp value.
    [SerializeField]
    private InputAction graspValueInputAction =
        new InputAction(binding: "<HandInteraction>{LeftHand}/graspValue", expectedControlType: "Axis");

    //The input action that will log the grasp value.
    [SerializeField]
    private InputAction pinchValueInputAction =
        new InputAction(binding: "<HandInteraction>{LeftHand}/pinchValue", expectedControlType: "Axis");


    private void Start()
    {
        positionInputAction.Enable();
        graspValueInputAction.Enable();
        pinchValueInputAction.Enable();
    }

    private void Update()
    {
        // Print pointer position to log.
        Debug.Log($"Left Hand Pointer Position: {positionInputAction.ReadValue<Vector3>()}");

        // Print the left hand grasp value to log.
        Debug.Log($"Left Hand Grasp Value: {graspValueInputAction.ReadValue<float>()}");

        // Print the left hand pinch value to log.
        Debug.Log($"Left Hand Pinch Value: {pinchValueInputAction.ReadValue<float>()}");
    }

    private void OnDestroy()
    {
        graspValueInputAction.Dispose();
        positionInputAction.Dispose();
    }
}