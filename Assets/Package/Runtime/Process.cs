using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
#nullable enable

namespace TSKT.Scenes
{
    public readonly struct InactivateObjects
    {
        readonly GameObject[] shouldActivateObjects;

        InactivateObjects(Scene scene)
        {
            shouldActivateObjects = DisableAllObjects(scene);
        }

        static public InactivateObjects Inactivate(Scene scene)
        {
            return new InactivateObjects(scene);
        }

        readonly public void Activate()
        {
            foreach (var it in shouldActivateObjects)
            {
                if (it)
                {
                    it.SetActive(true);
                }
            }
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
            // HACK : 新シーンロード->旧シーンアンロード->新シーンアクティベートの順にしたいが、ロードがallowSceneActivation=falseのためアンロードも終えることができない（Unityの非同期処理は順に処理される仕様のため）。なのでSetActive(false)で対応する。
            foreach (var it in toUnload.GetRootGameObjects())
            {
                it.gameObject.SetActive(false);
            }
            await add.Execute();

            var unloadTask = SceneManager.UnloadSceneAsync(toUnload);
            if (waitUnload)
            {
                await unloadTask;
                _ = Resources.UnloadUnusedAssets();
            }
            else
            {
                unloadTask
                    .ToUniTask()
                    .ContinueWith(Resources.UnloadUnusedAssets)
                    .Forget();
            }
        }
    }

    public readonly struct SwitchWithRevertable
    {
        public readonly struct Revertable
        {
            public readonly Scene toUnload;
            public readonly Scene toActivate;
            public readonly InactivateObjects shouldActivateObjects;

            public Revertable(Scene toUnload, Scene toActivate, InactivateObjects shouldActivateObjects)
            {
                this.toUnload = toUnload;
                this.toActivate = toActivate;
                this.shouldActivateObjects = shouldActivateObjects;
            }

            readonly public void Revert()
            {
                SwitchWithRevertable.Revert(
                    toUnload,
                    toActivate,
                    shouldActivateObjects);
            }
        }

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
            return new Revertable(added, toRevert, InactivateObjects.Inactivate(toRevert));
        }

        static public void Revert(Scene toUnload, Scene toActivate, InactivateObjects objectsToActivate)
        {
            SceneManager.SetActiveScene(toActivate);
            _ = SceneManager.UnloadSceneAsync(toUnload);

            objectsToActivate.Activate();

            _ = Resources.UnloadUnusedAssets();
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
