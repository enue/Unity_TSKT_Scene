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
            StartCoroutine(LoadCoroutine());
        }

        IEnumerator LoadCoroutine()
        {
            Scenes.Preload.Create("PreloadSample");
            Scenes.Preload.Create("PreloadSample2");
            Scenes.Preload.Create("PreloadSample3");

            yield return Resources.LoadAsync<Sprite>("Square");

            Scenes.Add.Load("PreloadSample2").Execute();
            Scenes.Preload.Create("PreloadSample");
            Scenes.Add.Load("PreloadSample2").Execute();
        }
    }
}
