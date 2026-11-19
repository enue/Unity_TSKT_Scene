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

    public static class SceneLoader
    {
        public static async Awaitable<Scene> Add(string sceneName, bool changeActiveScene = true, Action<AsyncOperation>? onLoadStarted = null)
        {
            var loadOperation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Additive);
            // ロード中のシーンは末尾に追加される。名前で検索すると、同名のシーンがロード済みの場合に取り違える。
            var scene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
            onLoadStarted?.Invoke(loadOperation);

            await loadOperation;

            if (changeActiveScene)
            {
                SceneManager.SetActiveScene(scene);
            }
            return scene;
        }

        public static async Awaitable<Scene> Switch(string sceneName, Scene toUnload, Action<AsyncOperation>? onLoadStarted = null)
        {
            if (SceneManager.sceneCount == 1)
            {
                var loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                onLoadStarted?.Invoke(loadOperation);
                await loadOperation;
                // LoadSceneMode.Singleの完了後は、ロードしたシーンだけが残ってアクティブになっている。
                return SceneManager.GetActiveScene();
            }
            else
            {
                await SceneManager.UnloadSceneAsync(toUnload);
                await Resources.UnloadUnusedAssets();
                return await Add(sceneName, changeActiveScene: true, onLoadStarted);
            }
        }

        public static async Awaitable<Revertable> SwitchAsRevertable(string sceneName, Scene currentScene, Action<AsyncOperation>? onLoadStarted = null)
        {
            var inactivateds = InactivateObjects.Inactivate(currentScene);
            var added = await Add(sceneName, changeActiveScene: true, onLoadStarted);

            return new Revertable(added, toActivate: currentScene, inactivateds);
        }

        public static async Awaitable<Scene> Reload(Scene scene, Action<AsyncOperation>? onLoadStarted = null)
        {
            var sceneIndex = scene.buildIndex;

            if (SceneManager.sceneCount == 1)
            {
                var loadOperation = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Single);
                onLoadStarted?.Invoke(loadOperation);
                await loadOperation;
                // LoadSceneMode.Singleの完了後は、ロードしたシーンだけが残ってアクティブになっている。
                return SceneManager.GetActiveScene();
            }
            else
            {
                var wasActive = SceneManager.GetActiveScene() == scene;
                await SceneManager.UnloadSceneAsync(scene);
                var loadOperation = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Additive);
                // ロード中のシーンは末尾に追加される。
                var reloaded = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
                onLoadStarted?.Invoke(loadOperation);
                await loadOperation;
                if (wasActive)
                {
                    SceneManager.SetActiveScene(reloaded);
                }
                _ = Resources.UnloadUnusedAssets();
                return reloaded;
            }
        }

    }
}
