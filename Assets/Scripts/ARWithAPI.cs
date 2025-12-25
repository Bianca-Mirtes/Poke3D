using System.Collections.Generic;
using System.IO;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;
using UnityEngine.Networking; // necessário para o UnityWebRequest
using System.Collections;              // Necessário para IEnumerator
using System;
using System.Text; // <== ESSENCIAL para Encoding.UTF8
using System.Linq;
using TMPro;
public class ARWithAPI : MonoBehaviour
{
    [Header("User UI")]
    [SerializeField] private Button captureButton;
    [SerializeField] private TextMeshProUGUI debugText;
    [SerializeField] private TMP_InputField inputUrlAPI;
    [SerializeField] private Button updateUrlAPI;
    [SerializeField] private TextMeshProUGUI alert;

    [SerializeField] private Material sharpenMaterial;

    [Header("AR TRacked Image")]
    [SerializeField] private ARTrackedImageManager trackedImageManager;
    private Dictionary<string, GameObject> spawnedPrefabs = new();
    private Dictionary<string, string> modelsInStandby = new();
    private GameObject currentPokemon;

    [Header("API")]
    private string apiUrl;

    [Header("Profiler")]
    private string filePath;

    [Header("AR Camera")]
    public ARCameraManager arCameraManager;

    private void Start()
    {
        // fallback
        apiUrl = "https://c8ce045e3795.ngrok-free.app/inference";
        captureButton.onClick.AddListener(OnCaptureAndRunInference);
        updateUrlAPI.onClick.AddListener(UpdateURLAPI);

        filePath = Path.Combine(Application.persistentDataPath, "execution_time_api.txt");
    }
    public void UpdateURLAPI()
    {
        if (!string.IsNullOrEmpty(inputUrlAPI.text))
        {
            if(alert.gameObject.activeSelf) 
                alert.gameObject.SetActive(false);
            if (inputUrlAPI.text[inputUrlAPI.text.Length-1] == '/')
                apiUrl = inputUrlAPI.text.ToLower().Trim() + "inference";
            else
                apiUrl = inputUrlAPI.text.ToLower().Trim() + "/inference";
        }
        else
        {
            alert.text = "invalid URL!";
            alert.gameObject.SetActive(true);
        }
    }

    public GameObject GetCurentPokemon()
    {
        return currentPokemon;
    }

    private void Update()
    {
        if (trackedImageManager == null) return;

        foreach (ARTrackedImage trackedImage in trackedImageManager.trackables)
        {
            if (trackedImage.trackingState == TrackingState.Tracking)
            {
                string markerName = trackedImage.referenceImage.name;

                // Verifica se a API já identificou essa carta
                if (modelsInStandby.TryGetValue(markerName, out string modelName))
                {
                    if (!spawnedPrefabs.TryGetValue(markerName, out GameObject instance) || instance == null)
                    {
                        string path = $"Models/{modelName}";

                        GameObject prefab = Resources.Load<GameObject>(path);
                        debugText.text = $"Carregando modelo: {markerName}";
                        if (prefab != null)
                        {
                            instance = Instantiate(prefab, trackedImage.transform);
                            instance.transform.position = trackedImage.transform.position;
                            currentPokemon = instance;
                            spawnedPrefabs[markerName] = instance;
                        }
                    }

                    // Atualiza a posição e ativa o modelo
                    instance.transform.position = trackedImage.transform.position;
                    currentPokemon = instance;
                    instance.SetActive(true);
                }
            }
            else
            {
                if(spawnedPrefabs.ContainsKey(trackedImage.referenceImage.name))
                    spawnedPrefabs[trackedImage.referenceImage.name].SetActive(false);
            }
        }
    }
    public void OnCaptureAndRunInference()
    {
        ExecuteML();
    }

