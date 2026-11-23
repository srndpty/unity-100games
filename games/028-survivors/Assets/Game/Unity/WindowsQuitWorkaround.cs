#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// Unity 6000.6.9f1 の Windows Player は、タイトルバーの閉じるボタンの処理の流れのまま終了すると、
    /// エンジンの後始末の途中で UnityPlayer.dll 内がクラッシュする。
    /// 終了要求をいったん取り消し、次のフレームで終了し直す。終了処理は通常どおりすべて動く。
    /// Unity を更新すると無効になるので、再現しなければこのファイルを消す。
    /// </summary>
    internal static class WindowsQuitWorkaround
    {
        private const string AffectedVersion = "6000.6.9f1";

        private static bool _quitDeferred;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (Application.unityVersion == AffectedVersion && !EvidenceCapture.IsRunning)
            {
                Application.wantsToQuit += DeferQuit;
            }
        }

        private static bool DeferQuit()
        {
            if (_quitDeferred)
            {
                return true;
            }

            _quitDeferred = true;
            Application.runInBackground = true; // 非アクティブのままでも次のフレームへ進める
            _ = QuitNextFrameAsync();
            return false;
        }

        private static async Awaitable QuitNextFrameAsync()
        {
            await Awaitable.NextFrameAsync();
            Application.Quit();
        }
    }
}
#endif
