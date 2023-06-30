using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DontDestroyMusic2 : MonoBehaviour
{
    private static DontDestroyMusic2 _instance;

    public static DontDestroyMusic2 instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = GameObject.FindObjectOfType<DontDestroyMusic2>();
                DontDestroyOnLoad(_instance.gameObject);
            }
            return _instance;
        }
    }
    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(this);
        }
        else
        {
            if (this != _instance)
                Destroy(this.gameObject);
        }
    }
}
