using UnityEngine;
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

    public readonly struct AddProcess
    {
        readonly string sceneName;
        public readonly AsyncOperation loadOperation;

        AddProcess(string sceneName, AsyncOperation loadOperation)
        {
            this.sceneName = sceneName;
            this.loadOperation = loadOperation;
        }

        static public AddProcess Load(string sceneName)
        {
            var loadOperation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Additive);
            loadOperation.allowSceneActivation = false;

            return new AddProcess(sceneName, loadOperation);
        }

        public async UniTask Execute()
        {
            loadOperation.allowSceneActivation = true;
            await loadOperation;
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneName));
        }
    }

    public readonly struct SwitchProcess
    {
        readonly Scene toUnload;
        readonly AddProcess add;
        public AsyncOperation LoadOperation => add.loadOperation;

        SwitchProcess(Scene from, AddProcess addScene)
        {
            toUnload = from;
            add = addScene;
        }
        public static SwitchProcess Load(string sceneName)
        {
            var fromScene = SceneManager.GetActiveScene();
            var addScene = AddProcess.Load(sceneName);

            return new SwitchProcess(fromScene, addScene);
        }

        public async UniTask Execute()
        {
            await add.Execute();
            await SceneManager.UnloadSceneAsync(toUnload);
            _ = Resources.UnloadUnusedAssets();
        }
    }

    public readonly struct SwitchWithRevertableProcess
    {
        readonly Scene toRevert;
        readonly AddProcess add;
        public AsyncOperation LoadOperation => add.loadOperation;

        static public SwitchWithRevertableProcess Load(string sceneName)
        {
            var fromScene = SceneManager.GetActiveScene();
            var addScene = AddProcess.Load(sceneName);
            return new SwitchWithRevertableProcess(fromScene, addScene);
        }

        SwitchWithRevertableProcess(Scene from, AddProcess addScene)
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

    public readonly struct ReloadProcess
    {
        readonly int sceneIndex;
        public readonly AsyncOperation loadOperation;

        static public async UniTask<ReloadProcess> Start()
        {
            var scene = SceneManager.GetActiveScene();
            var sceneIndex = scene.buildIndex;
            await SceneManager.UnloadSceneAsync(scene);
            var loadOperation = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Additive);

            return new ReloadProcess(loadOperation, sceneIndex);
        }

        ReloadProcess(AsyncOperation loadOperation, int sceneIndex)
        {
            this.loadOperation = loadOperation;
            this.sceneIndex = sceneIndex;
        }

        public async UniTask Finish()
        {
            await loadOperation;
            SceneManager.SetActiveScene(SceneManager.GetSceneByBuildIndex(sceneIndex));
            _ = Resources.UnloadUnusedAssets();
        }
    }
}