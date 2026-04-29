using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

[DisallowMultipleComponent]
public sealed class SmartCartBootstrap : MonoBehaviour
{
    static bool runtimeCreated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void EnsureRuntimeExists()
    {
        if (runtimeCreated)
            return;

        if (FindObjectOfType<SmartCartBootstrap>() != null)
            return;

        Debug.Log("SmartCart bootstrap was not present in the scene; creating runtime object.");
        var runtimeObject = new GameObject("SmartCart Runtime");
        DontDestroyOnLoad(runtimeObject);
        runtimeObject.AddComponent<SmartCartBootstrap>();
    }

    void Awake()
    {
        if (runtimeCreated)
        {
            Destroy(gameObject);
            return;
        }

        runtimeCreated = true;
        DontDestroyOnLoad(gameObject);
        Debug.Log("SmartCart bootstrap awake.");

        var xrCamera = Camera.main;
        if (xrCamera == null)
            xrCamera = CreateFallbackCamera();

        ConfigurePassthroughCamera(xrCamera);

        var panel = SmartCartPanel.Create(xrCamera.transform);
        var captureBox = SmartCartCaptureBox.Create(xrCamera.transform);
        var scanner = gameObject.AddComponent<QuestLabelScanner>();
        scanner.Initialize(panel, captureBox);
    }

    static Camera CreateFallbackCamera()
    {
        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<AudioListener>();
        return camera;
    }

    static void ConfigurePassthroughCamera(Camera xrCamera)
    {
        xrCamera.clearFlags = CameraClearFlags.SolidColor;
        xrCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        xrCamera.nearClipPlane = 0.01f;
        xrCamera.farClipPlane = 1000f;

        if (xrCamera.GetComponent<ARCameraManager>() == null)
            xrCamera.gameObject.AddComponent<ARCameraManager>();
    }
}

public sealed class QuestLabelScanner : MonoBehaviour
{
    const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";
    const float CaptureIntervalSeconds = 2.75f;
    const int LabelJpegQuality = 78;
    const string DefaultServerUrl = "http://192.168.137.1:8000";

    SmartCartPanel panel;
    SmartCartCaptureBox captureBox;
    SmartCartServerClient serverClient;
    WebCamTexture cameraTexture;
    Texture2D labelCropTexture;
    Color32[] cropPixels;
    Coroutine labelAnalysisRoutine;
    Coroutine flashRoutine;
    float nextCaptureTime;
    float lastServerErrorTime;
    float lastWaitingFrameLogTime;
    bool scannerReady;

    public void Initialize(SmartCartPanel targetPanel, SmartCartCaptureBox targetCaptureBox)
    {
        panel = targetPanel;
        captureBox = targetCaptureBox;
        serverClient = new SmartCartServerClient(PlayerPrefs.GetString("SmartCartServerUrl", DefaultServerUrl));

        panel.ShowStatus("Starting camera", "Endpoint: " + serverClient.BaseUrl);
        captureBox.SetIdle();
        Debug.Log("SmartCart label scanner initializing. Endpoint: " + serverClient.BaseUrl);
        StartCoroutine(StartCameraRoutine());
    }

    IEnumerator StartCameraRoutine()
    {
        yield return EnsureCameraPermission();

        if (!HasCameraPermission())
        {
            Debug.LogWarning("SmartCart camera permission is blocked.");
            panel.ShowError("Camera permission blocked", "Enable Headset Camera permission for SmartCart.");
            yield break;
        }

        var devices = WebCamTexture.devices;
        Debug.Log("SmartCart camera device count: " + (devices == null ? 0 : devices.Length));
        if (devices == null || devices.Length == 0)
        {
            panel.ShowError("No headset camera found", "Quest passthrough camera access requires Quest 3/3S on recent Horizon OS.");
            yield break;
        }

        var selectedDevice = SelectCamera(devices);
        var requestedResolution = ChooseResolution(selectedDevice);
        Debug.Log("SmartCart selected camera: " + selectedDevice.name + " requested " + requestedResolution.x + "x" + requestedResolution.y);
        cameraTexture = new WebCamTexture(selectedDevice.name, requestedResolution.x, requestedResolution.y, 30);
        cameraTexture.Play();

        var timeoutAt = Time.realtimeSinceStartup + 8f;
        while (cameraTexture.width <= 16 && Time.realtimeSinceStartup < timeoutAt)
            yield return null;

        if (cameraTexture.width <= 16)
        {
            Debug.LogWarning("SmartCart camera did not start before timeout.");
            panel.ShowError("Camera did not start", "Restart SmartCart after confirming the headset camera permission.");
            yield break;
        }

        scannerReady = true;
        Debug.Log("SmartCart camera started: " + cameraTexture.width + "x" + cameraTexture.height + " rotation=" + cameraTexture.videoRotationAngle + " mirrored=" + cameraTexture.videoVerticallyMirrored);
        panel.ShowReady(cameraTexture.width, cameraTexture.height, serverClient.BaseUrl);
    }

