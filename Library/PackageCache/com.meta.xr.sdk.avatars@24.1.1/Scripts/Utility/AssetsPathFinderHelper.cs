using System.IO;
using UnityEngine;

namespace Oculus.Avatar2
{
    public static class AssetsPathFinderHelper
    {
        public static readonly string coreAssetsPackageName = "com.meta.xr.sdk.avatars";
        public static readonly string sampleAssetsPackageName = "com.meta.xr.sdk.avatars.sample.assets";
        private static readonly string logScope = "assetfinder";

        public static string GetCoreAssetsPath()
        {
            return Path.Combine(Application.dataPath, "Oculus", "Avatar2", "CoreAssets");
        }

        public static string GetSampleAssetsPath()
        {
            if (!Directory.Exists(Path.Combine(Application.dataPath, "Oculus", "Avatar2_SampleAssets", "SampleAssets")))
            {
                OvrAvatarLog.LogWarning($"Avatar sample assets path was requested but the path doesn't exist. This likely means that you're missing the {sampleAssetsPackageName} package. " +
                    "You can install it via AvatarSDK > Assets > Sample Assets > Import Sample Assets Package " +
                    "or by searching for it and importing it from package manager under Window > Package Manager", logScope);
            }

            return Path.Combine(Application.dataPath, "Oculus", "Avatar2_SampleAssets", "SampleAssets");
        }
    }
}
