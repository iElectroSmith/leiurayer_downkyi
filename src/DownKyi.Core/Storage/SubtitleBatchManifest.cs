using DownKyi.Core.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;

namespace DownKyi.Core.Storage
{
    public class SubtitleBatchManifest
    {
        public int Version { get; set; } = 1;
        public long Mid { get; set; }
        public string UpName { get; set; }
        public int TabId { get; set; }
        public string TabName { get; set; }
        public int Total { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public List<SubtitleBatchItem> Items { get; set; } = new List<SubtitleBatchItem>();
    }

    public class SubtitleBatchItem
    {
        // pending | done | no_subtitle | failed
        public string Status { get; set; } = "pending";
        public string Bvid { get; set; }
        public string Title { get; set; }
        // 投稿时间（Unix 秒）。旧 manifest 没有此字段，反序列化为 0，下载时不加日期前缀。
        public long Created { get; set; }
        // 是否合集（多 P）。下载时从 view.Pages.Count > 1 拿到。旧 manifest 反序列化为 false。
        public bool IsMultiPart { get; set; }
        public List<string> Files { get; set; }
        public string Error { get; set; }
        public int Attempts { get; set; }
    }

    public static class SubtitleBatchManifestStore
    {
        public const string FileName = "_subtitle_manifest.json";
        private const string TmpFileName = "_subtitle_manifest.tmp";
        private const string Tag = "SubtitleBatchManifestStore";

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static string PathOf(string dir) => Path.Combine(dir, FileName);

        public static string BackupPath(string dir) =>
            Path.Combine(dir, $"_subtitle_manifest.bak.{DateTime.Now:yyyyMMddHHmmss}.json");

        /// <summary>
        /// 读取 manifest。文件不存在返回 null；解析失败抛异常由调用方决定如何处理（备份/重建）。
        /// </summary>
        public static SubtitleBatchManifest Load(string dir)
        {
            string path = PathOf(dir);
            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<SubtitleBatchManifest>(json, JsonSettings);
        }

        /// <summary>
        /// 保存 manifest。先写到 .tmp，再原子替换到正式文件，避免崩溃 / 掉电导致损坏。
        /// </summary>
        public static void Save(string dir, SubtitleBatchManifest manifest)
        {
            if (manifest == null) { return; }
            if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); }

            manifest.UpdatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            if (string.IsNullOrEmpty(manifest.CreatedAt))
            {
                manifest.CreatedAt = manifest.UpdatedAt;
            }

            string target = PathOf(dir);
            string tmp = Path.Combine(dir, TmpFileName);

            string json = JsonConvert.SerializeObject(manifest, JsonSettings);
            File.WriteAllText(tmp, json);

            try
            {
                if (File.Exists(target))
                {
                    File.Replace(tmp, target, null);
                }
                else
                {
                    File.Move(tmp, target);
                }
            }
            catch (Exception e)
            {
                LogManager.Error(Tag, e);
                // 兜底：直接覆盖（非原子，但能尽量保住数据）
                if (File.Exists(tmp))
                {
                    File.Copy(tmp, target, true);
                    File.Delete(tmp);
                }
                throw;
            }
        }
    }
}
