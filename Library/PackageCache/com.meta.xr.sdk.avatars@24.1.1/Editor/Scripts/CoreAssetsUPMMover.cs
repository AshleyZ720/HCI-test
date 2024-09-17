// (c) Meta Platforms, Inc. and affiliates. Confidential and proprietary

using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Oculus.Avatar2
{
    // ScriptableObject classname must match the filename for it to get the path correctly
    public class CoreAssetsUPMMover : ScriptableObject
    {
        private static string CoreAssetsDest => Path.Combine(Application.dataPath, "Oculus", "Avatar2", "CoreAssets");

        public static string GetBuildNumber()
        {
            var filePath = GetBuildNumberFilePath();
            if (!File.Exists(filePath))
            {
                Debug.LogError($"Could not find build number at path {filePath}");
                return "error";
            }

            try
            {
                return File.ReadAllText(filePath).Trim();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            return "error";
        }

        public static async void CopyFilesOver()
        {
            await Task.Yield();
            if (!CopyFiles())
            {
                return;
            }
            await Task.Yield();
            File.WriteAllText(GetVersionFilePath(), $"{GetBuildNumber()}\n");
            await Task.Yield();
            AssetDatabase.Refresh();
        }


        private static bool CopyFiles()
        {
            string sourcePath = Path.Combine(GetSourceAssetsPath(), "CoreAssets");
            DirectoryInfo sourceDir = new DirectoryInfo(sourcePath);
            try
            {
                if (!sourceDir.Exists)
                {
                    Debug.LogError("CoreAssetsUPMMover failed to find path: " + sourcePath);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("CoreAssetsUPMMover failed to find path: " + sourcePath + "\n\n" + e);
            }

            try
            {
                if (Directory.Exists(CoreAssetsDest))
                {
                    Debug.Log("CoreAssetsUPMMover is deleting the old version of CoreAssets.");
                    Directory.Delete(CoreAssetsDest);
                }
                CopyFilesRecursively(sourceDir, Directory.CreateDirectory(CoreAssetsDest));
            }
            catch (Exception e)
            {
                Debug.LogError("CoreAssetsUPMMover was unable to copy `" + sourcePath + "` to `" + CoreAssetsDest + "`." + "\n\n" + e);
                return false;
            }

            if (!Directory.Exists(CoreAssetsDest))
            {
                Debug.LogError("CoreAssetsUPMMover was unable to copy `" + sourcePath + "` to `" + CoreAssetsDest + "`." + "\n\n");
                return false;
            }
            return true;
        }

        public static bool DoesFileVersionMatch()
        {
            var textAsset = GetVersionFilePath();
            if (!File.Exists(textAsset))
            {
                return false;
            }

            try
            {
                return File.ReadAllText(textAsset).Trim() == GetBuildNumber();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            return false;
        }

        private static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target)
        {
            foreach (DirectoryInfo dir in source.GetDirectories())
            {
                CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name));
            }

            foreach (FileInfo file in source.GetFiles())
            {
                if (file.Extension == ".meta")
                {
                    continue;
                }

                Debug.Log("CoreAssetsUPMMover is copying " + file.FullName + " over to your Assets folder");
                file.CopyTo(Path.Combine(target.FullName, file.Name));
            }
        }

        private static string GetBuildNumberFilePath()
        {
            return Path.Combine(GetSourceAssetsPath(), "build_number.txt");
        }

        private static string GetVersionFilePath()
        {
            return Path.Combine(CoreAssetsDest, ".avatar_sdk_assets_version.txt");
        }

        // path to the containing directory of this directory or file
        private static string UpOneLevel(string path)
        {
            var lastIndex = path.LastIndexOfAny(new[] { '/', '\\' });
            return path.Substring(0, lastIndex);
        }

        private static string GetSourceAssetsPath()
        {
            // Get path to this script
            CoreAssetsUPMMover tmpInstance =
                ScriptableObject.CreateInstance<CoreAssetsUPMMover>();
            MonoScript ms = MonoScript.FromScriptableObject(tmpInstance);
            var path = AssetDatabase.GetAssetPath(ms);
            // go up 3 levels from this file to find the main package directory
            path = UpOneLevel(UpOneLevel(UpOneLevel(path)));
            return path;
        }
    }

    // Check for changes whenever Unity loads up this script.
    [InitializeOnLoad]
    public class StartupTrigger
    {
        static StartupTrigger()
        {
            // defer until the scene has loaded
            EditorApplication.update += RunOnce;
        }

        static void RunOnce()
        {
            EditorApplication.update -= RunOnce;
            if (!CoreAssetsUPMMover.DoesFileVersionMatch())
            {
                CoreAssetsUPMMover.CopyFilesOver();
            }
        }
    }
}
