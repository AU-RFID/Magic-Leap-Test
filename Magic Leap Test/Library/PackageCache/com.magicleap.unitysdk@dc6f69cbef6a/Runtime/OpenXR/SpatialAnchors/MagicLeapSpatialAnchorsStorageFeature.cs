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
using UnityEngine.XR.OpenXR.NativeTypes;
using UnityEngine.XR.OpenXR.Features.MagicLeapSupport.NativeInterop;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine.XR.OpenXR.Features.MagicLeapSupport
{
    using UnityEngine.LowLevel;

#if UNITY_EDITOR
    [OpenXRFeature(UiName = "Magic Leap 2 Spatial Anchors Storage",
        Desc = "Expand Spatial Anchors to allow to Publish, Delete, and update on Localized Maps.",
        Company = "Magic Leap",
        Version = "1.0.0",
        BuildTargetGroups = new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone },
        FeatureId = FeatureId,
        OpenxrExtensionStrings = ExtensionName
    )]
#endif // UNITY_EDITOR
    public partial class MagicLeapSpatialAnchorsStorageFeature : MagicLeapOpenXRFeatureBase
    {
        public const string FeatureId = "com.magicleap.openxr.feature.ml2_spatialanchorstorage";
        public const string ExtensionName = "XR_ML_spatial_anchors_storage XR_EXT_future";

        public delegate void OnPublishCompleteEvent(ulong anchorId, string anchorMapPositionId);
        public event OnPublishCompleteEvent OnPublishComplete;

        public delegate void OnQueryCompleteEvent(List<string> anchorMapPositionId);
        public event OnQueryCompleteEvent OnQueryComplete;

        public delegate void OnDeletedCompleteEvent(List<string> anchorMapPositionId);
        public event OnDeletedCompleteEvent OnDeletedComplete;

        public delegate void OnUpdateExpirationCompleteEvent(List<string> anchorMapPositionId);
        public event OnUpdateExpirationCompleteEvent OnUpdateExpirationCompleted;

        private int maxQueriesAnchorsExpected = 30;

        /// <summary>
        /// Set the maximum number of anchors expected from Anchors Storage.
        /// </summary>
        public int MaxQueriesAnchorsExpected
        {
            get { return maxQueriesAnchorsExpected; }
            set
            {
                maxQueriesAnchorsExpected = value;
            }
        }

        private MagicLeapSpatialAnchorsFeature anchorsFeature = null;

        private int pendingPublishRequests = 0;
        private int pendingQueries = 0;
        private int pendingDeleteRequests = 0;
        private int pendingUpdateRequests = 0;

        private struct AnchorsStorageUpdateType
        { }

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            if (OpenXRRuntime.IsExtensionEnabled("XR_ML_spatial_anchors_storage"))
            {
                var updateSystem = new PlayerLoopSystem
                {
                    subSystemList = Array.Empty<PlayerLoopSystem>(),
                    type = typeof(AnchorsStorageUpdateType),
                    updateDelegate = AnchorsStoragePlayerLoop,
                };
                var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
                if (!Utils.InstallIntoPlayerLoop(ref playerLoop, updateSystem, Utils.InstallPath))
                    throw new Exception("Unable to install Spatial Anchors Storage Update delegate into player loop!");

                PlayerLoop.SetPlayerLoop(playerLoop);

                return base.OnInstanceCreate(xrInstance);
            }
            Debug.LogError($"{ExtensionName} is not enabled. Disabling {nameof(MagicLeapSpatialAnchorsStorageFeature)}");
            return false;
        }

        /// <summary>
        /// Anchors Storage must be started for the feature to function properly.
        /// </summary>
        public bool StartSpatialAnchorStorage()
        {
            var result = NativeBindings.MLOpenXRCreateSpatialAnchorStorage();
            if (result != XrResult.Success)
            {
                Debug.LogError($"XR_ML_spatial_anchors_storage will not function properly due to failure to start Spatial Anchor Storage.");
                return false;
            }

            return true;
        }

        public bool CreateSpatialAnchorsFromStorage(List<String> AnchorMapPositionIds)
        {
            if(AnchorMapPositionIds.Count == 0)
            {
                Debug.LogError("CreateSpatialAnchors sent an empty list of AnchorMapPositionIds.");
                return false;
            }

            if(anchorsFeature == null)
            {
                anchorsFeature = OpenXRSettings.Instance.GetFeature<MagicLeapSpatialAnchorsFeature>();
            }

            if (!anchorsFeature.enabled)
            {
                Debug.LogError("CreateSpatialAnchors requires an active MagicLeapSpatialAnchorsFeature OpenXRfeature.");
                return false;
            }

            unsafe
            {
                NativeArray<XrUUID> anchorUuidsToCreate = new NativeArray<XrUUID>(AnchorMapPositionIds.Count, Allocator.Temp);

                for (int i = 0; i < anchorUuidsToCreate.Length; ++i)
                {
                    anchorUuidsToCreate[i] = new XrUUID(AnchorMapPositionIds[i]);
                }

                var resultCode = NativeBindings.MLOpenXRCreateSpatialAnchors((NativeInterop.XrUUID *)anchorUuidsToCreate.GetUnsafePtr(), anchorUuidsToCreate.Length);
                bool xrCallSucceded = Utils.DidXrCallSucceed(resultCode, nameof(NativeBindings.MLOpenXRCreateSpatialAnchors));

                if (!xrCallSucceded)
                {
                    Debug.LogError("CreateSpatialAnchors failed to send request to create anchors from AnchorMapPositionId list.");
                }

                anchorsFeature.PendingStorageAnchors += AnchorMapPositionIds.Count;

                return xrCallSucceded;
            }
        }

        /// <summary>
        /// Determine the maximum number of anchors to accept their completion status each update.
        /// </summary>
        /// <param name="position">The position of the center of the query</param>
        /// <param name="radius">The radius of the search area in meters.</param>
        public bool QueryStoredSpatialAnchors(Vector3 position, float radius)
        {
            var resultCode = NativeBindings.MLOpenXRQuerySpatialAnchorsStorage(position, radius);
            bool xrCallSucceded = Utils.DidXrCallSucceed(resultCode, nameof(NativeBindings.MLOpenXRQuerySpatialAnchorsStorage));

            if (!xrCallSucceded)
            {
                Debug.LogError("QueryStoredSpatialAnchors failed to send request to query for AnchorMapPositionId list around a specific position.");
            }

            pendingQueries++;

            return xrCallSucceded;
        }

        /// <summary>
        /// Publish local anchors to Spatial Anchor Storage.
        /// </summary>
        /// <param name="AnchorIds">The list of AnchorIds to publish. These were assigned in the OnCreationComplete event in MagicLeapSpatialAnchorsFeature.</param>
        /// <param name="expiration">The time in seconds since epoch after which these anchors may: expire. Use 0 for permanent anchors. The system may retain them longer.</param>
        public bool PublishSpatialAnchorsToStorage(List<ulong> AnchorIds, ulong expiration)
        {
            if (AnchorIds.Count == 0)
            {
                Debug.LogError("PublishSpatialAnchorsToStorage sent an empty list of AnchorIds.");
                return false;
            }

            unsafe
            {
                using NativeArray<ulong> anchorsToPublish = new NativeArray<ulong>(AnchorIds.ToArray(), Allocator.Temp);

                var resultCode = NativeBindings.MLOpenXRPublishSpatialAnchors((ulong*)anchorsToPublish.GetUnsafePtr(), AnchorIds.Count, expiration);
                bool xrCallSucceded = Utils.DidXrCallSucceed(resultCode, nameof(NativeBindings.MLOpenXRCreateSpatialAnchors));

                if (!xrCallSucceded)
                {
                    Debug.LogError("PublishSpatialAnchorsToStorage failed to send request to publish anchors from AnchorId list.");
                }

                pendingPublishRequests += AnchorIds.Count;

                return xrCallSucceded;
            }
        }

        /// <summary>
        /// Delete published anchors from Spatial Anchor Storage.
        /// </summary>
        /// <param name="AnchorMapPositionIds">The list of AnchorMapPositionIds to Delete. These were assigned in the OnPublishComplete event in MagicLeapSpatialAnchorsStorageFeature.</param>
        public bool DeleteStoredSpatialAnchor(List<string> AnchorMapPositionIds)
        {
            if (AnchorMapPositionIds.Count == 0)
            {
                Debug.LogError("DeleteStoredSpatialAnchor sent an empty list of AnchorMapPositionIds.");
                return false;
            }

            unsafe
            {
                NativeArray<XrUUID> anchorUuidsToDelete = new NativeArray<XrUUID>(AnchorMapPositionIds.Count, Allocator.Temp);

                for (int i = 0; i < anchorUuidsToDelete.Length; ++i)
                {
                    anchorUuidsToDelete[i] = new XrUUID(AnchorMapPositionIds[i]);
                }

                var resultCode = NativeBindings.MLOpenXRDeleteSpatialAnchors((NativeInterop.XrUUID*)anchorUuidsToDelete.GetUnsafePtr(), anchorUuidsToDelete.Length);
                bool xrCallSucceded = Utils.DidXrCallSucceed(resultCode, nameof(NativeBindings.MLOpenXRCreateSpatialAnchors));

                if (!xrCallSucceded)
                {
                    Debug.LogError("CreateSpatialAnchors failed to send request to create anchors from AnchorMapPositionId list.");
                }

                pendingDeleteRequests += AnchorMapPositionIds.Count;

                return xrCallSucceded;
            }
        }

        /// <summary>
        /// Update the exiration time for published anchors in Spatial Anchor Storage.
        /// </summary>
        /// <param name="AnchorMapPositionIds">The list of AnchorMapPositionIds to Delete. These were assigned in the OnPublishComplete event in MagicLeapSpatialAnchorsStorageFeature.</param>
        /// <param name="expiration">The time in seconds since epoch after which these anchors may: expire. The system may retain them longer.</param>
        public bool UpdateExpirationonStoredSpatialAnchor(List<string> AnchorMapPositionIds, ulong expiration)
        {
            if (AnchorMapPositionIds.Count == 0)
            {
                Debug.LogError("UpdateExpirationonStoredSpatialAnchor sent an empty list of AnchorMapPositionIds.");
                return false;
            }

            unsafe
            {
                NativeArray<XrUUID> anchorUuidsToUpdate = new NativeArray<XrUUID>(AnchorMapPositionIds.Count, Allocator.Temp);

                for (int i = 0; i < anchorUuidsToUpdate.Length; ++i)
                {
                    anchorUuidsToUpdate[i] = new XrUUID(AnchorMapPositionIds[i]);
                }

                var resultCode = NativeBindings.MLOpenXRUpdateSpatialAnchorsExpiration((XrUUID*)anchorUuidsToUpdate.GetUnsafePtr(), AnchorMapPositionIds.Count, expiration);
                bool xrCallSucceded = Utils.DidXrCallSucceed(resultCode, nameof(NativeBindings.MLOpenXRUpdateSpatialAnchorsExpiration));

                if (!xrCallSucceded)
                {
                    Debug.LogError("UpdateExpirationonStoredSpatialAnchor failed to send request to update anchors expiration from AnchorMapPositionId list.");
                }

                pendingUpdateRequests += AnchorMapPositionIds.Count;

                return xrCallSucceded;
            }
        }

        private void AnchorsStoragePlayerLoop()
        {
            if (pendingPublishRequests > 0)
            {
                int completedLength = pendingPublishRequests;
                unsafe
                {
                    using NativeArray<XrUUID> completedUuids = new NativeArray<XrUUID>(completedLength, Allocator.Temp);
                    using NativeArray<ulong> completedIds = new NativeArray<ulong>(completedLength, Allocator.Temp);

                    NativeBindings.MLOpenXRCheckPublishSpatialAnchorsCompletion((XrUUID*)completedUuids.GetUnsafePtr(), (ulong*)completedIds.GetUnsafePtr(), &completedLength);

                    if (completedLength > 0)
                    {
                        // NativeSlice contrains the number of values to the actual amount of anchors that have completed Publishing.
                        NativeSlice<XrUUID> finalCompleteUuids = new NativeSlice<XrUUID>(completedUuids, 0, completedLength);
                        NativeSlice<ulong> finalCompleteIds = new NativeSlice<ulong>(completedIds, 0, completedLength);

                        for(int i = 0; i < completedLength; i++)
                        {
                            OnPublishComplete.Invoke(finalCompleteIds[i], finalCompleteUuids[i].ToString());
                            pendingPublishRequests--;
                        }
                    }

                    if(pendingPublishRequests<0)
                    {
                        pendingPublishRequests = 0;
                    }
                }
            }

            if(pendingQueries > 0)
            {
                int completedQueriesLength = maxQueriesAnchorsExpected;
                unsafe
                {
                    using NativeArray<XrUUID> QueriedUuids = new NativeArray<XrUUID>(completedQueriesLength, Allocator.Temp);

                    NativeBindings.MLOpenXRCheckQuerySpatialAnchorCompletion((XrUUID*)QueriedUuids.GetUnsafePtr(), &completedQueriesLength);

                    if (completedQueriesLength > 0)
                    {
                        if (completedQueriesLength > maxQueriesAnchorsExpected)
                        {
                            Debug.LogError("QueryStoredSpatialAnchors returned a list of AnchorMapPositionIds greater than MaxQueriesAnchorsExpected.");
                        }

                        NativeSlice<XrUUID> finalQueriedUuids = new NativeSlice<XrUUID>(QueriedUuids, 0, completedQueriesLength);

                        List<string> returnedUuids = new List<string>();

                        foreach(XrUUID uuid in finalQueriedUuids)
                        {
                            returnedUuids.Add(uuid.ToString());
                        }

                        OnQueryComplete.Invoke(returnedUuids);

                        pendingQueries--;
                    }
                }

                if (pendingQueries < 0)
                {
                    pendingQueries = 0;
                }
            }

            if(pendingDeleteRequests > 0)
            {
                int completedDeletedLength = pendingDeleteRequests;
                unsafe
                {
                    using NativeArray<XrUUID> deletedUuids = new NativeArray<XrUUID>(completedDeletedLength, Allocator.Temp);

                    NativeBindings.MLOpenXRCheckDeleteSpatialAnchorsCompletion((XrUUID*)deletedUuids.GetUnsafePtr(), &completedDeletedLength);

                    if (completedDeletedLength > 0)
                    {
                        NativeSlice<XrUUID> finalDeletedUuids = new NativeSlice<XrUUID>(deletedUuids, 0, completedDeletedLength);

                        List<string> returnedDeletedUuids = new List<string>();

                        foreach (XrUUID uuid in finalDeletedUuids)
                        {
                            returnedDeletedUuids.Add(uuid.ToString());
                        }

                        OnDeletedComplete.Invoke(returnedDeletedUuids);

                        pendingDeleteRequests--;
                    }
                }
                if(pendingDeleteRequests < 0)
                {
                    pendingDeleteRequests = 0;
                }
            }

            if(pendingUpdateRequests > 0)
            {
                int completedUpdatedLength = pendingUpdateRequests;
                unsafe
                {
                    using NativeArray<XrUUID> updatedUuids = new NativeArray<XrUUID>(completedUpdatedLength, Allocator.Temp);

                    NativeBindings.MLOpenXRCheckDeleteSpatialAnchorsCompletion((XrUUID*)updatedUuids.GetUnsafePtr(), &completedUpdatedLength);

                    if (completedUpdatedLength > 0)
                    {
                        NativeSlice<XrUUID> finalUpdatedUuids = new NativeSlice<XrUUID>(updatedUuids, 0, completedUpdatedLength);

                        List<string> returnedUpdatedUuids = new List<string>();

                        foreach (XrUUID uuid in finalUpdatedUuids)
                        {
                            returnedUpdatedUuids.Add(uuid.ToString());
                        }

                        OnUpdateExpirationCompleted.Invoke(returnedUpdatedUuids);

                        pendingUpdateRequests--;
                    }
                }

                if (pendingUpdateRequests < 0)
                {
                    pendingUpdateRequests = 0;
                }
            }
        }

        protected override void OnInstanceDestroy(ulong xrInstance)
        {
            var result = NativeBindings.MLOpenXRDestroySpatialAnchorStorage();
            if (result != XrResult.Success)
            {
                Debug.LogError($"MagicLeapSpatialAnchorsStorageFeature failed to Destry Spatial Anchor Storage.");
            }
        }
    }
}
#endif // UNITY_OPENXR_1_9_0_OR_NEWER
