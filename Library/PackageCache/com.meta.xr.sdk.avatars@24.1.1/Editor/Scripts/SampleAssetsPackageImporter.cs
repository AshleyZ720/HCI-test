using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

public class SampleAssetsPackageImporter : ScriptableObject
{
    static AddRequest Request;

    [MenuItem("MetaAvatarsSDK/Assets/Sample Assets/Import Sample Assets Package")]
    public static void ImportSampleAssetsPackage()
    {
        // Add a package to the project
        Request = Client.Add("com.meta.xr.sdk.avatars.sample.assets@24.1.1");
        EditorApplication.update += Progress;
    }

    private static void Progress()
    {
        if (Request.IsCompleted)
        {
            if (Request.Status == StatusCode.Success)
                Debug.Log("Installed: " + Request.Result.packageId);

            else if (Request.Status >= StatusCode.Failure)
                Debug.Log(Request.Error.message);

            EditorApplication.update -= Progress;
        }
    }

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

    private static bool DoesFileVersionMatch()
    {
        var textAsset = GetVersionFilePath();
        if (!File.Exists(textAsset))
        {
            return false;
        }

        try
        {

            string buildNumber = GetBuildNumber();
            return (buildNumber == "error" || File.ReadAllText(textAsset).Trim() == buildNumber);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }

        return false;
    }

    private static string GetBuildNumberFilePath()
    {
        return Path.Combine(GetSourceAssetsPath(), "build_number.txt");
    }

    public static string GetVersionFilePath()
    {
        return Path.Combine(Application.dataPath, ".avatar_sdk_sample_assets_importer.txt");
    }

    private static string UpOneLevel(string path)
    {
        var lastIndex = path.LastIndexOfAny(new[] { '/', '\\' });
        return path.Substring(0, lastIndex);
    }

    private static string GetSourceAssetsPath()
    {
        // Get path to this script
        SampleAssetsPackageImporter tmpInstance =
            ScriptableObject.CreateInstance<SampleAssetsPackageImporter>();
        MonoScript ms = MonoScript.FromScriptableObject(tmpInstance);
        var path = AssetDatabase.GetAssetPath(ms);
        // go up 3 levels from this file to find the main package directory
        path = UpOneLevel(UpOneLevel(UpOneLevel(path)));
        return path;
    }
    public static bool ShouldShowSampleAssetsPackageImporterWindow()
    {
        return !DoesFileVersionMatch();
    }
}

[InitializeOnLoad]
public class SampleAssetsPackageImporterTrigger
{
    static SampleAssetsPackageImporterTrigger()
    {
        if (!SessionState.GetBool("SampleAssetsPackageImporterRanOnce", false))
        {
            EditorApplication.update += RunOnce;
        }
    }

    static void RunOnce()
    {
        bool sampleAssetsImporterShowWindow = SampleAssetsPackageImporter.ShouldShowSampleAssetsPackageImporterWindow();
        if (sampleAssetsImporterShowWindow)
        {
            SampleAssetsPackageImporterPopupDialog.ShowWindow();
        }
        SessionState.SetBool("SampleAssetsPackageImporterRanOnce", true);
        EditorApplication.update -= RunOnce;
    }
}

public class SampleAssetsPackageImporterPopupDialog : EditorWindow
{
    private static SampleAssetsPackageImporterPopupDialog _instance;
    public static void ShowWindow()
    {
        if (_instance)
        {
            return;
        }

        EditorApplication.quitting += Quit;
        float width = 500;
        float height = 175;
        float x = (EditorGUIUtility.GetMainWindowPosition().center.x - width / 2.0f);
        float y = 0;
        var window = GetWindow(typeof(SampleAssetsPackageImporterPopupDialog), true, "[Optional] Import Meta Avatars SDK Sample Assets", true);
        window.position = new Rect(x, y, width, height);
    }

    private void OnEnable()
    {
        _instance = this;
    }

    static void Quit()
    {
        if (_instance)
        {
            _instance.Close();
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(
            "Would you like to import the Meta Avatars SDK Sample Assets?\n\n" +
            "These assets include preset avatars used in the Meta Avatars SDK examples. " +
            "These presets are most comonly used to provide an avatar to someone that might not have an avatar asociated with their account " +
            "or as avatar representations for NPCs in your app.\n\n" +
            "Note: Importing these assets will increase the size of your final app.",
            EditorStyles.wordWrappedLabel);
        GUILayout.Space(10);

        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Yes"))
            {
                File.WriteAllText(SampleAssetsPackageImporter.GetVersionFilePath(), $"{SampleAssetsPackageImporter.GetBuildNumber()}\n");
                _instance.Close();
                SampleAssetsPackageImporter.ImportSampleAssetsPackage();
                EditorApplication.quitting -= Quit;
            }

            if (GUILayout.Button("No"))
            {
                File.WriteAllText(SampleAssetsPackageImporter.GetVersionFilePath(), $"{SampleAssetsPackageImporter.GetBuildNumber()}\n");
                _instance.Close();
                EditorApplication.quitting -= Quit;
            }
        }
    }
}