    IEnumerator EnsureCameraPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (HasCameraPermission())
            yield break;

        Permission.RequestUserPermission(HeadsetCameraPermission);
        yield return WaitForPermission(HeadsetCameraPermission, 10f);

        if (HasCameraPermission())
            yield break;

        Permission.RequestUserPermission(Permission.Camera);
        yield return WaitForPermission(Permission.Camera, 10f);
#else
        yield break;
#endif
    }

    IEnumerator WaitForPermission(string permission, float timeoutSeconds)
    {
        var timeoutAt = Time.realtimeSinceStartup + timeoutSeconds;
        while (!Permission.HasUserAuthorizedPermission(permission) && Time.realtimeSinceStartup < timeoutAt)
            yield return null;
    }

    bool HasCameraPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Permission.HasUserAuthorizedPermission(HeadsetCameraPermission) ||
               Permission.HasUserAuthorizedPermission(Permission.Camera);
#else
        return true;
#endif
    }

    static WebCamDevice SelectCamera(WebCamDevice[] devices)
    {
        foreach (var device in devices)
        {
            if (!device.isFrontFacing)
                return device;
        }

        return devices[0];
    }

    static Vector2Int ChooseResolution(WebCamDevice device)
    {
        var resolutions = device.availableResolutions;
        if (resolutions == null || resolutions.Length == 0)
            return new Vector2Int(1280, 960);

        Resolution? best = null;
        foreach (var resolution in resolutions)
        {
            var aspect = (float)resolution.width / resolution.height;
            var isFourByThree = Mathf.Abs(aspect - (4f / 3f)) < 0.08f;
            var withinTarget = resolution.width <= 1280 && resolution.height <= 960;
            if (!isFourByThree || !withinTarget)
                continue;

            if (!best.HasValue || resolution.width * resolution.height > best.Value.width * best.Value.height)
                best = resolution;
        }

        if (best.HasValue)
            return new Vector2Int(best.Value.width, best.Value.height);

        return new Vector2Int(resolutions[0].width, resolutions[0].height);
    }

    void Update()
    {
        if (!scannerReady || cameraTexture == null)
            return;

        if (!cameraTexture.didUpdateThisFrame)
        {
            if (Time.unscaledTime - lastWaitingFrameLogTime > 5f)
            {
                lastWaitingFrameLogTime = Time.unscaledTime;
                Debug.Log("SmartCart waiting for a fresh camera frame. playing=" + cameraTexture.isPlaying + " size=" + cameraTexture.width + "x" + cameraTexture.height);
            }
            return;
        }

        if (labelAnalysisRoutine != null || Time.unscaledTime < nextCaptureTime)
            return;

        nextCaptureTime = Time.unscaledTime + CaptureIntervalSeconds;

        byte[] jpegBytes;
        try
        {
            jpegBytes = CaptureLabelCrop(cameraTexture.GetPixels32());
        }
        catch (Exception ex)
        {
            Debug.LogWarning("SmartCart label crop failed: " + ex.Message);
            return;
        }

        captureBox.SetSending();
        panel.ShowSending();
        Debug.Log("SmartCart sending label crop to laptop. bytes=" + jpegBytes.Length + " endpoint=" + serverClient.BaseUrl);
        labelAnalysisRoutine = StartCoroutine(serverClient.AnalyzeLabel(
            jpegBytes,
            response =>
            {
                labelAnalysisRoutine = null;
                Debug.Log("SmartCart label response received: " + (response.summary ?? "<no summary>"));
                panel.ShowLabelAnalysis(response);
                StartSuccessFlash();
            },
            error =>
            {
                labelAnalysisRoutine = null;
                captureBox.SetIdle();
                if (Time.unscaledTime - lastServerErrorTime > 8f)
                {
                    lastServerErrorTime = Time.unscaledTime;
                    Debug.LogWarning("SmartCart laptop AI unavailable: " + error);
                    panel.ShowServerOffline(serverClient.BaseUrl, error);
                }
            }));
    }

    byte[] CaptureLabelCrop(Color32[] sourcePixels)
    {
        var crop = LabelCropRect(cameraTexture.width, cameraTexture.height);
        var requiredLength = crop.width * crop.height;
        if (cropPixels == null || cropPixels.Length != requiredLength)
            cropPixels = new Color32[requiredLength];

        for (var row = 0; row < crop.height; row++)
        {
            var sourceOffset = (crop.y + row) * cameraTexture.width + crop.x;
            var targetOffset = row * crop.width;
            Array.Copy(sourcePixels, sourceOffset, cropPixels, targetOffset, crop.width);
        }

        if (labelCropTexture == null ||
            labelCropTexture.width != crop.width ||
            labelCropTexture.height != crop.height)
        {
            if (labelCropTexture != null)
                Destroy(labelCropTexture);

            labelCropTexture = new Texture2D(crop.width, crop.height, TextureFormat.RGBA32, false);
        }

        labelCropTexture.SetPixels32(cropPixels);
        labelCropTexture.Apply(false);
        return labelCropTexture.EncodeToJPG(LabelJpegQuality);
    }

    static RectInt LabelCropRect(int width, int height)
    {
        var cropWidth = Mathf.RoundToInt(width * 0.42f);
        var cropHeight = Mathf.RoundToInt(height * 0.58f);
        var x = Mathf.RoundToInt(width * 0.55f);
        var y = Mathf.RoundToInt((height - cropHeight) * 0.5f);

        x = Mathf.Clamp(x, 0, width - 2);
        y = Mathf.Clamp(y, 0, height - 2);
        cropWidth = Mathf.Clamp(cropWidth, 2, width - x);
        cropHeight = Mathf.Clamp(cropHeight, 2, height - y);
        return new RectInt(x, y, cropWidth, cropHeight);
    }

    void StartSuccessFlash()
    {
        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        flashRoutine = StartCoroutine(SuccessFlashRoutine());
    }

    IEnumerator SuccessFlashRoutine()
    {
        captureBox.SetSuccess();
        yield return new WaitForSecondsRealtime(1f);
        captureBox.SetIdle();
        flashRoutine = null;
    }

    void OnDestroy()
    {
        if (cameraTexture != null && cameraTexture.isPlaying)
            cameraTexture.Stop();

        if (cameraTexture != null)
            Destroy(cameraTexture);

        if (labelCropTexture != null)
            Destroy(labelCropTexture);
    }
}

