using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TSKT
{
    public class PreloadSample : MonoBehaviour
    {
        private void Awake()
        {
            Debug.Log("awake");
        }
        void Start()
        {
            Debug.Log("start");
            Scenes.SceneUtil.Preload("PreloadSample");
            Scenes.SceneUtil.Preload("PreloadSample3");
            Scenes.SceneUtil.Preload("PreloadSample2");

            Scenes.SceneUtil.Load("PreloadSample2");
        }
    }
}
