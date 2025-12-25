using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;


public class RunYolo : MonoBehaviour
{
    [Header("YOLOv8")]
    [SerializeField] private ModelAsset yoloModelAsset;
    private Model runtimeModel;
    private Worker worker;
    private Tensor<float> inputTensor;
    private Tensor outputTensor;
    private float confidenceThreshold = 0.5f;
    private float iouThreshold = 0.5f;

    [Header("AR Camera")]
    public ARCameraManager arCameraManager;

    [Header(" User UI")]
    [SerializeField] private Button captureButton;
    [SerializeField] private TextMeshProUGUI debugText;

    [SerializeField] private Material sharpenMaterial;
    
    [Header("Profiler")]
    private string filePath;

    public Texture2D imageEx;

    [Header("AR TRacked Image")]
    [SerializeField] private ARTrackedImageManager trackedImageManager;
    private Dictionary<string, GameObject> spawnedPrefabs = new();
    private Dictionary<string, string> modelsInStandby = new();
    private GameObject currentPokemon;

    [Header("OCR")]
    private TesseractDriver _tesseractDriver;
    [SerializeField] private Texture2D[] card_set;
    [SerializeField] private Texture2D[] card_num;

    private void Start()
    {
        runtimeModel = ModelLoader.Load(yoloModelAsset);
        worker = new Worker(runtimeModel, BackendType.GPUCompute);
        filePath = Path.Combine(Application.persistentDataPath, "execution_time_yolo.txt");
        _tesseractDriver = new TesseractDriver();

        captureButton.onClick.AddListener(OnCaptureAndRunInference);
    }
    public void OnCaptureAndRunInference()
    {
        //ExecuteML();
        ExecuteExemplo();
    }

    private void RunOCR(Texture2D card_set, Texture2D card_num)
    {
        string text1 = _tesseractDriver.Recognize(card_set);
        string text2 = _tesseractDriver.Recognize(card_num);

        debugText.text = $"card_set: {text1} | card_num: {text2}";
    }

