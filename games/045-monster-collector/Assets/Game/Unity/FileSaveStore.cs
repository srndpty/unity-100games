#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Game.App;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 一時ファイル書き込み＋原子的置換を行うファイルセーブストア。I/O の失敗は結果として返す。
    /// バッチ実行や証跡撮影では通常プレイの persistentDataPath を汚さないよう独立した一時領域を使う。
    /// </summary>
    public sealed class FileSaveStore : ISaveStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
        private readonly string _filePath;

        public FileSaveStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("セーブファイルパスが空です。", nameof(filePath));
            }

            _filePath = Path.GetFullPath(filePath);
        }

        public string FilePath => _filePath;

        public bool Exists => File.Exists(_filePath);

        public static FileSaveStore CreateDefault(string fileName)
        {
            if (EvidenceCapture.IsRunning || Application.isBatchMode)
            {
                var isolatedDir = Path.Combine(
                    Application.temporaryCachePath,
                    "thornwild_isolated_" + Process.GetCurrentProcess().Id);
                return new FileSaveStore(Path.Combine(isolatedDir, fileName));
            }

            return new FileSaveStore(Path.Combine(Application.persistentDataPath, fileName));
        }

        public SaveReadStatus TryRead(out string content, out string failure)
        {
            content = string.Empty;
            failure = string.Empty;
            try
            {
                if (!File.Exists(_filePath))
                {
                    return SaveReadStatus.Missing;
                }

                content = File.ReadAllText(_filePath, Utf8NoBom);
                return SaveReadStatus.Loaded;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                failure = ex.Message;
                return SaveReadStatus.Failed;
            }
        }

        public bool TryWrite(string content, out string failure)
        {
            var tempPath = _filePath + ".tmp";
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(tempPath, content ?? string.Empty, Utf8NoBom);
                if (File.Exists(_filePath))
                {
                    File.Replace(tempPath, _filePath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tempPath, _filePath);
                }

                failure = string.Empty;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                failure = ex.Message;
                return false;
            }
        }

        public void Delete()
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }

            var tempPath = _filePath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
