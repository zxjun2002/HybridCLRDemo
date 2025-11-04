#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public class HotUpdateBytesPostprocessor : AssetPostprocessor
{
    static HotUpdateBytesSettings cfg;

    [InitializeOnLoadMethod]
    static void LoadCfg()
    {
        var guids = AssetDatabase.FindAssets("t:HotUpdateBytesSettings");
        if (guids.Length > 0)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            cfg = AssetDatabase.LoadAssetAtPath<HotUpdateBytesSettings>(path);
        }
    }

    enum Slot { None, Hot, AOT }

    static void OnPostprocessAllAssets(
        string[] imported, string[] deleted, string[] movedTo, string[] movedFrom)
    {
        if (cfg == null) return;

        // 1) 新导入/覆盖导入
        if (imported != null)
        {
            foreach (var path in imported)
            {
                if (Skip(path)) continue;
                HandleByLocation(path); // 根据所在目录决定标签/组
            }
        }

        // 2) 处理“移动到/从哪来”（成对数组）
        if (movedTo != null && movedFrom != null)
        {
            for (int i = 0; i < movedTo.Length; i++)
            {
                var newPath = movedTo[i];
                var oldPath = movedFrom[i];
                if (Skip(newPath)) continue;

                var oldSlot = Where(oldPath);
                var newSlot = Where(newPath);

                // 从 Hot <-> AOT 互相移动：改成 .bytes，换组，打新标签并移除旧标签
                if (newSlot == Slot.Hot || newSlot == Slot.AOT)
                {
                    EnsureBytes(ref newPath);
                    ApplyAddressables(newPath, newSlot, cleanupOppositeLabel: true);
                }
                else
                {
                    // 移出受管目录：为了安全，移除两种标签，保留所在组（或可选移到默认组）
                    CleanupAllLabels(newPath);
                }
            }
        }
    }

    // —— 依据当前所在目录处理（导入时用）——
    static void HandleByLocation(string path)
    {
        var slot = Where(path);
        if (slot == Slot.None) return;

        EnsureBytes(ref path);
        ApplyAddressables(path, slot, cleanupOppositeLabel: false);
    }

    // 判定在热更/AOT/外部
    static Slot Where(string path)
    {
        if (string.IsNullOrEmpty(path)) return Slot.None;
        var hotDir = cfg.HotDir;
        var aotDir = cfg.AotDir;
        if (!string.IsNullOrEmpty(hotDir) && path.StartsWith(hotDir, StringComparison.Ordinal)) return Slot.Hot;
        if (!string.IsNullOrEmpty(aotDir) && path.StartsWith(aotDir, StringComparison.Ordinal)) return Slot.AOT;
        return Slot.None;
    }

    static bool Skip(string path)
    {
        if (string.IsNullOrEmpty(path)) return true;
        if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return true;
        if (AssetDatabase.IsValidFolder(path)) return true;
        return false;
    }

    // 确保 .bytes（避免被当插件）
    static void EnsureBytes(ref string assetPath)
    {
        if (assetPath.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase)) return;

        var newPath = assetPath + ".bytes";
        var err = AssetDatabase.MoveAsset(assetPath, newPath);
        if (!string.IsNullOrEmpty(err))
        {
            // 已存在同名 .bytes → 覆盖
            if (File.Exists(newPath))
            {
                FileUtil.ReplaceFile(assetPath, newPath);
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.ImportAsset(newPath);
            }
            else
            {
                Debug.LogError($"MoveAsset failed: {err} ({assetPath} -> {newPath})");
                return;
            }
        }
        assetPath = newPath;
    }

    // 把资源放进目标组 + 打目标标签（可选移除对立标签）
    static void ApplyAddressables(string assetPath, Slot slot, bool cleanupOppositeLabel)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogWarning("Addressables Settings 未初始化。请先打开 Window > Asset Management > Addressables > Groups。");
            return;
        }

        AddressableAssetGroup group = null;
        string keepLabel = null, removeLabel = null;

        if (slot == Slot.Hot)
        {
            group = cfg.hotGroup ?? settings.DefaultGroup;
            keepLabel = cfg.hotUpdateLabel.labelString;
            removeLabel = cfg.aotMetadataLabel.labelString;
        }
        else if (slot == Slot.AOT)
        {
            group = cfg.aotGroup ?? settings.DefaultGroup;
            keepLabel = cfg.aotMetadataLabel.labelString;
            removeLabel = cfg.hotUpdateLabel.labelString;
        }

        if (group == null) group = settings.DefaultGroup;
        if (!string.IsNullOrEmpty(keepLabel)) settings.AddLabel(keepLabel);

        var guid = AssetDatabase.AssetPathToGUID(assetPath);
        var entry = settings.CreateOrMoveEntry(guid, group);

        if (!string.IsNullOrEmpty(keepLabel))
            entry.SetLabel(keepLabel, true, true);

        if (cleanupOppositeLabel && !string.IsNullOrEmpty(removeLabel))
            entry.SetLabel(removeLabel, false, true); // 取消对立标签

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true, false);
    }

    // 移出受管目录：把两种标签都去掉，避免误加载
    static void CleanupAllLabels(string assetPath)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;

        var guid = AssetDatabase.AssetPathToGUID(assetPath);
        var entry = settings.FindAssetEntry(guid);
        if (entry == null) return;

        var hotLabel = cfg.hotUpdateLabel.labelString;
        var aotLabel = cfg.aotMetadataLabel.labelString;

        if (!string.IsNullOrEmpty(hotLabel)) entry.SetLabel(hotLabel, false, true);
        if (!string.IsNullOrEmpty(aotLabel)) entry.SetLabel(aotLabel, false, true);

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true, false);
    }
}
#endif