    public void ExecuteExemplo()
    {
        DetectCard(imageEx);
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
            Texture2D sharpened = UnsharpMask(rotated, 1.5f, 0.05f);

            float timeYolo = ExecutionTime(() => DetectCard(sharpened));

            int value1 = UnityEngine.Random.Range(0, card_set.Count());

            _tesseractDriver.CheckTessVersion();

            float timeOCR = ExecutionTime(() => _tesseractDriver.Setup(() => RunOCR(card_set[value1], card_num[value1])));
            SaveProfileFile(timeYolo, timeOCR);
        }
    }

    private void SaveProfileFile(float timeYolo, float timeOCR)
    {
        string content = $"Execution Time - (YOLO): {timeYolo}ms \n Execution Time - (OCR): {timeOCR}ms";
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
    void DetectCard(Texture2D image)
    {
        // 1️ Pré-processar a imagem para tensor NCHW (1, 3, 640, 640)
        var resized = ResizeTexture(image, 640, 640);

        //SaveImage(resized);

        inputTensor = new Tensor<float>(new TensorShape(1, 3, resized.height, resized.width));
        TextureConverter.ToTensor(resized, inputTensor);

        // Executa o modelo
        worker.Schedule(inputTensor);
        worker.CopyOutput("output0", ref outputTensor);

        Tensor<float> output = outputTensor.ReadbackAndClone() as Tensor<float>;

        // Parse → aplica threshold
        var detections = ParseYoloOutput(output, 0.5f);

        // Aplica NMS
        var final = Nms(detections, 0.45f);

        if (final.Count > 0)
        {
            // pega o melhor resultado (ID da carta)
            var best = final[0];

            Debug.Log(best.rect);
            // Saída do modelo já vem como [x1,y1,x2,y2]
            float x1 = best.rect.x;
            float y1 = best.rect.y;
            float x2 = best.rect.xMax;
            float y2 = best.rect.yMax;

            // Escala para a textura original
            float scaleX = (float)image.width / 640f;
            float scaleY = (float)image.height / 640f;

            Texture2D fullFrame = MakeReadable(image);

            int x = Mathf.Clamp((int)(x1 * scaleX), 0, image.width - 1);
            int y = Mathf.Clamp((int)(y1 * scaleY), 0, image.height - 1);
            int w = Mathf.Clamp((int)((x2 - x1) * scaleX), 1, image.width - x);
            int h = Mathf.Clamp((int)((y2 - y1) * scaleY), 1, image.height - y);

            Texture2D cropped = new Texture2D(w, h);
            cropped.SetPixels(fullFrame.GetPixels(x, y, w, h));
            cropped.Apply();
            SaveImage(cropped);
        }

        _tesseractDriver.CheckTessVersion();
        int value1 = UnityEngine.Random.Range(0, card_set.Count());

        Texture2D upscaled1 = UpscaleTexture(card_set[value1], 3);
        Texture2D upscaled2 = UpscaleTexture(card_num[value1], 3);

        Texture2D sharpened1 = UnsharpMask(upscaled1, 1.5f, 0.05f);
        Texture2D sharpened2 = UnsharpMask(upscaled2, 1.5f, 0.05f);

        Texture2D set = MakeReadable(sharpened1);
        Texture2D number = MakeReadable(sharpened2);
        float timeOCR = ExecutionTime(() => _tesseractDriver.Setup(() => RunOCR(set, number)));

        output.Dispose();
    }

    public class Detection
    {
        public Rect rect;
        public float score;
        public int classId;
    }

    public List<Detection> ParseYoloOutput(Tensor<float> output, float scoreThreshold = 0.5f, int inputSize = 640)
    {
        var detections = new List<Detection>();

        // output shape: (1,6,8400)
        int numPreds = output.shape[2];

        for (int i = 0; i < numPreds; i++)
        {
            float conf = output[0, 4, i];
            if (conf < scoreThreshold) continue;

            float cx = output[0, 0, i];
            float cy = output[0, 1, i];
            float w = output[0, 2, i];
            float h = output[0, 3, i];
            int cls = (int)output[0, 5, i];

            float x1 = cx - w / 2f;
            float y1 = cy - h / 2f;

            Rect box = new Rect(x1, y1, w, h);

            detections.Add(new Detection { rect = box, score = conf, classId = cls });
        }

        return detections;
    }

    public List<Detection> Nms(List<Detection> detections, float iouThreshold = 0.45f)
    {
        // Ordena pela confiança (maior primeiro)
        detections.Sort((a, b) => b.score.CompareTo(a.score));
        var results = new List<Detection>();

        while (detections.Count > 0)
        {
            var best = detections[0];
            results.Add(best);
            detections.RemoveAt(0);

            detections.RemoveAll(det => IoU(best.rect, det.rect) > iouThreshold);
        }

        return results;
    }

    static float IoU(Rect a, Rect b)
    {
        float areaA = a.width * a.height;
        float areaB = b.width * b.height;

        float minX = Mathf.Max(a.xMin, b.xMin);
        float minY = Mathf.Max(a.yMin, b.yMin);
        float maxX = Mathf.Min(a.xMax, b.xMax);
        float maxY = Mathf.Min(a.yMax, b.yMax);

        float interW = Mathf.Max(0, maxX - minX);
        float interH = Mathf.Max(0, maxY - minY);
        float interArea = interW * interH;

        return interArea / (areaA + areaB - interArea);
    }

    void SaveImage(Texture2D image)
    {
        byte[] bytes = image.EncodeToPNG();

        string fileName = $"frame_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        string fullPath = Path.Combine(Application.persistentDataPath, fileName);

        //Salva no armazenamento interno da aplicação
        File.WriteAllBytes(fullPath, bytes);
    }

    Texture2D ResizeTexture(Texture2D src, int newW, int newH)
    {
        RenderTexture rt = RenderTexture.GetTemporary(newW, newH, 0, RenderTextureFormat.ARGB32);
        rt.filterMode = FilterMode.Bilinear;

        RenderTexture.active = rt; Graphics.Blit(src, rt); 
        Texture2D result = new Texture2D(newW, newH, TextureFormat.RGBA32, false);
        
        result.ReadPixels(new Rect(0, 0, newW, newH), 0, 0);
        result.Apply(); 

        RenderTexture.ReleaseTemporary(rt);
        RenderTexture.active = null;

        return result;
    }

    Texture2D MakeReadable(Texture2D source)
    {
        RenderTexture tmp = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, tmp);

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = tmp;

        Texture2D readableTex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        readableTex.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
        readableTex.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(tmp);

        return readableTex;
    }

    #region Tratamento da Imagem
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
    #endregion

    #region AR Tracked Image
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
                if (spawnedPrefabs.ContainsKey(trackedImage.referenceImage.name))
                    spawnedPrefabs[trackedImage.referenceImage.name].SetActive(false);
            }
        }
    }
    #endregion
}
