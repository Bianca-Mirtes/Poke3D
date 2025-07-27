using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class AnimationController : MonoBehaviour
{
    [SerializeField] private Button animationButton;
    [SerializeField] private string nameAnimation;

    // Start is called before the first frame update
    void Start()
    {
        animationButton.onClick.AddListener(StartAnimation);
    }

    private void StartAnimation()
    {
        Transform obj = GameObject.FindWithTag("Target").transform;
        if (obj != null)
        {
            Animator animator = obj.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.Play(nameAnimation);
            }
            else
            {
                Debug.LogWarning("Animator component not found on the target object.");
            }
        }
        else
        {
            Debug.LogWarning("Target object not found.");
        }
    }
}
