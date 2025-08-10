using UnityEngine;
using UnityEngine.UI;

public class SizeController : MonoBehaviour
{
    [SerializeField] private Slider sizeSlider;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        sizeSlider.onValueChanged.AddListener(SetSize);
    }

    public void SetSize(float value)
    {
        Transform obj = FindFirstObjectByType<ARWithAPI>().GetCurentPokemon().transform.GetChild(0);
        if (obj != null)
            obj.localScale = new Vector3 (value, value, value);
    }
}
