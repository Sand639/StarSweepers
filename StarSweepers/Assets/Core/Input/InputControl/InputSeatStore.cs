using System;
using System.IO;
using UnityEngine;

namespace ProjectEL4S.InputControl
{
    /// <summary>
    /// 保存しておく固定登録の中身。番号（＝席）ごとのデバイスの名前（Windows のデバイスパス）。空文字の番号は押した順。
    /// </summary>
    [Serializable]
    public sealed class InputSeatSaveData
    {
        public string[] keyboards = Array.Empty<string>();
        public string[] mice = Array.Empty<string>();
        /// <summary>いつ登録したか（表示用）。</summary>
        public string savedAt = string.Empty;
    }

    /// <summary>
    /// 固定登録をファイルに読み書きする。置き場所は <see cref="FilePath"/>。
    ///
    /// Windows では、エディタとビルドのどちらで登録しても同じファイル
    /// （%USERPROFILE%\AppData\LocalLow\会社名\製品名\InputSeats.json）を使うので、エディタで登録した内容がビルドでも効く。
    /// 展示用の PC を作り直したときは、このファイルを写すか、もう一度登録すればよい。
    /// </summary>
    public static class InputSeatStore
    {
        public const string FileName = "InputSeats.json";

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static bool Exists => File.Exists(FilePath);

        /// <summary>無い・読めないときは null。</summary>
        public static InputSeatSaveData Load()
        {
            string path = FilePath;
            if (!File.Exists(path)) return null;
            try
            {
                var data = JsonUtility.FromJson<InputSeatSaveData>(File.ReadAllText(path));
                if (data == null) return null;
                data.keyboards = data.keyboards ?? Array.Empty<string>();
                data.mice = data.mice ?? Array.Empty<string>();
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[InputSeat] 登録ファイルを読めませんでした（押した順で割り当てます）: {path}\n{e.Message}");
                return null;
            }
        }

        public static bool Save(InputSeatSaveData data)
        {
            string path = FilePath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(data, true));
                Debug.Log($"[InputSeat] キーボード・マウスの登録を保存しました: {path}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[InputSeat] 登録を保存できませんでした: {path}\n{e.Message}");
                return false;
            }
        }

        public static void Delete()
        {
            string path = FilePath;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[InputSeat] 登録ファイルを消せませんでした: {path}\n{e.Message}");
            }
        }
    }
}
