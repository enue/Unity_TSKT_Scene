using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

namespace TSKT.Scenes
{
    public static class SceneUtil
    {
        readonly static List<(string scene, AsyncOperation operation)> sceneLoadOperations = new List<(string, AsyncOperation)>();

        static public int Preload(string sceneName)
        {
            var index = sceneLoadOperations.FindIndex(_ => _.scene == sceneName);

            if (index < 0)
            {
                var loadOperation = SceneManager.LoadSceneAsync(
                    sceneName,
                    LoadSceneMode.Additive);
                loadOperation.allowSceneActivation = false;
                sceneLoadOperations.Add((sceneName, loadOperation));

                index = sceneLoadOperations.Count - 1;
            }
            return index;
        }

        static public AsyncOperation Load(string sceneName)
        {
            var index = Preload(sceneName);

            for (int i = 0; i < index; ++i)
            {
                var (scene, operation) = sceneLoadOperations[i];
                operation.allowSceneActivation = true;

                void UnloadUnnecessaryScene(Scene loadedScene, LoadSceneMode _)
                {
                    if (loadedScene.name == scene)
                    {
                        foreach(var it in loadedScene.GetRootGameObjects())
                        {
                            it.SetActive(false);
                        }
                        SceneManager.UnloadSceneAsync(loadedScene);
                        SceneManager.sceneLoaded -= UnloadUnnecessaryScene;
                    }
                }

                SceneManager.sceneLoaded += UnloadUnnecessaryScene;
            }

            var result = sceneLoadOperations[index].operation;
            result.allowSceneActivation = true;

            sceneLoadOperations.RemoveRange(0, index + 1);

            return result;
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

        static public Add Load(string sceneName)
        {
            var loadOperation = SceneUtil.Load(sceneName);
            loadOperation.allowSceneActivation = false;
            LoadingProgress.Instance.Add(loadOperation, 0.9f);

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
        public static Switch Load(string sceneName)
        {
            var fromScene = SceneManager.GetActiveScene();
            var addScene = Add.Load(sceneName);

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

    public static class Reload
    {
        static public async UniTask Execute(System.IProgress<float> progress = null)
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
