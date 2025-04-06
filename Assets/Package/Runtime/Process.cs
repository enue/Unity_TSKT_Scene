#nullable enable
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System;

namespace TSKT.Scenes
{
    public readonly struct InactivateObjects
    {
        readonly int[] shouldActivateObjectInstanceIds;
        InactivateObjects(Scene scene)
        {
            shouldActivateObjectInstanceIds = DisableAllObjects(scene);
        }

        public static InactivateObjects Inactivate(Scene scene)
        {
            return new InactivateObjects(scene);
        }

        public readonly void Activate()
        {
            GameObject.SetGameObjectsActive(shouldActivateObjectInstanceIds, true);
        }

        static int[] DisableAllObjects(in Scene scene)
        {
            using (UnityEngine.Pool.ListPool<GameObject>.Get(out var objects))
            {
                scene.GetRootGameObjects(objects);
                Span<int> span = stackalloc int[objects.Count];
                int index = 0;
                foreach (var it in objects)
                {
                    if (it.activeSelf)
                    {
                        span[index] = it.GetInstanceID();
                    }
                    ++index;
                }

                var result = span[..index].ToArray();
                GameObject.SetGameObjectsActive(result, false);
                return result;
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
        readonly AsyncOperation? singleSceneOperation;

        Switch(Scene from, Add addScene, AsyncOperation? singleSceneOperation)
        {
            toUnload = from;
            add = addScene;
            this.singleSceneOperation = singleSceneOperation;
        }
        public static Switch Load(string sceneName, Scene toUnload)
        {
            if (SceneManager.sceneCount == 1)
            {
                var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                operation.allowSceneActivation = false;
                return new Switch(toUnload, default, operation);
            }
            else
            {
                var addScene = Add.Load(sceneName);
                return new Switch(toUnload, addScene, null);
            }
        }

        public readonly async Awaitable Execute(bool waitUnload = true, System.IProgress<float>? progress = null)
        {
            foreach (var it in toUnload.GetRootGameObjects())
            {
                it.SetActive(false);
            }

            if (singleSceneOperation == null)
            {
                await add.Execute(progress);
                var unloadTask = SceneManager.UnloadSceneAsync(toUnload);

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
            else
            {
                if (progress != null)
                {
                    _ = ReportUtil.Report(singleSceneOperation, progress);
                }
                singleSceneOperation.allowSceneActivation = true;
                await singleSceneOperation;
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

            public readonly async Awaitable Revert(bool waitUnload = false)
            {
                UnityEngine.Assertions.Assert.IsTrue(toUnload.IsValid(), "scene to unload is invalid.");
                await Revert(toUnload, waitUnload);
            }
            public readonly async Awaitable Revert(Scene toUnload, bool waitUnload = false)
            {
                var operation = SwitchWithRevertable.Revert(
                    toUnload,
                    toActivate,
                    shouldActivateObjects);
                if (waitUnload)
                {
                    await operation;
                }
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

            if (SceneManager.sceneCount == 1)
            {
                var loadOperation = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Single);
                if (progress != null)
                {
                    _ = ReportUtil.Report(loadOperation, progress);
                }
                await loadOperation;
            }
            else
            {
                await SceneManager.UnloadSceneAsync(scene);
                var loadOperation = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Additive);
                if (progress != null)
                {
                    _ = ReportUtil.Report(loadOperation, progress);
                }
                await loadOperation;
                SceneManager.SetActiveScene(SceneManager.GetSceneByBuildIndex(sceneIndex));
            }
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