[Serializable]
public sealed class LabelAnalysisResponse
{
    public bool ok;
    public string mode;
    public ServerProductLabel product;
    public string summary;
    public string[] warnings;
}

[Serializable]
public sealed class ServerProductLabel
{
    public string name;
    public string brand;
    public string quantity;
    public string[] ingredients;
    public string[] allergens;
    public string[] claims;
    public ServerNutritionFacts nutrition;
    public float confidence;
    public string source;
}

[Serializable]
public sealed class ServerNutritionFacts
{
    public string serving_size;
    public float calories;
    public string calories_unit;
    public float fat_g;
    public float saturated_fat_g;
    public float carbohydrates_g;
    public float sugars_g;
    public float fiber_g;
    public float protein_g;
    public float sodium_mg;
}

public sealed class SmartCartServerClient
{
    readonly List<string> endpoints = new List<string>();
    int activeEndpointIndex;

    public SmartCartServerClient(string baseUrl)
    {
        AddEndpoint(string.IsNullOrWhiteSpace(baseUrl) ? "http://192.168.137.1:8000" : baseUrl);
        AddEndpoint("http://192.168.137.1:8000");
        AddEndpoint("http://127.0.0.1:8000");
    }

    public string BaseUrl => endpoints[activeEndpointIndex];

