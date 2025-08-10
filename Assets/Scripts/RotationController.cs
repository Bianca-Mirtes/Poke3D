using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RotationController : MonoBehaviour
{
    [SerializeField] private Button leftButton;
    [SerializeField] private Button rightButton;
    // Start is called before the first frame update
    void Start()
    {
        leftButton.onClick.AddListener(RotateLeft);
        rightButton.onClick.AddListener(RotateRight);
    }

    private void RotateLeft()
    {
        Transform obj = FindFirstObjectByType<ARWithAPI>().GetCurentPokemon().transform.GetChild(0);
        if (obj != null)
            obj.Rotate(0, 0, 60);
    }

    private void RotateRight()
    {
        Transform obj = FindFirstObjectByType<ARWithAPI>().GetCurentPokemon().transform.GetChild(0);
        if(obj != null)
            obj.Rotate(0, 0, -60);
    }
}
