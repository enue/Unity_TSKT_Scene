using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
#nullable enable

namespace TSKT.Scenes
{
    public readonly struct Preload
    {
        readonly static Dictionary<string, Preload> sceneLoadOperations = new Dictionary<string, Preload>();

        public readonly AsyncOperation operation;
        readonly List<GameObject> shouldActivateObjects;

        public static void Create(string sceneName)
        {
            if (!sceneLoadOperations.ContainsKey(sceneName))
            {
                var obj = new Preload(sceneName);
                sceneLoadOperations.Add(sceneName, obj);
            }
        }

        public static bool TryPop(string sceneName, out Preload result)
        {
            if (sceneLoadOperations.TryGetValue(sceneName, out result))
            {
                sceneLoadOperations.Remove(sceneName);
                return true;
            }
            return false;
        }

        Preload(string sceneName)
        {
            operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Additive);
            shouldActivateObjects = new List<GameObject>();

            var activeObjects = shouldActivateObjects;
            SceneManager.sceneLoaded += DeactiveScene;

            void DeactiveScene(Scene loadedScene, LoadSceneMode _)
            {
                if (loadedScene.name == sceneName)
                {
                    var rootObjects = loadedScene.GetRootGameObjects();
                    foreach (var it in rootObjects)
                    {
                        if (it.activeSelf)
                        {
                            activeObjects.Add(it);
                        }
                        it.SetActive(false);
                    }
                    SceneManager.sceneLoaded -= DeactiveScene;
                }
            }
        }

        readonly public void Activate()
        {
            foreach (var it in shouldActivateObjects)
            {
                it.SetActive(true);
            }
        }
    }

    public readonly struct Revertable
    {
        readonly Scene toActivate;
        readonly GameObject[] shouldActivateObjects;

        public Revertable(Scene from, List<GameObject> shouldActivateObjects)
        {
            toActivate = from;
            this.shouldActivateObjects = shouldActivateObjects.ToArray();
        }

        readonly public async UniTask Revert()
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
        readonly Preload? loadOperation;
        readonly AsyncOperation operation;

        Add(string sceneName, Preload? loadOperation, AsyncOperation operation)
        {
            this.sceneName = sceneName;
            this.loadOperation = loadOperation;
            this.operation = operation;
        }

        static public Add Load(string sceneName)
        {
            if (Preload.TryPop(sceneName, out var preload))
            {
                LoadingProgress.Instance.Add(preload.operation, 1.0f);
                return new Add(sceneName, preload, preload.operation);
            }
            else
            {
                var loadOperation = SceneManager.LoadSceneAsync(
                    sceneName,
                    LoadSceneMode.Additive);
                loadOperation.allowSceneActivation = false;
                LoadingProgress.Instance.Add(loadOperation, 0.9f);
                return new Add(sceneName, default, loadOperation);
            }
        }

        readonly public async UniTask Execute()
        {
            operation.allowSceneActivation = true;
            await operation;
            loadOperation?.Activate();
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
        public static Switch Load(string sceneName)
        {
            var fromScene = SceneManager.GetActiveScene();
            var addScene = Add.Load(sceneName);

            return new Switch(fromScene, addScene);
        }

        readonly public async UniTask Execute()
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

        static public SwitchWithRevertable Load(string sceneName)
        {
            var fromScene = SceneManager.GetActiveScene();
            var addScene = Add.Load(sceneName);
            return new SwitchWithRevertable(fromScene, addScene);
        }

        SwitchWithRevertable(Scene from, Add addScene)
        {
            toRevert = from;
            add = addScene;
        }

        readonly public async UniTask<Revertable> Execute()
        {
            await add.Execute();
            return new Revertable(toRevert, DisableAllObjects(toRevert));
        }

        static List<GameObject> DisableAllObjects(in Scene scene)
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

    public static class Reload
    {
        static public async UniTask Execute()
        {
            var scene = SceneManager.GetActiveScene();
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
