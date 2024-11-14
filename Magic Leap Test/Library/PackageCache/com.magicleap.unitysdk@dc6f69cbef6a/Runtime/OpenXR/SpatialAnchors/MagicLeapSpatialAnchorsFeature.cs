// %BANNER_BEGIN%
// ---------------------------------------------------------------------
// %COPYRIGHT_BEGIN%
// Copyright (c) (2019-2022) Magic Leap, Inc. All Rights Reserved.
// Use of this file is governed by the Software License Agreement, located here: https://www.magicleap.com/software-license-agreement-ml2
// Terms and conditions applicable to third-party materials accompanying this distribution may also be found in the top-level NOTICE file appearing herein.
// %COPYRIGHT_END%
// ---------------------------------------------------------------------
// %BANNER_END%
#if UNITY_OPENXR_1_9_0_OR_NEWER
using System;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif // UNITY_EDITOR
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine.XR.OpenXR.NativeTypes;
using UnityEngine.XR.OpenXR.Features.MagicLeapSupport.NativeInterop;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine.XR.OpenXR.Features.MagicLeapSupport
{
    using UnityEngine.LowLevel;

#if UNITY_EDITOR
    [OpenXRFeature(UiName = "Magic Leap 2 Spatial Anchors",
        Desc = "When localized to a map, create spatial anchors at target locations.",
        Company = "Magic Leap",
        Version = "1.0.0",
        BuildTargetGroups = new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone },
        FeatureId = FeatureId,
        OpenxrExtensionStrings = ExtensionName
    )]
#endif // UNITY_EDITOR
    public partial class MagicLeapSpatialAnchorsFeature : MagicLeapOpenXRFeatureBase
    {
        public const string FeatureId = "com.magicleap.openxr.feature.ml2_spatialanchor";
        public const string ExtensionName = "XR_ML_spatial_anchors XR_EXT_future";

        public delegate void OnCreationCompleteEvent(Pose pose, ulong anchorId, XrResult result);
        public event OnCreationCompleteEvent OnCreationComplete;
        public delegate void OnCreationCompleteFromStorageEvent(Pose pose, ulong anchorId, string anchorStorageId, XrResult result);
        public event OnCreationCompleteFromStorageEvent OnCreationCompleteFromStorage;

        private int maxAnchorsCreatedPerUpdate = 30;

        /// <summary>
        /// Determine the maximum number of anchors to accept their completion status each update.
        /// </summary>
        public int MaxAnchorsCreatedPerUpdate
        {
            get { return maxAnchorsCreatedPerUpdate; }
            set
            {
                maxAnchorsCreatedPerUpdate = value;
            }
        }

        private int pendingStorageAnchors = 0;

        public int PendingStorageAnchors
        {
            get { return pendingStorageAnchors; }
            set
            {
                pendingStorageAnchors = value;
            }
        }

        public enum AnchorConfidence
        {
            NotFound = 0,
            Low = 1,
            Medium = 2,
            High = 3
        }

        private List<Pose> pendingAnchors = new List<Pose>();

        private struct AnchorsUpdateType
        { }

        internal struct AnchorCompletionStatus
        {
            internal Pose Pose;
            internal ulong Id;
            internal XrUUID AnchorStorageId;
            [MarshalAs(UnmanagedType.I1)]
            internal bool FromStorage;
            internal XrResult Result;
        }

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            if (OpenXRRuntime.IsExtensionEnabled("XR_ML_spatial_anchors"))
            {
                var updateSystem = new PlayerLoopSystem
                {
                    subSystemList = Array.Empty<PlayerLoopSystem>(),
                    type = typeof(AnchorsUpdateType),
                    updateDelegate = AnchorsPlayerLoop,
                };
                var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
                if (!Utils.InstallIntoPlayerLoop(ref playerLoop, updateSystem, Utils.InstallPath))
                    throw new Exception("Unable to install Spatial Anchors Update delegate into player loop!");

                PlayerLoop.SetPlayerLoop(playerLoop);

                return base.OnInstanceCreate(xrInstance);
            }
            Debug.LogError($"{ExtensionName} is not enabled. Disabling {nameof(MagicLeapSpatialAnchorsFeature)}");
            return false;
        }

        public bool CreateSpatialAnchor(Pose pose)
        {
            var resultCode = NativeBindings.MLOpenXRCreateSpatialAnchor(pose);
            bool createSucceded = Utils.DidXrCallSucceed(resultCode, nameof(NativeBindings.MLOpenXRCreateSpatialAnchor));

            if(createSucceded)
            {
                pendingAnchors.Add(pose);
            }

            return createSucceded;

        }

        public AnchorConfidence GetAnchorConfidence(ulong AnchorId)
        {
            uint anchorCon = 0;
            var result = NativeBindings.MLOpenXRGetSpatialAnchorConfidence(AnchorId, out anchorCon);

            if(result != XrResult.Success)
            {
                Debug.LogError("GetAnchorConfidence failed at " + AnchorId.ToString() + " with result: " + result );
            }
            else
            {
                // AnchorConfidence contains an additional Enum for Not Found.
                anchorCon++;
            }


            return (AnchorConfidence)anchorCon;
        }

        public Pose GetPosefromAnchorId(ulong AnchorId)
        {
            Pose returnedPose;

            NativeBindings.MLOpenXRGetSpatialAnchorPose(in AnchorId, out returnedPose);

            return returnedPose;
        }

        public bool DeleteLocalSpatialAnchor(ulong AnchorId)
        {
            var resultCode = NativeBindings.MLOpenXRDeleteLocalSpatialAnchor(in AnchorId);
            return Utils.DidXrCallSucceed(resultCode, nameof(NativeBindings.MLOpenXRCreateSpatialAnchor));
        }

        private void AnchorsPlayerLoop()
        {
            if(pendingAnchors.Count > 0 || pendingStorageAnchors > 0)
            {
                int completedLength = maxAnchorsCreatedPerUpdate;
                unsafe
                {
                    using NativeArray<AnchorCompletionStatus> completed = new NativeArray<AnchorCompletionStatus>(completedLength, Allocator.Temp);

                    bool fromPose = (pendingAnchors.Count > 0);
                    
                    NativeBindings.MLOpenXRCheckSpatialAnchorCompletion((AnchorCompletionStatus*)completed.GetUnsafePtr(), &completedLength, fromPose);

                    if (completedLength > 0)
                    {
                        // NativeSlice contrains the number of values to the actual amount of anchors that have completed creation.
                        NativeSlice<AnchorCompletionStatus> finalComplete = new NativeSlice<AnchorCompletionStatus>(completed, 0, completedLength);

                        foreach (AnchorCompletionStatus status in finalComplete)
                        {
                            if (!status.FromStorage)
                            {
                                if (pendingAnchors.Contains(status.Pose))
                                {
                                    pendingAnchors.Remove(status.Pose);
                                }
                                else
                                {
                                    Debug.LogWarning("MagicLeapSpatialAnchorsFeature received a completion status for a pose not requested at location: " + status.Pose.ToString());
                                }

                                OnCreationComplete.Invoke(status.Pose, status.Id, status.Result);
                            }
                            else
                            {
                                pendingStorageAnchors--;

                                OnCreationCompleteFromStorage.Invoke(status.Pose, status.Id, status.AnchorStorageId.ToString(), status.Result);
                            }
                        }

                        if (pendingStorageAnchors < 0)
                        {
                            pendingStorageAnchors = 0;
                        }
                    }
                }
            }
        }
    }
}
#endif // UNITY_OPENXR_1_9_0_OR_NEWER