    public IEnumerator AnalyzeLabel(byte[] jpegBytes, Action<LabelAnalysisResponse> completed, Action<string> failed)
    {
        string lastError = null;

        for (var attempt = 0; attempt < endpoints.Count; attempt++)
        {
            var endpointIndex = (activeEndpointIndex + attempt) % endpoints.Count;
            var endpoint = endpoints[endpointIndex];
            var form = new WWWForm();
            form.AddBinaryData("image", jpegBytes, "smartcart-label-roi.jpg", "image/jpeg");

            using (var request = UnityWebRequest.Post(endpoint + "/analyze-label", form))
            {
                request.timeout = 25;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    lastError = endpoint + " -> " + request.error;
                    continue;
                }

                LabelAnalysisResponse response;
                try
                {
                    response = JsonUtility.FromJson<LabelAnalysisResponse>(request.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    lastError = endpoint + " -> could not parse AI response: " + ex.Message;
                    continue;
                }

                if (response == null || response.product == null)
                {
                    lastError = endpoint + " -> AI response did not include product data.";
                    continue;
                }

                activeEndpointIndex = endpointIndex;
                completed(response);
                yield break;
            }
        }

        failed(lastError ?? "No SmartCart AI endpoints are configured.");
    }

    void AddEndpoint(string value)
    {
        var endpoint = value.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(endpoint))
            return;

        foreach (var existing in endpoints)
        {
            if (string.Equals(existing, endpoint, StringComparison.OrdinalIgnoreCase))
                return;
        }

        endpoints.Add(endpoint);
    }
}

public sealed class SmartCartPanel
{
    Text statusText;
    Text productText;
    Text detailText;
    Text hintText;

    public static SmartCartPanel Create(Transform cameraTransform)
    {
        var panelObject = new GameObject("SmartCart Label Panel");
        panelObject.transform.SetParent(cameraTransform, false);
        panelObject.transform.localPosition = new Vector3(-0.42f, 0.27f, 1.16f);
        panelObject.transform.localRotation = Quaternion.identity;
        panelObject.transform.localScale = Vector3.one * 0.001f;

        var rect = panelObject.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(520f, 300f);

        var canvas = panelObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cameraTransform.GetComponent<Camera>();
        canvas.sortingOrder = 10;

        var background = panelObject.AddComponent<SmartCartRoundedRect>();
        background.CornerRadius = 30f;
        background.BorderThickness = 3f;
        background.FillColor = new Color(0.03f, 0.07f, 0.08f, 0.58f);
        background.BorderColor = new Color(0.13f, 0.86f, 0.72f, 0.45f);
        background.raycastTarget = false;

        var panel = new SmartCartPanel
        {
            statusText = CreateText(rect, "Status", "SmartCart", 24, FontStyle.Bold, new Vector2(22f, -20f), new Vector2(476f, 34f), TextAnchor.UpperLeft),
            productText = CreateText(rect, "Product", "", 22, FontStyle.Bold, new Vector2(22f, -64f), new Vector2(476f, 76f), TextAnchor.UpperLeft),
            detailText = CreateText(rect, "Details", "", 18, FontStyle.Normal, new Vector2(22f, -146f), new Vector2(476f, 96f), TextAnchor.UpperLeft),
            hintText = CreateText(rect, "Hint", "", 15, FontStyle.Italic, new Vector2(22f, -252f), new Vector2(476f, 34f), TextAnchor.UpperLeft)
        };

        panel.ShowStatus("SmartCart", "Point the right box at a label.");
        return panel;
    }

    static Text CreateText(RectTransform parent, string name, string text, int size, FontStyle style, Vector2 position, Vector2 sizeDelta, TextAnchor alignment)
    {
        var textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);

        var rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = sizeDelta;

