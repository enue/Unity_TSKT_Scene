﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

namespace TSKT.Scenes
{
    public readonly struct Revertable
    {
        readonly Scene toActivate;
        readonly GameObject[] shouldActivateObjects;

        public Revertable(Scene from, List<GameObject> shouldActivateObjects)
        {
            toActivate = from;
            this.shouldActivateObjects = shouldActivateObjects.ToArray();
        }

        public async UniTask Revert()
        {
            var currentScene = SceneManager.GetActiveScene();

            SceneManager.SetActiveScene(toActivate);
            await SceneManager.UnloadSceneAsync(currentScene);

            foreach (var it in shouldActivateObjects)
            {
                if (it)
                {
                    it.SetActive(true);
                }
            }

            _ = Resources.UnloadUnusedAssets();
        }
    }

    public readonly struct Add
    {
        readonly string sceneName;
        readonly AsyncOperation loadOperation;

        Add(string sceneName, AsyncOperation loadOperation)
        {
            this.sceneName = sceneName;
            this.loadOperation = loadOperation;
        }

        static public Add Load(string sceneName, System.IProgress<float> progress = null)
        {
            var loadOperation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Additive);
            if (progress != null)
            {
                loadOperation.ToUniTask(progress);
            }
            loadOperation.allowSceneActivation = false;

            return new Add(sceneName, loadOperation);
        }

        public async UniTask Execute()
        {
            loadOperation.allowSceneActivation = true;
            await loadOperation;
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneName));
        }
    }

    public readonly struct Switch
    {
        readonly Scene toUnload;
        readonly Add add;

        Switch(Scene from, Add addScene)
        {
            toUnload = from;
            add = addScene;
        }
        public static Switch Load(string sceneName, System.IProgress<float> progress = null)
        {
            var fromScene = SceneManager.GetActiveScene();
            var addScene = Add.Load(sceneName, progress);

            return new Switch(fromScene, addScene);
        }

        public async UniTask Execute()
        {
            await add.Execute();
            await SceneManager.UnloadSceneAsync(toUnload);
            _ = Resources.UnloadUnusedAssets();
        }
    }

    public readonly struct SwitchWithRevertable
    {
        readonly Scene toRevert;
        readonly Add add;

        static public SwitchWithRevertable Load(string sceneName, System.IProgress<float> progress = null)
        {
            var fromScene = SceneManager.GetActiveScene();
            var addScene = Add.Load(sceneName, progress);
            return new SwitchWithRevertable(fromScene, addScene);
        }

        SwitchWithRevertable(Scene from, Add addScene)
        {
            toRevert = from;
            add = addScene;
        }

        public async UniTask<Revertable> Execute()
        {
            await add.Execute();
            return new Revertable(toRevert, DisableAllObjects(toRevert));
        }

        static List<GameObject> DisableAllObjects(Scene scene)
        {
            var result = new List<GameObject>();
            foreach (var it in scene.GetRootGameObjects())
            {
                if (it.activeSelf)
                {
                    result.Add(it);
                }
                it.SetActive(false);
            }
            return result;
        }
    }

    public readonly struct Reload
    {
        static public async UniTask Start(System.IProgress<float> progress = null)
        {
            var scene = SceneManager.GetActiveScene();
            var sceneIndex = scene.buildIndex;
            await SceneManager.UnloadSceneAsync(scene);

            var loadOperation = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Additive);
            if (progress != null)
            {
                loadOperation.ToUniTask(progress).Forget();
            }
            await loadOperation;
            SceneManager.SetActiveScene(SceneManager.GetSceneByBuildIndex(sceneIndex));
            _ = Resources.UnloadUnusedAssets();
        }
    }
}