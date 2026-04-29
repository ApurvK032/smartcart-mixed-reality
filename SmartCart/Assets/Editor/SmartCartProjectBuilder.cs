using System;
using System.IO;
using System.Xml;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.SpatialTracking;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

public static class SmartCartProjectBuilder
{
    public const string ApplicationId = "com.apurv.smartcart";
    public const string ScenePath = "Assets/Scenes/SmartCart.unity";
    public const string ApkPath = "Builds/SmartCart.apk";

    [MenuItem("SmartCart/Configure Project")]
    public static void ConfigureProject()
    {
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Builds");

        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            throw new InvalidOperationException("Could not switch Unity build target to Android.");

        EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
        EditorUserBuildSettings.buildAppBundle = false;

        PlayerSettings.companyName = "SmartCart";
        PlayerSettings.productName = "SmartCart";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, ApplicationId);
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)29;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });

        ConfigureXR();
        EnsureScene();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("SmartCart/Build APK")]
    public static void ConfigureAndBuild()
    {
        ConfigureProject();

        var buildOptions = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            locationPathName = ApkPath,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(buildOptions);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"SmartCart Android build failed: {report.summary.result}");

        Debug.Log("SmartCart APK built at " + Path.GetFullPath(ApkPath));
    }

    static void ConfigureXR()
    {
        var perBuildTargetSettings = GetOrCreateXRGeneralSettingsPerBuildTarget();
        if (!perBuildTargetSettings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            perBuildTargetSettings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

        var generalSettings = perBuildTargetSettings.SettingsForBuildTarget(BuildTargetGroup.Android);
        generalSettings.InitManagerOnStart = true;

        var managerSettings = perBuildTargetSettings.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        managerSettings.automaticLoading = true;
        managerSettings.automaticRunning = true;

        var loaderAssigned = XRPackageMetadataStore.AssignLoader(
            managerSettings,
            "UnityEngine.XR.OpenXR.OpenXRLoader",
            BuildTargetGroup.Android);

        if (!loaderAssigned && managerSettings.activeLoaders.Count == 0)
            throw new InvalidOperationException("Could not assign Unity OpenXR loader for Android.");

        FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
        EnableOpenXRFeature("com.unity.openxr.feature.metaquest");
        EnableOpenXRFeature("com.unity.openxr.feature.input.oculustouch");
        EnableOpenXRFeature("com.unity.openxr.feature.input.metaquestplus");
        EnableOpenXRFeature("com.unity.openxr.feature.input.handinteraction");
        EnableOpenXRFeature("com.unity.openxr.feature.input.handinteractionposes");
        EnableOpenXRFeature("com.unity.openxr.feature.arfoundation-meta-session");
        EnableOpenXRFeature("com.unity.openxr.feature.arfoundation-meta-camera");

        EditorUtility.SetDirty(generalSettings);
        EditorUtility.SetDirty(managerSettings);
    }

    static void EnableOpenXRFeature(string featureId)
    {
        var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, featureId);
        if (feature == null)
        {
            Debug.LogWarning("OpenXR feature was not found: " + featureId);
            return;
        }

        feature.enabled = true;
        EditorUtility.SetDirty(feature);
    }

    static XRGeneralSettingsPerBuildTarget GetOrCreateXRGeneralSettingsPerBuildTarget()
    {
        XRGeneralSettingsPerBuildTarget settings = null;
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out settings);
        if (settings != null)
            return settings;

        var guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
        if (guids.Length > 0)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if (settings != null)
            {
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
                return settings;
            }
        }

        Directory.CreateDirectory("Assets/XR/Settings");
        settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
        AssetDatabase.CreateAsset(settings, "Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset");
        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
        AssetDatabase.SaveAssets();
        return settings;
    }

    static void EnsureScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientLight = Color.white;

        var arSessionObject = new GameObject("AR Session");
        arSessionObject.AddComponent<ARSession>();

        var originObject = new GameObject("XR Origin");
        var xrOrigin = originObject.AddComponent<XROrigin>();
        xrOrigin.Origin = originObject;

        var cameraOffsetObject = new GameObject("Camera Offset");
        cameraOffsetObject.transform.SetParent(originObject.transform, false);

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(cameraOffsetObject.transform, false);

        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 1000f;
        cameraObject.AddComponent<AudioListener>();

        var poseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
        poseDriver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
        poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        cameraObject.AddComponent<ARCameraManager>();

        xrOrigin.Camera = camera;
        xrOrigin.CameraFloorOffsetObject = cameraOffsetObject;
        xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        var runtimeObject = new GameObject("SmartCart Runtime");
        runtimeObject.AddComponent<SmartCartBootstrap>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }
}

