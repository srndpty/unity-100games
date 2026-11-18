#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// Unity 6000.6.9f1 の Windows Player は、タイトルバーの閉じるボタンで終了すると、
    /// エンジンの後始末中に届いたウィンドウメッセージの処理で UnityPlayer.dll 内がクラッシュする。
    /// C# の終了処理が済んだ時点でプロセスを終え、問題の後始末を通らないようにする。
    /// 証跡撮影は終了コードを返す必要があるので、通常の終了処理に任せる。
    /// </summary>
    internal static class WindowsQuitWorkaround
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (!EvidenceCapture.IsRunning)
            {
                Application.quitting += ExitProcess;
            }
        }

        private static void ExitProcess()
        {
            PlayerPrefs.Save();
            TerminateProcess(GetCurrentProcess(), 0);
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    }
}
#endif
