// Copied from https://developer-docs.magicleap.cloud/docs/guides/unity/camera/ml-camera-example/#photo-capture

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.MagicLeap;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

/// <summary>
/// This script provides an example of capturing images using the Magic Leap 2's Main Camera stream and Magic Leap 2 Camera APIs
/// It handles permissions, connects to the camera, captures images at regular intervals, and sends the result data to the Camera Capture visualizer.
/// </summary>
public class ImageCapture : MonoBehaviour
{
    [SerializeField, Tooltip("The renderer to show the camera capture on JPEG format")]
    private Renderer _screenRendererJPEG = null;

    // Indicates if the camera is connected
    private bool isCameraConnected;
    // Reference to the MLCamera object that will access the device's camera
    private MLCamera colorCamera;
    // Indicates if the camera device is available
    private bool cameraDeviceAvailable;

    // Indicates if an image is currently being captured
    private bool isCapturingImage;

    // Reference to the MLPermissions.Callbacks object that will handle the permission requests and responses
    private readonly MLPermissions.Callbacks permissionCallbacks = new MLPermissions.Callbacks();

    // Used to display the JPEG image.
    private Texture2D imageTexture;

    // Register the permission callbacks in the Awake method
    private void Awake()
    {
        permissionCallbacks.OnPermissionGranted += OnPermissionGranted;
        permissionCallbacks.OnPermissionDenied += OnPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain += OnPermissionDenied;
    }

    // Request the camera permission in the Start method
    void Start()
    {
        MLResult result = MLPermissions.RequestPermission(MLPermission.Camera, permissionCallbacks);
        if (!result.IsOk)
        {
            Debug.LogErrorFormat("Error: ImageCaptureExample failed to get requested permissions, disabling script. Reason: {0}", result);
            enabled = false;
        }

    }

    /// <summary>
    /// Stop the camera, unregister callbacks, and stop input and permissions APIs.
    /// </summary>
    void OnDisable()
    {
        permissionCallbacks.OnPermissionGranted -= OnPermissionGranted;
        permissionCallbacks.OnPermissionDenied -= OnPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain -= OnPermissionDenied;

        if (colorCamera != null && isCameraConnected)
        {
            DisableMLCamera();
        }
    }

    // Handle the permission denied event by logging an error message
    private void OnPermissionDenied(string permission)
    {
        MLPluginLog.Error($"{permission} denied, test won't function.");
    }

    // Handle the permission granted event by starting two coroutines:
    // one to enable the camera and one to capture images in a loop
    private void OnPermissionGranted(string permission)
    {
        StartCoroutine(EnableMLCamera());
        StartCoroutine(CaptureImagesLoop());
    }

    // Define a coroutine that will enable the camera by checking its availability,
    // creating and connecting it, and preparing it for capture
    private IEnumerator EnableMLCamera()
    {
        // Loop until the camera device is available
        while (!cameraDeviceAvailable)
        {
            MLResult result = MLCamera.GetDeviceAvailabilityStatus(MLCamera.Identifier.Main, out cameraDeviceAvailable);
            if (!(result.IsOk && cameraDeviceAvailable))
            {
                // Wait until camera device is available
                yield return new WaitForSeconds(1.0f);
            }
        }

        Debug.Log("Camera device available.");

        // Create and connect the camera with a context that enables video stabilization and camera only capture
        ConnectCamera();

        // Wait until the camera is connected since this script uses the async "CreateAndConnectAsync" Method to connect to the camera.
        while (!isCameraConnected)
        {
            yield return null;
        }

        Debug.Log("Camera device connected.");

        // Prepare the camera for capture with a configuration that specifies JPEG output format, frame rate, and resolution
        ConfigureAndPrepareCapture();
    }

    // Define a coroutine that will capture images every 3 seconds if the camera is connected and supports image capture type. 
    // The image is then captured async
    private IEnumerator CaptureImagesLoop()
    {
        while (true)
        {
            if (isCameraConnected && !isCapturingImage)
            {
                if (MLCamera.IsCaptureTypeSupported(colorCamera, MLCamera.CaptureType.Image))
                {
                    CaptureImage();
                }
            }
            yield return new WaitForSeconds(3.0f);
        }
    }

    // Define an async method that will create and connect the camera with a context that enables video stabilization and Video only capture
    private async void ConnectCamera()
    {
        MLCamera.ConnectContext context = MLCamera.ConnectContext.Create();
        context.EnableVideoStabilization = false;
        context.Flags = MLCameraBase.ConnectFlag.CamOnly;

        // Use the CreateAndConnectAsync method to create and connect the camera asynchronously
        colorCamera = await MLCamera.CreateAndConnectAsync(context);

        if (colorCamera != null)
        {
            // Register a callback for when a raw image is available after capture
            colorCamera.OnRawImageAvailable += OnCaptureRawImageComplete;
            isCameraConnected = true;
        }
    }


