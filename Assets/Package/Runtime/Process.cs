#nullable enable
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace TSKT.Scenes
{
    public readonly struct InactivateObjects
    {
        readonly GameObject[] shouldActivateObjects;

        InactivateObjects(Scene scene)
        {
            shouldActivateObjects = DisableAllObjects(scene);
        }

        public static InactivateObjects Inactivate(Scene scene)
        {
            return new InactivateObjects(scene);
        }

        public readonly void Activate()
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
        readonly bool changeActiveScene;

        Add(string sceneName, AsyncOperation operation, bool changeActiveScene)
        {
            this.sceneName = sceneName;
            this.operation = operation;
            this.changeActiveScene = changeActiveScene;
        }

        public static Add Load(string sceneName, bool changeActiveScene = true)
        {
            var loadOperation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Additive);
            loadOperation.allowSceneActivation = false;
            return new Add(sceneName, loadOperation, changeActiveScene);
        }

        public readonly async Awaitable<Scene> Execute(System.IProgress<float>? progress = null)
        {
            operation.allowSceneActivation = true;
            if (progress != null)
            {
                _ = ReportUtil.Report(operation, progress);
            }
            await operation;
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid())
            {
                scene = SceneManager.GetSceneByPath(sceneName);
            }
            if (changeActiveScene)
            {
                SceneManager.SetActiveScene(scene);
            }
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

        public readonly async Awaitable Execute(bool waitUnload = true, System.IProgress<float>? progress = null)
        {
            foreach (var it in toUnload.GetRootGameObjects())
            {
                it.SetActive(false);
            }
            var unloadTask = SceneManager.UnloadSceneAsync(toUnload);

            await add.Execute(progress);

            if (waitUnload)
            {
                await unloadTask;
                _ = Resources.UnloadUnusedAssets();
            }
            else
            {
                unloadTask.completed += static _ => Resources.UnloadUnusedAssets();
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

            public readonly async Awaitable Revert()
            {
                UnityEngine.Assertions.Assert.IsTrue(toUnload.IsValid(), "scene to unload is invalid.");
                await Revert(toUnload);
            }
            public readonly async Awaitable Revert(Scene toUnload)
            {
                await SwitchWithRevertable.Revert(
                    toUnload,
                    toActivate,
                    shouldActivateObjects);
            }
        }

        readonly Scene toRevert;
        readonly Add add;

        public static SwitchWithRevertable Load(string sceneName, Scene currentScene)
        {
            var addScene = Add.Load(sceneName);
            return new SwitchWithRevertable(currentScene, addScene);
        }

        SwitchWithRevertable(Scene from, Add addScene)
        {
            toRevert = from;
            add = addScene;
        }

        public readonly async Awaitable<Revertable> Execute(System.IProgress<float>? progress = null)
        {
            var added = await add.Execute(progress);
            return new Revertable(added, toRevert, InactivateObjects.Inactivate(toRevert));
        }

        public static async Awaitable Revert(Scene toUnload, Scene toActivate, InactivateObjects objectsToActivate)
        {
            SceneManager.SetActiveScene(toActivate);
            foreach (var it in toUnload.GetRootGameObjects())
            {
                it.SetActive(false);
            }
            var unloading = SceneManager.UnloadSceneAsync(toUnload);

            objectsToActivate.Activate();

            await unloading;
            _ = Resources.UnloadUnusedAssets();
        }
    }

    public static class Reload
    {
        public static async Awaitable Execute(Scene scene, System.IProgress<float>? progress = null)
        {
            var sceneIndex = scene.buildIndex;
            await SceneManager.UnloadSceneAsync(scene);

            var loadOperation = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Additive);
            if (progress != null)
            {
                _ = ReportUtil.Report(loadOperation, progress);
            }
            await loadOperation;
            SceneManager.SetActiveScene(SceneManager.GetSceneByBuildIndex(sceneIndex));
            _ = Resources.UnloadUnusedAssets();
        }

    }

    static class ReportUtil
    {
        public static async Awaitable Report(AsyncOperation operation, System.IProgress<float> progress)
        {
            while (!operation.isDone)
            {
                progress.Report(operation.progress);
                await Awaitable.NextFrameAsync();
            }
            progress.Report(1f);
        }
    }
}
