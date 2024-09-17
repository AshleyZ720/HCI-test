#if USING_XR_MANAGEMENT && USING_XR_SDK_OCULUS && !OVRPLUGIN_UNSUPPORTED_PLATFORM
#define USING_XR_SDK
#endif

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Oculus.Avatar2
{
    /// <summary>
    /// This editor script adds a preprocess build step that copies the Avatars SDK streaming assets that are required
    /// on the current target platform to the project's StreamingAssets folder. The copied assets are cleaned up after
    /// the build finishes.
    /// Run this manually from the AvatarSDK2 > Streaming Assets menu.
    /// </summary>
    public class CoreAssetsIncluder : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private static char s = Path.DirectorySeparatorChar;

        private static readonly string AvatarAssetsZip = $"Oculus{s}OvrAvatar2Assets.zip";


        private static readonly List<string> AssetsToCopy = new List<string>();

        public int callbackOrder => default;

        public void OnPreprocessBuild(BuildReport report)
        {
            Application.logMessageReceived += OnBuildError; // Start listening for errors
            CopyAssets();
        }

        private static void OnBuildError(string condition, string stacktrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
            {
                Application.logMessageReceived -= OnBuildError; // Stop listening for errors
                EditorApplication.update += CleanUpAssets; // Clean up after build stops
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            Application.logMessageReceived -= OnBuildError; // Stop listening for errors
            CleanUpAssets();
        }


        [MenuItem("MetaAvatarsSDK/Assets/Core Assets/Copy Assets")]
        private static void CopyAssets()
        {
            OvrAvatarLog.LogInfo("Copying core Avatar2 assets to StreamingAssets",
                nameof(CoreAssetsIncluder));

            AssetsToCopy.Clear();
            AssetsToCopy.Add(AvatarAssetsZip);


            CopyFiles();

            AssetDatabase.Refresh();
        }

        private static void CleanUpAssets()
        {
            EditorApplication.update -= CleanUpAssets;
            OvrAvatarLog.LogInfo("Cleaning up Avatar2 core assets", nameof(CoreAssetsIncluder));

            // Clean up the files that were copied
            DeleteFiles(AssetsToCopy);

            // This needs to run on the next editor update because Unity sometimes crashes when calling
            // AssetDatabase.Refresh() within a logMessageReceived callback
            EditorApplication.update += RefreshAfterCleanUp;
        }

        private static void RefreshAfterCleanUp()
        {
            EditorApplication.update -= RefreshAfterCleanUp;
            AssetDatabase.Refresh();
        }

        private static void CopyFiles()
        {
            foreach (var path in AssetsToCopy)
            {
                var source = GetSourcePath(path);
                var destination = GetDestinationPath(path);
                CopyFile(source, destination);
            }
        }

        private static void CopyFile(string source, string destination)
        {
            if (!File.Exists(source))
            {
                OvrAvatarLog.LogWarning("Trying to copy an asset that doesn't exist: " + source,
                    nameof(CoreAssetsIncluder));
                return;
            }

            if (File.Exists(destination))
            {
                OvrAvatarLog.LogWarning(
                    $"Asset at path {destination} already exists and will be overwritten. Fix this using AvatarSDK2 > Streaming Assets > Clean up",
                    nameof(CoreAssetsIncluder));
            }

            try
            {
                var destinationDirectory = Path.GetDirectoryName(destination);
                Directory.CreateDirectory(destinationDirectory ?? throw new InvalidOperationException("Bad destination file path"));
                File.Copy(source, destination, true);
            }
            catch (IOException e)
            {
                OvrAvatarLog.LogException("Copy core asset", e, nameof(CoreAssetsIncluder));
            }
        }

        private static void DeleteFiles(List<string> paths)
        {
            var directories = new HashSet<string>();

            foreach (var path in paths)
            {
                try
                {
                    var destinationPath = GetDestinationPath(path);
                    directories.Add(Path.GetDirectoryName(destinationPath));

                    if (Directory.Exists(Path.GetDirectoryName(destinationPath)))
                    {
                        File.Delete(destinationPath);
                        File.Delete(destinationPath + ".meta");
                    }
                }
                catch (IOException e)
                {
                    OvrAvatarLog.LogException("Clean up core asset", e, nameof(CoreAssetsIncluder));
                }
            }

            // Clean up empty directories too
            foreach (var directory in directories)
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        bool isDirectoryEmpty;
                        using (var enumerator = Directory.EnumerateFileSystemEntries(directory).GetEnumerator())
                        {
                            isDirectoryEmpty = !enumerator.MoveNext();
                        }

                        if (isDirectoryEmpty)
                        {
                            Directory.Delete(directory);
                            File.Delete(directory + ".meta");
                        }
                    }
                }
                catch (IOException e)
                {
                    OvrAvatarLog.LogException("Clean up core asset", e, nameof(CoreAssetsIncluder));
                }
            }
        }

        private static string GetSourcePath(string file)
        {
            return Path.Combine(AssetsPathFinderHelper.GetCoreAssetsPath(), file);
        }

        private static string GetDestinationPath(string file)
        {
            return Path.Combine(Application.streamingAssetsPath, file);
        }

        [InitializeOnLoad]
        private static class UpgradeCheck
        {
            static UpgradeCheck()
            {
                if (!SessionState.GetBool("AvatarCoreAssetsUpgradeCheckRanOnce", false))
                {
                    EditorApplication.update += RunOnce;
                }
            }

            [MenuItem("MetaAvatarsSDK/Assets/Clean Old Assets")]
            private static void RunOnce()
            {
                EditorApplication.update -= RunOnce;
                if (DoesProjectNeedUpgrade())
                {
                    SessionState.SetBool("AvatarCoreAssetsUpgradeCheckRanOnce", true);
                    if (EditorUtility.DisplayDialog("Avatars SDK Upgrade",
                            "A previous version of Avatars SDK copied assets to this project's StreamingAssets folder during setup. The current version of Avatars SDK no longer needs these extra assets.\nWould you like to clean up this project's StreamingAssets folder?\n\nYou can also do this later using AvatarSDK2 > Streaming Assets > Clean up.",
                            "Clean up", "Ignore"))
                    {
                        CleanUpOldStreamingAssets();
                    }
                }
            }

            private static bool DoesProjectNeedUpgrade()
            {
                var oldFilePath = Path.Combine(Application.streamingAssetsPath, "Oculus", "OvrAvatar2Assets.zip");
                return File.Exists(oldFilePath);
            }

            private static void CleanUpOldStreamingAssets()
            {
                var oldStreamingAssets = new List<string>()
                {
                    $"Oculus{s}OvrAvatar2Assets.zip",
                    $"SampleAssets{s}PresetAvatars_Fastload.zip",
                    $"SampleAssets{s}PresetAvatars_Rift.zip",
                    $"SampleAssets{s}PresetAvatars_Quest.zip",
                    $"SampleAssets{s}PresetAvatars_Rift_High.zip",
                    $"SampleAssets{s}PresetAvatars_Quest_High.zip"
                };

                DeleteFiles(oldStreamingAssets);


                AssetDatabase.Refresh();
            }
        }
    }
}
