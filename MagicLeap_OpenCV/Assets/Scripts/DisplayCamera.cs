using OpenCVForUnity.CoreModule;
using OpenCVForUnity.UnityUtils;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.MagicLeap;
using static UnityEngine.XR.MagicLeap.MLCameraBase;

public class DisplayCamera : MonoBehaviour
{
    [SerializeField, Tooltip("Desired width for the camera capture")]
    private int captureWidth = 1280;
    [SerializeField, Tooltip("Desired height for the camera capture")]
    private int captureHeight = 720;
    [SerializeField, Tooltip("The UI to show the camera capture in RGBA format")]
    private RawImage _screenRendererRGBA = null;

    //RGBA Image Texture
    private Texture2D _rawVideoTextureRGBA;

    //The identifier can either target the Main or CV cameras.
    private MLCamera.Identifier _identifier = MLCamera.Identifier.Main;
    private MLCamera _camera;
    //Is true if the camera is ready to be connected.
    private bool _cameraDeviceAvailable;

    private MLCamera.CaptureConfig _captureConfig;

    private Texture2D _videoTextureRgb;
    //The camera capture state
    bool _isCapturing;

    // Frame Material
    private Mat frameMaterial;

    /// <summary>
    /// The base mat.
    /// </summary>
    protected Mat baseMat;

    /// <summary>
    /// The buffer colors.
    /// </summary>
    protected Color32[] colors;


