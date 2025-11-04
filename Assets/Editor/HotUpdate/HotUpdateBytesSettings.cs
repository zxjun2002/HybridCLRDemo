using UnityEngine;
using UnityEngine.AddressableAssets;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets.Settings; // AddressableAssetGroup
#endif

[CreateAssetMenu(fileName = "HotUpdateBytesSettings",
    menuName = "HybridCLR/HotUpdate Bytes Settings")]
public class HotUpdateBytesSettings : ScriptableObject
{
#if UNITY_EDITOR
    [Header("监控目录（直接拖 Project 面板中的文件夹）")]
    public DefaultAsset hotWatchFolder; // 热更 DLL 投递口
    public DefaultAsset aotWatchFolder; // AOT 元数据投递口

    [Header("Addressables Group（可留空=默认组）")]
    public AddressableAssetGroup hotGroup;
    public AddressableAssetGroup aotGroup;
#endif

    [Header("下拉选择标签（AssetLabelReference）")]
    public AssetLabelReference hotUpdateLabel;    // 例如 hotUpdateDll
    public AssetLabelReference aotMetadataLabel;  // 例如 aot

#if UNITY_EDITOR
    public string HotDir => GetPath(hotWatchFolder);
    public string AotDir  => GetPath(aotWatchFolder);

    static string GetPath(DefaultAsset folder)
    {
        if (!folder) return null;
        var p = AssetDatabase.GetAssetPath(folder);
        return AssetDatabase.IsValidFolder(p) ? p.Replace('\\', '/') : null;
    }
#endif
}