    public void ExecuteML()
    {
        if (arCameraManager == null || !arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
            return;

        using (cpuImage)
        {
            var nativeWidth = cpuImage.width;
            var nativeHeight = cpuImage.height;

            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, nativeWidth, nativeHeight),
                outputDimensions = new Vector2Int(nativeWidth, nativeHeight),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.MirrorX
            };

            int size = cpuImage.GetConvertedDataSize(conversionParams);
            var buffer = new NativeArray<byte>(size, Allocator.Temp);

            // Converte imagem da câmera para formato legível pela CPU
            cpuImage.Convert(conversionParams, buffer);

            // Cria textura com resolução aumentada
            var cameraTexture = new Texture2D(
                conversionParams.outputDimensions.x,
                conversionParams.outputDimensions.y,
                conversionParams.outputFormat,
                false
            );

            cameraTexture.LoadRawTextureData(buffer);
            cameraTexture.filterMode = FilterMode.Bilinear;
            cameraTexture.Apply();

            buffer.Dispose();

            // Upscale por RenderTexture para aumentar a nitidez
            Texture2D upscaled = UpscaleTexture(cameraTexture, 3);

            // Corrige rotação
            Texture2D rotated = RotateTexture(upscaled);

            // Sharpen extra via CPU (melhora brilho mais contraste)
            //Texture2D sharpened = SharpenAndContrast(rotated, 0.3f, 1.3f);
            Texture2D sharpened = UnsharpMask(rotated, 1.5f, 0.05f); // você pode ajustar

            // Salva a imagem capturada
            SaveImage(sharpened);

            float time = ExecutionTime(()=> SendImage(sharpened));
            SaveProfileFile(time);
        }
    }

     void SendImage(Texture2D image)
    {
        StartCoroutine(SendImageToAPI(image));
    }

    Texture2D UpscaleTexture(Texture2D source, int upscaleFactor)
    {
        int upscaleWidth = source.width * upscaleFactor;
        int upscaleHeight = source.height * upscaleFactor;

        RenderTexture rt = new RenderTexture(upscaleWidth, upscaleHeight, 0);
        rt.filterMode = FilterMode.Point;

        // Passa a textura para o shader de sharpening
        sharpenMaterial.SetFloat("_SharpenStrength", 0.5f); // ajuste entre 0 e 1

        Graphics.Blit(source, rt, sharpenMaterial);

        RenderTexture.active = rt;

        Texture2D result = new Texture2D(upscaleWidth, upscaleHeight, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, upscaleWidth, upscaleHeight), 0, 0);
        result.Apply();

        RenderTexture.active = null;
        rt.Release();

        return result;
    }

    Texture2D BlurTexture(Texture2D source, int blurSize)
    {
        Texture2D blurred = new Texture2D(source.width, source.height);
        Color[] pixels = source.GetPixels();

        for (int y = 0; y < source.height; y++)
        {
            for (int x = 0; x < source.width; x++)
            {
                Color avg = Color.black;
                int blurPixelCount = 0;

                for (int ky = -blurSize; ky <= blurSize; ky++)
                {
                    int py = Mathf.Clamp(y + ky, 0, source.height - 1);

                    for (int kx = -blurSize; kx <= blurSize; kx++)
                    {
                        int px = Mathf.Clamp(x + kx, 0, source.width - 1);
                        avg += source.GetPixel(px, py);
                        blurPixelCount++;
                    }
                }

                avg /= blurPixelCount;
                blurred.SetPixel(x, y, avg);
            }
        }

        blurred.Apply();
        return blurred;
    }

    Texture2D UnsharpMask(Texture2D input, float amount = 1.5f, float threshold = 0.05f)
    {
        Texture2D blurred = BlurTexture(input, 1); // blurSize = 1 é ideal para texto
        Texture2D result = new Texture2D(input.width, input.height);

        for (int y = 0; y < input.height; y++)
        {
            for (int x = 0; x < input.width; x++)
            {
                Color original = input.GetPixel(x, y);
                Color blurredPixel = blurred.GetPixel(x, y);
                Color diff = original - blurredPixel;

                // Aplica threshold para não amplificar ruído
                if (Mathf.Abs(diff.r) < threshold) diff.r = 0;
                if (Mathf.Abs(diff.g) < threshold) diff.g = 0;
                if (Mathf.Abs(diff.b) < threshold) diff.b = 0;

                Color final = original + diff * amount;
                final.a = 1;
                result.SetPixel(x, y, final);
            }
        }

        result.Apply();
        return result;
    }

    Texture2D RotateTexture(Texture2D originalTexture)
    {
        int width = originalTexture.width;
        int height = originalTexture.height;

        Texture2D rotatedTexture = new Texture2D(height, width, originalTexture.format, false);

        Color32[] originalPixels = originalTexture.GetPixels32();
        Color32[] rotatedPixels = new Color32[originalPixels.Length];

        for (int x = 0; x < width; ++x)
        {
            for (int y = 0; y < height; ++y)
            {
                rotatedPixels[y + (width - 1 - x) * height] = originalPixels[x + y * width];
            }
        }

        rotatedTexture.SetPixels32(rotatedPixels);
        rotatedTexture.Apply();
        return rotatedTexture;
    }

    void SaveImage(Texture2D image)
    {
        byte[] bytes = image.EncodeToPNG();

        string fileName = $"frame_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        string fullPath = Path.Combine(Application.persistentDataPath, fileName);

        //Salva no armazenamento interno da aplicação
        File.WriteAllBytes(fullPath, bytes);
    }

    private void SaveProfileFile(float time)
    {
        string content = $"Execution Time - (API Method): {time}ms";
        File.WriteAllText(filePath, content);
    }

    private float ExecutionTime(Action method)
    {
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        sw.Start();
        method.Invoke();
        sw.Stop();

        return sw.Elapsed.Milliseconds;
    }

    IEnumerator SendImageToAPI(Texture2D image)
    {
        // Converte a imagem em JPG
        byte[] jpgBytes = image.EncodeToPNG();
        string base64Image = Convert.ToBase64String(jpgBytes);

        // Monta o payload JSON
        string jsonPayload = JsonUtility.ToJson(new ImagePayload(base64Image));

        debugText.text = "Payload Montado!";
        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            debugText.text = "Result request: " + request.result;
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Erro: {request.responseCode} - {request.downloadHandler.text}");
            }
            else
            {
                var responseJson = request.downloadHandler.text;
                InferenceResponse result = JsonUtility.FromJson<InferenceResponse>(responseJson);
                Debug.Log($"Card Set: {result.card_set}");
                Debug.Log($"Card Number: {result.card_num}");

                debugText.text = $"Card Set: {result.card_set} | Card Number: {result.card_num}";

                string filePath = Path.Combine(Application.persistentDataPath, "resultado.txt");
                File.WriteAllText(filePath, $"Card Set: {result.card_set}\nCard Number: {result.card_num}");

                // Mostrar resultado
                ShowCard(result.card_set, result.card_num);
            }
        }
    }

    public void ShowCard(string set, string num)
    {
        string newSet = new string(set.Take(3).ToArray());

        string prefix = newSet switch
        {
            "SCR" => "sv07",
            "SSP" => "sv08",
            _ => "desconhecido"
        };

        string number = new string(num.Take(3).Where(char.IsDigit).ToArray());
        string model = $"{prefix}-{number}_cropped_art_complete_textured";

        string marker = $"{prefix}-{number}";

        // Armazena esse modelo como pendente para o marcador detectado
        modelsInStandby[marker] = model;
    }

    [Serializable]
    public class ImagePayload
    {
        public string image_base64;
        public ImagePayload(string base64) => image_base64 = base64;
    }

    [Serializable]
    public class InferenceResponse
    {
        public string card_set;
        public string card_num;
    }
}
