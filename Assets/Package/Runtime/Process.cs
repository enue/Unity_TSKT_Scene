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
        readonly EntityId[] shouldActivateObjectEntityIds;
        InactivateObjects(Scene scene)
        {
            shouldActivateObjectEntityIds = DisableAllObjects(scene);
        }

        public static InactivateObjects Inactivate(Scene scene)
        {
            return new InactivateObjects(scene);
        }

        public readonly void Activate()
        {
            GameObject.SetGameObjectsActive(shouldActivateObjectEntityIds, true);
        }

        static EntityId[] DisableAllObjects(in Scene scene)
        {
            using (UnityEngine.Pool.ListPool<GameObject>.Get(out var objects))
            {
                scene.GetRootGameObjects(objects);
                Span<EntityId> span = stackalloc EntityId[objects.Count];
                int index = 0;
                foreach (var it in objects)
                {
                    if (it.activeSelf)
                    {
                        span[index] = it.GetEntityId();
                        ++index;
                    }
                }

                var result = span[..index].ToArray();
                GameObject.SetGameObjectsActive(result, false);
                return result;
            }
        }
    }

    public static class Add
    {
        public static async Awaitable<Scene> Execute(string sceneName, bool changeActiveScene = true, Action<AsyncOperation>? beforeComplete = null)
        {
            var loadOperation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Additive);
            beforeComplete?.Invoke(loadOperation);

            await loadOperation;

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

    public static class Switch
    {
        public static async Awaitable Execute(string sceneName, Scene toUnload, Action<AsyncOperation>? beforeComplete = null)
        {
            if (SceneManager.sceneCount == 1)
            {
                var loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                beforeComplete?.Invoke(loadOperation);
                await loadOperation;
            }
            else
            {
                await SceneManager.UnloadSceneAsync(toUnload);
                await Resources.UnloadUnusedAssets();
                await Add.Execute(sceneName, changeActiveScene: true, beforeComplete);
            }
        }
    }

    public static class SwitchWithRevertable
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

                SceneManager.SetActiveScene(toActivate);
                foreach (var it in toUnload.GetRootGameObjects())
                {
                    it.SetActive(false);
                }
                var unloading = SceneManager.UnloadSceneAsync(toUnload);
                if (waitUnload)
                {
                    await unloading;
                }

                shouldActivateObjects.Activate();

                _ = Resources.UnloadUnusedAssets();
            }
        }

        public static async Awaitable<Revertable> Execute(string sceneName, Scene currentScene, Action<AsyncOperation>? beforeComplete = null)
        {
            var inactivateds = InactivateObjects.Inactivate(currentScene);
            var added = await Add.Execute(sceneName, changeActiveScene: true, beforeComplete);

            return new Revertable(added, toActivate: currentScene, inactivateds);
        }
    }

    public static class Reload
    {
        public static async Awaitable Execute(Scene scene, Action<AsyncOperation>? beforeComplete = null)
        {
            var sceneIndex = scene.buildIndex;

            if (SceneManager.sceneCount == 1)
            {
                var loadOperation = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Single);
                beforeComplete?.Invoke(loadOperation);
                await loadOperation;
            }
            else
            {
                await SceneManager.UnloadSceneAsync(scene);
                var loadOperation = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Additive);
                beforeComplete?.Invoke(loadOperation);
                await loadOperation;
                SceneManager.SetActiveScene(SceneManager.GetSceneByBuildIndex(sceneIndex));
            }
            _ = Resources.UnloadUnusedAssets();
        }

    }
}
