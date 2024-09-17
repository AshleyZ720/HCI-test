using Oculus.Avatar2;
using System.Collections.Generic;
using UnityEngine;

namespace Oculus.Avatar2
{
    /// <summary>
    /// EyePose behavior that enables eye tracking through OVRPlugin.
    /// </summary>
    public class OvrAvatarEyeTrackingBehaviorOvrPlugin : OvrAvatarEyePoseBehavior
    {
        private OvrAvatarEyePoseProviderBase _eyePoseProvider;
        private static readonly CAPI.ovrAvatar2Platform[] s_eyeTrackingEnabledPlatforms = { CAPI.ovrAvatar2Platform.QuestPro, CAPI.ovrAvatar2Platform.PC };

        public override OvrAvatarEyePoseProviderBase EyePoseProvider
        {
            get
            {
                InitializeEyePoseProvider();

                return _eyePoseProvider;
            }
        }

        private void InitializeEyePoseProvider()
        {
            if (_eyePoseProvider != null || OvrAvatarManager.Instance == null)
            {
                return;
            }

            // Check for unsupported Eye Tracking platforms
            if (!s_eyeTrackingEnabledPlatforms.Contains(OvrAvatarManager.Instance.Platform))
            {
                return;
            }

            // check for Link connection
            if (OvrAvatarManager.Instance.Platform == CAPI.ovrAvatar2Platform.PC && !OvrAvatarUtility.IsHeadsetActive())
            {
                return;
            }

            OvrAvatarManager.Instance.RequestEyeTrackingPermission();

            if (OvrAvatarManager.Instance.OvrPluginEyePoseProvider != null)
            {
                OvrAvatarLog.LogInfo("Eye tracking service available");
                _eyePoseProvider = OvrAvatarManager.Instance.OvrPluginEyePoseProvider;
            }
            else
            {
                OvrAvatarLog.LogWarning("Eye tracking service unavailable");
            }
        }
    }
}
