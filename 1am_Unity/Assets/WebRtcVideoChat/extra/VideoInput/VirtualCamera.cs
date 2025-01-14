﻿#define ALLOW_UNSAFE
#if UNITY_EDITOR || (!UNITY_WSA)
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Byn.Unity.Examples
{
    /// <summary>
    /// Use at your own risk! The shown feature might not be stable
    /// and they are only tested on win64 so far.
    /// 
    /// Example for use of VideoInput class. 
    /// 
    /// This script will create a virtual video camera which then
    /// can be selected like a webcam via the CallApp.
    /// 
    /// It will obtain its image via a Unity Camera object and then store
    /// it in a native buffer. The image can be used later via the
    /// ICall or IMediaNetwork interface by setting
    /// MediaConfig.VideoDeviceName to the value of VirtualCamera._DeviceName.
    /// 
    /// Test the performance of this system first before using it. It might
    /// be too slow for many systems if used with high resolution images.
    /// 
    /// 
    /// </summary>
    public class VirtualCamera : MonoBehaviour
    {
        public Camera _Camera;
        private float mLastSample;

        private Texture2D mTexture;
        private RenderTexture mRtBuffer = null;

        /// <summary>
        /// Can be used to output the image sent for testing
        /// </summary>
        public RawImage _DebugTarget = null;

        /// <summary>
        /// Name used to access it later via MediaConfig
        /// </summary>
        public string _DeviceName = "VirtualCamera1";

        /// <summary>
        /// FPS the virtual device is suppose to have.
        /// (This isn't really used yet except to filter
        /// out this device if MediaConfig requests specific FPS)
        /// </summary>
        public int _Fps = 60;

        /// <summary>
        /// Width the output is suppose to have
        /// </summary>
        public int _Width = 640;
        /// <summary>
        /// Height the output is suppose to have
        /// </summary>
        public int _Height = 480;

        private string mUsedDeviceName;


        /// <summary>
        /// Interface for video device input.
        /// </summary>
        private VideoInput mVideoInput;

#if UNITY_WEBGL
        //Doesn't seem to be supported in WebGL
        public static readonly bool ASYNC_READBACK = false;
#else
        public static readonly bool ASYNC_READBACK = true;
#endif

        private void Awake()
        {
            mUsedDeviceName = _DeviceName;
            mRtBuffer = new RenderTexture(_Width, _Height, 16, RenderTextureFormat.ARGB32);

            mRtBuffer.wrapMode = TextureWrapMode.Repeat;

        }

        // Use this for initialization
        void Start()
        {
            mVideoInput = new VideoInput();
            mTexture = new Texture2D(_Width, _Height, mVideoInput.Format, false);
            mVideoInput.AddDevice(mUsedDeviceName, _Width, _Height, _Fps);

        }

        private void OnDestroy()
        {
            Destroy(mRtBuffer);
            Destroy(mTexture);

            if (mVideoInput != null)
                mVideoInput.RemoveDevice(mUsedDeviceName);

        }


        private void UpdateFrame()
        {
            //read frame data written to mTexture last time
#if ALLOW_UNSAFE

            //this avoids one copy compared to unity 2017 method
            var data = mTexture.GetRawTextureData<byte>();
            unsafe
            {
                var ptr = (IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data);
                //update the internal WebRTC device 
                mVideoInput.UpdateFrame(mUsedDeviceName, ptr, data.Length, mTexture.width, mTexture.height, 0, true);
            }
#else


            mVideoInput.UpdateFrame(mUsedDeviceName, mTexture, 0, true);

#endif
        }

        void OnAsyncGPUReadback(AsyncGPUReadbackRequest req)
        {
            //This doesn't seem to work well in WebGL apps. It appears to fail randomly.
            if (req.hasError)
            {
                Debug.LogError("AsyncGPUReadbackRequest has returned an error. Skipping frame.");
                return;
            }

            var data = req.GetData<byte>();
            unsafe
            {
                var ptr = (IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data);
                mVideoInput.UpdateFrame(mUsedDeviceName, ptr, data.Length, req.width, req.height, 0, true);
            }

        }


        void Update()
        {
            //ensure correct fps
            float deltaSample = 1.0f / _Fps;
            mLastSample += Time.deltaTime;
            if (mLastSample >= deltaSample)
            {
                mLastSample -= deltaSample;

                if (ASYNC_READBACK == false)
                {
                    //access frame data we read during a previous frame.
                    UpdateFrame();
                }

                //backup the current configuration to restore it later
                var oldTargetTexture = _Camera.targetTexture;
                var oldActiveTexture = RenderTexture.active;

                //Set the buffer as target and render the view of the camera into it
                _Camera.targetTexture = mRtBuffer;
                _Camera.Render();
                if(ASYNC_READBACK)
                {
                    //we read mTexture on the next loop giving the GPU time to sync
                    UnityEngine.Rendering.AsyncGPUReadbackRequest request = UnityEngine.Rendering.AsyncGPUReadback.Request(mRtBuffer, 0, mVideoInput.Format, OnAsyncGPUReadback);
                }
                else
                {
                    //without async readback we still try to add some delay between the access on the GPU
                    //and access on CPU. Move the pixels on the GPU to the pixels at the end of a frame
                    //and then access it on the next 
                    RenderTexture.active = mRtBuffer;
                    mTexture.ReadPixels(new Rect(0, 0, mRtBuffer.width, mRtBuffer.height), 0, 0, false);
                    mTexture.Apply();
                }
                


                //reset the camera/active render texture  in case it is still used for other purposes
                _Camera.targetTexture = oldTargetTexture;
                RenderTexture.active = oldActiveTexture;

                //update debug output if available
                if (_DebugTarget != null)
                {

                    if (ASYNC_READBACK)
                    {
                        _DebugTarget.texture = mRtBuffer;
                    }
                    else
                    {
                        _DebugTarget.texture = mTexture;
                    }
                }
            }
        }

    }
}
#endif