public sealed class SmartCartAndroidManifestPostprocessor : IPostGenerateGradleAndroidProject
{
    const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

    public int callbackOrder => 10000;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(manifestPath))
        {
            Debug.LogWarning("SmartCart could not find generated AndroidManifest.xml at " + manifestPath);
            return;
        }

        var document = new XmlDocument();
        document.Load(manifestPath);

        var manifest = document.DocumentElement;
        AddUsesPermission(document, manifest, "horizonos.permission.HEADSET_CAMERA");
        AddUsesPermission(document, manifest, "android.permission.CAMERA");
        AddUsesPermission(document, manifest, "android.permission.INTERNET");
        AddUsesPermission(document, manifest, "com.oculus.permission.HAND_TRACKING");
        AddUsesFeature(document, manifest, "android.hardware.camera", false);
        AddUsesFeature(document, manifest, "android.hardware.camera.any", false);
        AddUsesFeature(document, manifest, "oculus.software.handtracking", false);
        AddApplicationMetaData(document, manifest, "com.oculus.handtracking.version", "V2.0");
        AddApplicationMetaData(document, manifest, "com.oculus.handtracking.frequency", "HIGH");
        EnableCleartextHttp(document, manifest);

        document.Save(manifestPath);
    }

    static void AddUsesPermission(XmlDocument document, XmlElement manifest, string permissionName)
    {
        if (HasChildWithAndroidName(manifest, "uses-permission", permissionName))
            return;

        var element = document.CreateElement("uses-permission");
        SetAndroidAttribute(document, element, "name", permissionName);
        manifest.InsertBefore(element, manifest.FirstChild);
    }

    static void AddUsesFeature(XmlDocument document, XmlElement manifest, string featureName, bool required)
    {
        if (HasChildWithAndroidName(manifest, "uses-feature", featureName))
            return;

        var element = document.CreateElement("uses-feature");
        SetAndroidAttribute(document, element, "name", featureName);
        SetAndroidAttribute(document, element, "required", required ? "true" : "false");
        manifest.InsertBefore(element, manifest.FirstChild);
    }

    static bool HasChildWithAndroidName(XmlElement parent, string childName, string androidName)
    {
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child is not XmlElement element || element.Name != childName)
                continue;

            if (element.GetAttribute("name", AndroidNamespace) == androidName)
                return true;
        }

        return false;
    }

    static void AddApplicationMetaData(XmlDocument document, XmlElement manifest, string name, string value)
    {
        var application = manifest["application"];
        if (application == null)
            return;

        foreach (XmlNode child in application.ChildNodes)
        {
            if (child is not XmlElement element || element.Name != "meta-data")
                continue;

            if (element.GetAttribute("name", AndroidNamespace) == name)
            {
                SetAndroidAttribute(document, element, "value", value);
                return;
            }
        }

        var metaData = document.CreateElement("meta-data");
        SetAndroidAttribute(document, metaData, "name", name);
        SetAndroidAttribute(document, metaData, "value", value);
        application.AppendChild(metaData);
    }

    static void EnableCleartextHttp(XmlDocument document, XmlElement manifest)
    {
        var application = manifest["application"];
        if (application == null)
            return;

        SetAndroidAttribute(document, application, "usesCleartextTraffic", "true");
    }

    static void SetAndroidAttribute(XmlDocument document, XmlElement element, string localName, string value)
    {
        var existing = element.GetAttributeNode(localName, AndroidNamespace);
        if (existing != null)
        {
            existing.Value = value;
            return;
        }

        var attribute = document.CreateAttribute("android", localName, AndroidNamespace);
        attribute.Value = value;
        element.Attributes.Append(attribute);
    }
}