        var uiText = textObject.AddComponent<Text>();
        uiText.font = GetBuiltinFont();
        uiText.text = text;
        uiText.fontSize = size;
        uiText.fontStyle = style;
        uiText.alignment = alignment;
        uiText.horizontalOverflow = HorizontalWrapMode.Wrap;
        uiText.verticalOverflow = VerticalWrapMode.Truncate;
        uiText.color = new Color(0.92f, 0.98f, 0.96f, 1f);
        uiText.raycastTarget = false;
        return uiText;
    }

    static Font GetBuiltinFont()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

    public void ShowStatus(string title, string hint)
    {
        statusText.text = title;
        productText.text = "Label scan mode";
        detailText.text = "Waiting for headset camera.";
        hintText.text = hint;
    }

    public void ShowReady(int width, int height, string serverUrl)
    {
        statusText.text = "Ready";
        productText.text = "Label capture active";
        detailText.text = "Camera " + width + " x " + height + "\nServer " + serverUrl;
        hintText.text = "Place the label inside the right box.";
    }

    public void ShowSending()
    {
        statusText.text = "Reading label";
        hintText.text = "Sending cropped label to laptop.";
    }

    public void ShowLabelAnalysis(LabelAnalysisResponse response)
    {
        var product = response.product;
        var nutrition = product.nutrition ?? new ServerNutritionFacts();

        statusText.text = "Label received";
        productText.text = FirstNonEmpty(product.name, "Unknown product");

        var details = new List<string>();
        details.Add(FirstNonEmpty(product.brand, "Brand not visible"));

        if (!string.IsNullOrWhiteSpace(product.quantity))
            details.Add(product.quantity);

        var nutritionLine = FormatNutrition(nutrition);
        if (!string.IsNullOrWhiteSpace(nutritionLine))
            details.Add(nutritionLine);

        if (product.allergens != null && product.allergens.Length > 0)
            details.Add("Allergens: " + string.Join(", ", product.allergens));
        else if (product.ingredients != null && product.ingredients.Length > 0)
            details.Add("Ingredients: " + string.Join(", ", product.ingredients));

        detailText.text = string.Join("\n", details);
        hintText.text = FirstNonEmpty(response.summary, "Source: " + product.source);
    }

    public void ShowServerOffline(string serverUrl, string error)
    {
        statusText.text = "Laptop offline";
        productText.text = "No server response";
        detailText.text = "Server " + serverUrl;
        hintText.text = error;
    }

    public void ShowError(string title, string details)
    {
        statusText.text = title;
        productText.text = "Label scan paused";
        detailText.text = details;
        hintText.text = "SmartCart is still running.";
    }

    static string FormatNutrition(ServerNutritionFacts nutrition)
    {
        var parts = new List<string>();
        if (nutrition.calories > 0f)
            parts.Add(nutrition.calories.ToString("0.#") + " kcal");
        if (nutrition.sugars_g > 0f)
            parts.Add(nutrition.sugars_g.ToString("0.#") + "g sugar");
        if (nutrition.protein_g > 0f)
            parts.Add(nutrition.protein_g.ToString("0.#") + "g protein");
        if (nutrition.sodium_mg > 0f)
            parts.Add(nutrition.sodium_mg.ToString("0.#") + "mg sodium");
        return string.Join(" | ", parts);
    }

    static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }
}

public sealed class SmartCartCaptureBox
{
    readonly SmartCartRoundedRect border;

    SmartCartCaptureBox(SmartCartRoundedRect targetBorder)
    {
        border = targetBorder;
    }

    public static SmartCartCaptureBox Create(Transform cameraTransform)
    {
        var boxObject = new GameObject("SmartCart Label Capture Box");
        boxObject.transform.SetParent(cameraTransform, false);
        boxObject.transform.localPosition = new Vector3(0.43f, 0.01f, 1.12f);
        boxObject.transform.localRotation = Quaternion.identity;
        boxObject.transform.localScale = Vector3.one * 0.00105f;

        var rect = boxObject.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(360f, 430f);

        var canvas = boxObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cameraTransform.GetComponent<Camera>();
        canvas.sortingOrder = 11;

        var border = boxObject.AddComponent<SmartCartRoundedRect>();
        border.CornerRadius = 34f;
        border.BorderThickness = 7f;
        border.FillColor = new Color(0f, 0f, 0f, 0.02f);
        border.BorderColor = new Color(1f, 1f, 1f, 0.92f);
        border.raycastTarget = false;

        return new SmartCartCaptureBox(border);
    }

    public void SetIdle()
    {
        border.BorderColor = new Color(1f, 1f, 1f, 0.92f);
        border.FillColor = new Color(0f, 0f, 0f, 0.02f);
        border.SetVerticesDirty();
    }

    public void SetSending()
    {
        border.BorderColor = new Color(0.58f, 0.82f, 1f, 0.94f);
        border.FillColor = new Color(0.1f, 0.2f, 0.28f, 0.05f);
        border.SetVerticesDirty();
    }

