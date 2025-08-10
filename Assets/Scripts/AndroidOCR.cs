using UnityEngine;
using System;
using System.Collections;

public class AndroidOCR : MonoBehaviour
{
    public void RunOCR(Texture2D image)
    {
        byte[] imageBytes = image.EncodeToPNG(); // ou JPG
        string base64Image = Convert.ToBase64String(imageBytes);

/*#if UNITY_ANDROID && !UNITY_EDITOR
        using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        using (AndroidJavaClass pluginClass = new AndroidJavaClass("com.seuapp.mlkitocr.YourWrapperClass"))
        {
            pluginClass.CallStatic("runOCR", activity, base64Image);
        }
#endif*/
    }

    public void OnOCRSuccess(string result)
    {
        Debug.Log("OCR result: " + result);
    }

    public void OnOCRError(string error)
    {
        Debug.LogError("OCR failed: " + error);
    }
}
