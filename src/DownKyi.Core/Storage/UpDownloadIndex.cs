using DownKyi.Core.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;

namespace DownKyi.Core.Storage
{
    /// <summary>
    /// UP 主下载历史聚合索引。<br/>
    /// 数据来源是各 UP 主工作目录下的字幕 manifest，但聚合存放在程序根目录，
    /// 跟用户选的下载根目录解耦——用户改下载目录后，历史记录依然保留。
    /// </summary>
    public class UpDownloadIndex
    {
        public int Version { get; set; } = 1;
        public List<UpDownloadIndexEntry> Items { get; set; } = new List<UpDownloadIndexEntry>();
    }

    public class UpDownloadIndexEntry
    {
        public long Mid { get; set; }
        public string UpName { get; set; }
        // 最近一次批量字幕下载的更新时间（来自 manifest.UpdatedAt）
        public string LastUpdate { get; set; }
        // 字幕已 done 的数量
        public int DoneCount { get; set; }
        // manifest 中累计跟踪的视频总数（pending + done + no_subtitle + failed）
        public int TotalCount { get; set; }
    }

    public static class UpDownloadIndexStore
    {
        public const string FileName = "up_download_index.json";
        public const string SubDir = "Storage";
        private const string Tag = "UpDownloadIndexStore";

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        /// <summary>
        /// 索引文件在程序根目录下的固定路径。
        /// </summary>
        public static string DefaultPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, SubDir, FileName);
        }

        /// <summary>
        /// 加载索引。文件不存在或解析失败时返回空索引（不抛异常）。
        /// </summary>
        public static UpDownloadIndex Load()
        {
            string path = DefaultPath();
            if (!File.Exists(path))
            {
                return new UpDownloadIndex();
            }
            try
            {
                string json = File.ReadAllText(path);
                var idx = JsonConvert.DeserializeObject<UpDownloadIndex>(json, JsonSettings);
                return idx ?? new UpDownloadIndex();
            }
            catch (Exception e)
            {
                LogManager.Error(Tag, e);
                return new UpDownloadIndex();
            }
        }

        /// <summary>
        /// 保存索引（原子替换：tmp → File.Replace / File.Move）。
        /// </summary>
        public static void Save(UpDownloadIndex index)
        {
            if (index == null) { return; }
            string target = DefaultPath();
            string dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string tmp = target + ".tmp";
            string json = JsonConvert.SerializeObject(index, JsonSettings);
            File.WriteAllText(tmp, json);
            try
            {
                if (File.Exists(target)) { File.Replace(tmp, target, null); }
                else { File.Move(tmp, target); }
            }
            catch (Exception e)
            {
                LogManager.Error(Tag, e);
                if (File.Exists(tmp)) { File.Copy(tmp, target, true); File.Delete(tmp); }
                throw;
            }
        }

        /// <summary>
        /// 增量更新某 UP 主的记录。会自动落盘。
        /// </summary>
        public static void Upsert(long mid, string upName, string lastUpdate, int doneCount, int totalCount)
        {
            var idx = Load();
            var existing = idx.Items.Find(x => x.Mid == mid);
            if (existing == null)
            {
                idx.Items.Add(new UpDownloadIndexEntry
                {
                    Mid = mid,
                    UpName = upName,
                    LastUpdate = lastUpdate,
                    DoneCount = doneCount,
                    TotalCount = totalCount
                });
            }
            else
            {
                existing.UpName = upName;
                existing.LastUpdate = lastUpdate;
                existing.DoneCount = doneCount;
                existing.TotalCount = totalCount;
            }
            Save(idx);
        }

        /// <summary>
        /// 扫描下载根目录下每个 {mid}_{upName}\_subtitle_manifest.json 构建索引。
        /// 用于首次启动迁移已有数据，或用户手动删除索引文件后的自动重建。
        /// </summary>
        public static UpDownloadIndex BuildFromWorkingDir(string downloadRoot)
        {
            var idx = new UpDownloadIndex();
            if (string.IsNullOrEmpty(downloadRoot) || !Directory.Exists(downloadRoot))
            {
                return idx;
            }

            try
            {
                foreach (string subDir in Directory.GetDirectories(downloadRoot))
                {
                    string manifestPath = Path.Combine(subDir, SubtitleBatchManifestStore.FileName);
                    if (!File.Exists(manifestPath)) { continue; }

                    SubtitleBatchManifest m;
                    try { m = SubtitleBatchManifestStore.Load(subDir); }
                    catch (Exception e) { LogManager.Error(Tag, e); continue; }
                    if (m == null) { continue; }

                    int doneCount = 0;
                    if (m.Items != null)
                    {
                        foreach (var it in m.Items)
                        {
                            if (it != null && it.Status == "done") { doneCount++; }
                        }
                    }

                    idx.Items.Add(new UpDownloadIndexEntry
                    {
                        Mid = m.Mid,
                        UpName = m.UpName,
                        LastUpdate = m.UpdatedAt,
                        DoneCount = doneCount,
                        TotalCount = m.Items?.Count ?? 0
                    });
                }
            }
            catch (Exception e)
            {
                LogManager.Error(Tag, e);
            }
            return idx;
        }
    }
}
