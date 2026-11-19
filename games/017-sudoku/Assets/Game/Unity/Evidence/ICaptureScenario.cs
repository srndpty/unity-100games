using UnityEngine;

namespace Game.Unity.Evidence
{
    /// <summary>
    /// 証跡撮影の台本。各作品がシーン上のコンポーネントとして 1 つだけ実装し、
    /// 画面を進めながら <see cref="CaptureContext.ShootAsync"/> で撮影する。
    /// </summary>
    public interface ICaptureScenario
    {
        Awaitable RunAsync(CaptureContext capture);
    }
}