    // Define an async method that will prepare the camera for capture with a configuration that specifies
    // JPEG output format, frame rate, and resolution
    // ERRORS HAPPENING HERE
    private async void ConfigureAndPrepareCapture()
    {
        MLCamera.CaptureStreamConfig[] imageConfig = new MLCamera.CaptureStreamConfig[1]
        {
            new MLCamera.CaptureStreamConfig()
            {
                OutputFormat = MLCamera.OutputFormat.JPEG,
                CaptureType = MLCamera.CaptureType.Image,
                Width = 1280,
                Height = 720
            }
        };

        MLCamera.CaptureConfig captureConfig = new MLCamera.CaptureConfig()
        {
            StreamConfigs = imageConfig,
            //CaptureFrameRate = MLCamera.CaptureFrameRate.None
        };

        // Use the PrepareCapture method to set the capture configuration and get the metadata handle
        MLResult prepareCaptureResult = colorCamera.PrepareCapture(captureConfig, out MLCamera.Metadata _);

        if (!prepareCaptureResult.IsOk)
        {
            Debug.Log("prepareCapture FAILED");
            return;
        }
        Debug.Log("prepareCapture SUCCESS");
    }

    /// <summary>
    /// Disconnects the MLCamera if it was ever created or connected.
    /// </summary>
    private void DisableMLCamera()
    {
        if (colorCamera != null)
        {
            colorCamera.Disconnect();

            // Explicitly set to false here as the disconnect was attempted.
            isCameraConnected = false;
        }
    }

    /// <summary>
    /// Takes a picture async with the device's camera using the camera's CaptureImageAsync method.
    /// </summary>
    private async void CaptureImage()
    {
        // Set the flag to indicate that an image capture is in progress
        isCapturingImage = true;

        var aeawbResult = await colorCamera.PreCaptureAEAWBAsync();
        if (!aeawbResult.IsOk)
        {
            Debug.LogError("Image capture failed!");
        }
        else
        {
            var result = await colorCamera.CaptureImageAsync(1);
            if (!result.IsOk)
            {
                Debug.LogError("Image capture failed!");
            }
        }

        // Reset the flag to indicate that image capture is complete
        isCapturingImage = false;
    }

    /// <summary>
    /// Handles the event of a new image getting captured and visualizes it with the Image Visualizer
    /// </summary>
    /// <param name="capturedImage">Captured frame.</param>
    /// <param name="resultExtras">Results Extras.</param>
    private void OnCaptureRawImageComplete(MLCamera.CameraOutput capturedImage, MLCamera.ResultExtras resultExtras, MLCamera.Metadata metadataHandle)
    {
        MLResult aeStateResult = metadataHandle.GetControlAEStateResultMetadata(out MLCamera.Metadata.ControlAEState controlAEState);
        MLResult awbStateResult = metadataHandle.GetControlAWBStateResultMetadata(out MLCamera.Metadata.ControlAWBState controlAWBState);

        if (aeStateResult.IsOk && awbStateResult.IsOk)
        {
            bool autoExposureComplete = controlAEState == MLCameraBase.Metadata.ControlAEState.Converged || controlAEState == MLCameraBase.Metadata.ControlAEState.Locked;
            bool autoWhiteBalanceComplete = controlAWBState == MLCameraBase.Metadata.ControlAWBState.Converged || controlAWBState == MLCameraBase.Metadata.ControlAWBState.Locked;

            if (autoExposureComplete && autoWhiteBalanceComplete)
            {
                // This example is configured to render JPEG images only.
                if (capturedImage.Format == MLCameraBase.OutputFormat.JPEG)
                {
                    UpdateJPGTexture(capturedImage.Planes[0], _screenRendererJPEG);
                }
            }
        }

    }

    private void UpdateJPGTexture(MLCamera.PlaneInfo imagePlane, Renderer renderer)
    {
        Debug.Log("Attempting to update JPG Texture");
        if (imageTexture != null)
        {
            Destroy(imageTexture);
        }
        imageTexture = new Texture2D(8, 8);
        bool status = imageTexture.LoadImage(imagePlane.Data);

        if (status && (imageTexture.width != 8 && imageTexture.height != 8))
        {
            renderer.material.mainTexture = imageTexture;
            Debug.Log("Updated JPG Texture");
        }
    }
}