# install

Unity Package Manager

add package from git url

+ `https://github.com/Cysharp/UniTask.git?path=Assets/UniRx.Async`
+ `https://github.com/enue/Unity_TSKT_Scene.git?path=Assets/Package`

# usage

```cs
// 最小構成
var process = Scenes.AddProcess.Load(sceneName);
await process.Execute();
```

```cs
// フェードイン/アウトやローディングゲージに対応できる。
var process = Scenes.SwitchProcess.Load(sceneName);
ShowProgressBar(process.loadOperation);
await DoFadeOut();
await process.Execute();
await DoFadeIn();
```
