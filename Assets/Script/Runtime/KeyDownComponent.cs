using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class KeyDownComponent : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            Debug.Log("按下了回车键");
        }
        else if (Input.GetKeyDown(KeyCode.A))
        {
            Debug.Log("按下了A键");
        }
    }
}
