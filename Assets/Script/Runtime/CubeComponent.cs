using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CubeComponent : MonoBehaviour
{
    void Start()
    {
        Debug.Log("你好");
        List<CubeComponent> list = new List<CubeComponent>();
        list.Add(this);
        foreach (var com in list)
        {
            Debug.Log(com);
        }
    }
}
