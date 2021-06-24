using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
#nullable enable

namespace TSKT.Scenes
{
    public readonly struct Revertable
    {
        readonly Scene toUnload;
        readonly Scene toActivate;
        readonly GameObject[] shouldActivateObjects;

        public Revertable(Scene toUnload, Scene toActivate, GameObject[] shouldActivateObjects)
        {
            this.toUnload = toUnload;
            this.toActivate = toActivate;
            this.shouldActivateObjects = shouldActivateObjects;
        }

        readonly public void Revert()
        {
            SceneManager.SetActiveScene(toActivate);
            _ = SceneManager.UnloadSceneAsync(toUnload);

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
        readonly AsyncOperation operation;

        Add(string sceneName, AsyncOperation operation)
        {
            this.sceneName = sceneName;
            this.operation = operation;
        }

        static public Add Load(string sceneName)
        {
            var loadOperation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Additive);
            loadOperation.allowSceneActivation = false;
            LoadingProgress.Instance.Add(loadOperation, 0.9f);
            return new Add(sceneName, loadOperation);
        }

        readonly public async UniTask<Scene> Execute()
        {
            operation.allowSceneActivation = true;
            await operation;
            var scene = SceneManager.GetSceneByName(sceneName);
            SceneManager.SetActiveScene(scene);
            return scene;
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
        public static Switch Load(string sceneName, Scene toUnload)
        {
            var addScene = Add.Load(sceneName);
            return new Switch(toUnload, addScene);
        }

        readonly public async UniTask Execute(bool waitUnload = true)
        {
            await add.Execute();
            if (waitUnload)
            {
                await SceneManager.UnloadSceneAsync(toUnload);
                _ = Resources.UnloadUnusedAssets();
            }
            else
            {
                _ = SceneManager.UnloadSceneAsync(toUnload).ToUniTask()
                    .ContinueWith(Resources.UnloadUnusedAssets);
            }
        }
    }

    public readonly struct SwitchWithRevertable
    {
        readonly Scene toRevert;
        readonly Add add;

        static public SwitchWithRevertable Load(string sceneName, Scene currentScene)
        {
            var addScene = Add.Load(sceneName);
            return new SwitchWithRevertable(currentScene, addScene);
        }

        SwitchWithRevertable(Scene from, Add addScene)
        {
            toRevert = from;
            add = addScene;
        }

        readonly public async UniTask<Revertable> Execute()
        {
            var added = await add.Execute();
            return new Revertable(added, toRevert, DisableAllObjects(toRevert));
        }

        static GameObject[] DisableAllObjects(in Scene scene)
        {
            using (UnityEngine.Pool.ListPool<GameObject>.Get(out var result))
            {
                foreach (var it in scene.GetRootGameObjects())
                {
                    if (it.activeSelf)
                    {
                        result.Add(it);
                    }
                    it.SetActive(false);
                }
                return result.ToArray();
            }
        }
    }

    public static class Reload
    {
        static public async UniTask Execute(Scene scene)
        {
            var sceneIndex = scene.buildIndex;
            await SceneManager.UnloadSceneAsync(scene);

            var loadOperation = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Additive);
            LoadingProgress.Instance.Add(loadOperation, 1f);
            await loadOperation;
            SceneManager.SetActiveScene(SceneManager.GetSceneByBuildIndex(sceneIndex));
            _ = Resources.UnloadUnusedAssets();
        }
    }
}
