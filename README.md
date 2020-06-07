# install

Unity Package Manager

add package from git url

+ `https://github.com/Cysharp/UniTask.git?path=Assets/UniRx.Async`
+ `https://github.com/enue/Unity_TSKT_Editor.git?path=Assets/Package`
+ `https://github.com/enue/Unity_TSKT_File.git?path=Assets/Package`
+ `https://github.com/enue/Unity_TSKT_Scene.git?path=Assets/Package`

# usage

```cs
// 最小構成
var process = Scenes.Add.Load(sceneName);
await process.Execute();
```

```cs
// フェードイン/アウトに対応できる。
var process = Scenes.Switch.Load(sceneName);
await DoFadeOut();
await process.Execute();
await DoFadeIn();
```

```cs
// ローディングゲージ表示
image.fillAmount = LoadingProgress.Instance.GetProgress();
```