    public void SetSuccess()
    {
        border.BorderColor = new Color(0.18f, 1f, 0.47f, 0.98f);
        border.FillColor = new Color(0.02f, 0.35f, 0.13f, 0.08f);
        border.SetVerticesDirty();
    }
}

public sealed class SmartCartRoundedRect : MaskableGraphic
{
    [Range(0f, 100f)]
    public float CornerRadius = 24f;

    [Range(0f, 40f)]
    public float BorderThickness = 0f;

    public Color FillColor = Color.white;
    public Color BorderColor = Color.clear;
    public int Segments = 8;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        var radius = Mathf.Clamp(CornerRadius, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
        if (FillColor.a > 0f)
            AddFilledRoundedRect(vh, rect, radius, Tint(FillColor));

        if (BorderThickness > 0f && BorderColor.a > 0f)
            AddRoundedBorder(vh, rect, radius, Mathf.Min(BorderThickness, Mathf.Min(rect.width, rect.height) * 0.5f), Tint(BorderColor));
    }

    Color Tint(Color source)
    {
        return new Color(source.r * color.r, source.g * color.g, source.b * color.b, source.a * color.a);
    }

    static void AddFilledRoundedRect(VertexHelper vh, Rect rect, float radius, Color color)
    {
        var points = BuildRoundedPoints(rect, radius, 8);
        var vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = rect.center;
        var centerIndex = vh.currentVertCount;
        vh.AddVert(vertex);

        var firstIndex = vh.currentVertCount;
        foreach (var point in points)
        {
            vertex.position = point;
            vh.AddVert(vertex);
        }

        for (var i = 0; i < points.Count; i++)
            vh.AddTriangle(centerIndex, firstIndex + i, firstIndex + ((i + 1) % points.Count));
    }

    static void AddRoundedBorder(VertexHelper vh, Rect rect, float radius, float thickness, Color color)
    {
        var innerRect = new Rect(rect.xMin + thickness, rect.yMin + thickness, rect.width - thickness * 2f, rect.height - thickness * 2f);
        if (innerRect.width <= 0f || innerRect.height <= 0f)
            return;

        var outerPoints = BuildRoundedPoints(rect, radius, 8);
        var innerPoints = BuildRoundedPoints(innerRect, Mathf.Max(0f, radius - thickness), 8);

        var vertex = UIVertex.simpleVert;
        vertex.color = color;
        var firstOuter = vh.currentVertCount;
        foreach (var point in outerPoints)
        {
            vertex.position = point;
            vh.AddVert(vertex);
        }

        var firstInner = vh.currentVertCount;
        foreach (var point in innerPoints)
        {
            vertex.position = point;
            vh.AddVert(vertex);
        }

        for (var i = 0; i < outerPoints.Count; i++)
        {
            var next = (i + 1) % outerPoints.Count;
            vh.AddTriangle(firstOuter + i, firstOuter + next, firstInner + next);
            vh.AddTriangle(firstOuter + i, firstInner + next, firstInner + i);
        }
    }

    static List<Vector2> BuildRoundedPoints(Rect rect, float radius, int segments)
    {
        var points = new List<Vector2>();
        if (radius <= 0.01f)
        {
            points.Add(new Vector2(rect.xMax, rect.yMin));
            points.Add(new Vector2(rect.xMax, rect.yMax));
            points.Add(new Vector2(rect.xMin, rect.yMax));
            points.Add(new Vector2(rect.xMin, rect.yMin));
            return points;
        }

        AddArc(points, new Vector2(rect.xMax - radius, rect.yMin + radius), radius, -90f, 0f, segments);
        AddArc(points, new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 0f, 90f, segments);
        AddArc(points, new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 90f, 180f, segments);
        AddArc(points, new Vector2(rect.xMin + radius, rect.yMin + radius), radius, 180f, 270f, segments);
        return points;
    }

    static void AddArc(List<Vector2> points, Vector2 center, float radius, float startDegrees, float endDegrees, int segments)
    {
        var count = Mathf.Max(2, segments);
        for (var i = 0; i <= count; i++)
        {
            var t = (float)i / count;
            var radians = Mathf.Lerp(startDegrees, endDegrees, t) * Mathf.Deg2Rad;
            points.Add(new Vector2(center.x + Mathf.Cos(radians) * radius, center.y + Mathf.Sin(radians) * radius));
        }
    }
}