    private void OnEnable()
    {
        //This script assumes that camera permissions were already granted.
        StartCoroutine(EnableMLCamera());
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private IEnumerator EnableMLCamera()
    {
        //Checks the main camera's availability.
        while (!_cameraDeviceAvailable)
        {
            MLResult result = MLCamera.GetDeviceAvailabilityStatus(_identifier, out _cameraDeviceAvailable);
            if (result.IsOk == false || _cameraDeviceAvailable == false)
            {
                // Wait until camera device is available
                yield return new WaitForSeconds(1.0f);
            }
        }
        ConnectCamera();
    }

    private void ConnectCamera()
    {
        //Once the camera is available, we can connect to it.
        if (_cameraDeviceAvailable)
        {
            MLCamera.ConnectContext connectContext = MLCamera.ConnectContext.Create();
            connectContext.CamId = _identifier;
            //MLCamera.Identifier.Main is the only camera that can access the virtual and mixed reality flags
            connectContext.Flags = MLCamera.ConnectFlag.CamOnly;
            connectContext.EnableVideoStabilization = true;

            _camera = MLCamera.CreateAndConnect(connectContext);
            if (_camera != null)
            {
                Debug.Log("Camera device connected");
                ConfigureCameraInput();
                SetCameraCallbacks();
            }
        }
    }

    private void StartVideoCapture()
    {
        if (colors == null || colors.Length != captureWidth * captureHeight)
            colors = new Color32[captureWidth * captureHeight];
        // baseMat = new Mat(captureHeight, captureWidth, CvType.CV_8UC4);

        // frameMaterial = new Mat(baseMat.rows(), baseMat.cols(), CvType.CV_8UC(Channels(outputColorFormat)), new Scalar(0, 0, 0, 255));

        if (frameMaterial == null)
        {
            frameMaterial = new Mat(captureHeight, captureWidth, CvType.CV_8UC4, new Scalar(0, 0, 0, 255));
        }

        MLResult result = _camera.PrepareCapture(_captureConfig, out MLCamera.Metadata metaData);
        if (result.IsOk)
        {
            //Trigger auto exposure and auto white balance
            _camera.PreCaptureAEAWB();
            //Starts video capture. This call can also be called asynchronously 
            //Images capture uses the CaptureImage function instead.
            result = _camera.CaptureVideoStart();
            _isCapturing = MLResult.DidNativeCallSucceed(result.Result, nameof(_camera.CaptureVideoStart));
            if (_isCapturing)
            {
                Debug.Log("Video capture started!");
            }
            else
            {
                Debug.LogError($"Could not start camera capture. Result : {result}");
            }
        }
    }

    private void ConfigureCameraInput()
    {
        //Gets the stream capabilities the selected camera. (Supported capture types, formats and resolutions)
        MLCamera.StreamCapability[] streamCapabilities = MLCamera.GetImageStreamCapabilitiesForCamera(_camera, MLCamera.CaptureType.Video);

        if (streamCapabilities.Length == 0)
            return;

        //Set the default capability stream
        MLCamera.StreamCapability defaultCapability = streamCapabilities[0];

        //Try to get the stream that most closely matches the target width and height
        if (MLCamera.TryGetBestFitStreamCapabilityFromCollection(streamCapabilities, captureWidth, captureHeight,
                MLCamera.CaptureType.Video, out MLCamera.StreamCapability selectedCapability))
        {
            defaultCapability = selectedCapability;
        }

        //Initialize a new capture config.
        _captureConfig = new MLCamera.CaptureConfig();
        //Set RGBA video as the output
        MLCamera.OutputFormat outputFormat = MLCamera.OutputFormat.RGBA_8888;
        //Set the Frame Rate to 30fps
        _captureConfig.CaptureFrameRate = MLCamera.CaptureFrameRate._30FPS;
        //Initialize a camera stream config.
        //The Main Camera can support up to two stream configurations
        _captureConfig.StreamConfigs = new MLCamera.CaptureStreamConfig[1];
        _captureConfig.StreamConfigs[0] = MLCamera.CaptureStreamConfig.Create(
            defaultCapability, outputFormat
        );
        StartVideoCapture();
    }

    private void StopCapture()
    {
        if (_isCapturing)
        {
            _camera.CaptureVideoStop();
        }

        _camera.Disconnect();
        _camera.OnRawVideoFrameAvailable -= OnCaptureDataReceived;
        _isCapturing = false;
    }

    //Assumes that the capture configure was created with a Video CaptureType
    private void SetCameraCallbacks()
    {
        //Provides frames in either YUV/RGBA format depending on the stream configuration
        _camera.OnRawVideoFrameAvailable += OnCaptureDataReceived;
    }

    public void OnCaptureDataReceived(MLCamera.CameraOutput output, MLCamera.ResultExtras extras, MLCamera.Metadata metadataHandle)
    {
        if (output.Format == MLCamera.OutputFormat.RGBA_8888)
        {
            //Flips the frame vertically so it does not appear upside down.
            MLCamera.FlipFrameVertically(ref output);
            UpdateRGBTexture(output.Planes[0]);
        }
    }

    private void UpdateRGBTexture(MLCamera.PlaneInfo imagePlane)
    {
        int actualWidth = (int)(imagePlane.Width * imagePlane.PixelStride);

        if (_rawVideoTextureRGBA != null &&
            (_rawVideoTextureRGBA.width != imagePlane.Width || _rawVideoTextureRGBA.height != imagePlane.Height))
        {
            Destroy(_rawVideoTextureRGBA);
            _rawVideoTextureRGBA = null;
        }

        if (_rawVideoTextureRGBA == null)
        {
            // Create a new texture that will display the RGB image
            _rawVideoTextureRGBA = new Texture2D((int)imagePlane.Width, (int)imagePlane.Height, TextureFormat.RGBA32, false);
            _rawVideoTextureRGBA.filterMode = FilterMode.Bilinear;

            // Assign the RawImage Texture to the resulting texture
            _screenRendererRGBA.texture = _rawVideoTextureRGBA;
        }

        // Image width and stride may differ due to padding bytes for memory alignment. Skip over padding bytes when accessing pixel data.
        if (imagePlane.Stride != actualWidth)
        {
            // Create a new array to store the pixel data without padding
            var newTextureChannel = new byte[actualWidth * imagePlane.Height];
            // Loop through each row of the image
            for (int i = 0; i < imagePlane.Height; i++)
            {
                // Copy the pixel data from the original array to the new array, skipping the padding bytes
                Buffer.BlockCopy(imagePlane.Data, (int)(i * imagePlane.Stride), newTextureChannel, i * actualWidth, actualWidth);
            }
            // Load the new array as the texture data
            _rawVideoTextureRGBA.LoadRawTextureData(newTextureChannel);
        }
        else // If the stride is equal to the width, no padding bytes are present
        {
            _rawVideoTextureRGBA.LoadRawTextureData(imagePlane.Data);
        }



        _rawVideoTextureRGBA.Apply();
    }
}
