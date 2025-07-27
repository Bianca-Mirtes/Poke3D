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
public class YOLO_ARCamera : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button captureButton;
    [SerializeField] private TextMeshProUGUI debugText;

    [SerializeField] private Material sharpenMaterial;

    [Header("AR TRacked Image")]
    [SerializeField] private ARTrackedImageManager trackedImageManager;
    private Dictionary<string, GameObject> spawnedPrefabs = new();
    private Dictionary<string, string> modelsInStandby = new();

    [Header("API")]
    [SerializeField] private string apiUrl = "https://a673b711cd26.ngrok-free.app/inference";

    [Header("AR Camera")]
    public ARCameraManager arCameraManager;

    [Header("ICR")]
    private Worker worker;
    Tensor<float> centersToCorners;

    public struct BoundingBox
    {
        public float centerX;
        public float centerY;
        public float width;
        public float height;
        public string label;
    }
    private void Start()
    {
        captureButton.onClick.AddListener(OnCaptureAndRunInference);
    }

    private void Update()
    {
        if (trackedImageManager == null) return;

        foreach (ARTrackedImage trackedImage in trackedImageManager.trackables)
        {
            if (trackedImage.trackingState == TrackingState.Tracking)
            {
                string markerName = trackedImage.referenceImage.name;
                debugText.text = $"Nome do marcardor: {markerName}";
                // Verifica se a API já identificou essa carta
                if (modelsInStandby.TryGetValue(markerName, out string modelName))
                {
                    if (!spawnedPrefabs.ContainsKey(markerName))
                    {
                        string path = $"Models/{modelName}";
                        
                        GameObject prefab = Resources.Load<GameObject>(path);
                        debugText.text = $"Carregando modelo: {path}";
                        if (prefab != null)
                        {
                            GameObject instance = Instantiate(prefab, trackedImage.transform);
                            spawnedPrefabs[markerName] = instance;
                        }
                    }
                }
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
                outputFormat = TextureFormat.RGB24,
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

            // Upscale por RenderTexture
            Texture2D upscaled = UpscaleTexture(cameraTexture, 2);

            // Salva a imagem capturada
            SaveImage(upscaled);

            StartCoroutine(SendImageToAPI(upscaled));

        }
    }

    Texture2D UpscaleTexture(Texture2D source, int upscaleFactor)
    {
        int upscaleWidth = source.width * upscaleFactor;
        int upscaleHeight = source.height * upscaleFactor;

        RenderTexture rt = new RenderTexture(upscaleWidth, upscaleHeight, 0);
        rt.filterMode = FilterMode.Bilinear;

        // Passa a textura para o shader de sharpening
        sharpenMaterial.SetFloat("_SharpenStrength", 0.4f); // ajuste entre 0 e 1

        Graphics.Blit(source, rt, sharpenMaterial);

        RenderTexture.active = rt;

        Texture2D result = new Texture2D(upscaleWidth, upscaleHeight, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, upscaleWidth, upscaleHeight), 0, 0);
        result.Apply();

        RenderTexture.active = null;
        rt.Release();

        return result;
    }

    void SaveImage(Texture2D image)
    {
        byte[] bytes = image.EncodeToPNG();

        /*string folderPath = Path.Combine(Application.dataPath, "Capturas");
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);
        string filePath = Path.Combine(folderPath, filename);

        //File.WriteAllBytes(filePath, bytes);*/

        string fileName = $"frame_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        string fullPath = Path.Combine(Application.persistentDataPath, fileName);

        //Salva no armazenamento interno da aplicação
        File.WriteAllBytes(fullPath, bytes);

        Debug.Log($"📸 Imagem salva em: {fullPath}");
    }

    IEnumerator SendImageToAPI(Texture2D image)
    {
        // Converte a imagem em JPG
        byte[] jpgBytes = image.EncodeToPNG();
        string base64Image = Convert.ToBase64String(jpgBytes);

        // Monta o payload JSON
        string jsonPayload = JsonUtility.ToJson(new ImagePayload(base64Image));
        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            #if UNITY_2020_1_OR_NEWER
                 if (request.result != UnityWebRequest.Result.Success)
            #else
                 if (request.isHttpError || request.isNetworkError)
            #endif
            {
                Debug.LogError($"Erro: {request.responseCode} - {request.downloadHandler.text}");
            }
            else
            {
                var responseJson = request.downloadHandler.text;
                InferenceResponse result = JsonUtility.FromJson<InferenceResponse>(responseJson);
                Debug.Log($"Card Set: {result.card_set}");
                Debug.Log($"Card Number: {result.card_num}");

                string filePath = Path.Combine(Application.persistentDataPath, "resultado.txt");
                File.WriteAllText(filePath, $"Card Set: {result.card_set}\nCard Number: {result.card_num}");

                // Mostrar resultado
                ShowCard(result.card_set, result.card_num);
            }
        }
    }

    public void ShowCard(string set, string num)
    {
        string prefix = set switch
        {
            "SCR" => "sv07",
            "SSP" => "sv08",
            _ => "desconhecido"
        };

        string number = new string(num.Take(3).Where(char.IsDigit).ToArray());
        string model = $"{prefix}-{number}_cropped_art_complete_textured";

        string marker = $"sv{prefix}-{number}";

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

    void OnDestroy()
    {
        centersToCorners?.Dispose();
        worker?.Dispose();
    }
}
