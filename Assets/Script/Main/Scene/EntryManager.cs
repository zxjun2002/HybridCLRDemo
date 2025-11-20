using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HybridCLR;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Main
{
    public class EntryManager : MonoBehaviour
    {
        // Addressables中资源的标签和引用
        public AssetLabelReference hotUpdateDllLabelRef; // 热更DLL标签
        public AssetLabelReference aotMetadataDllLabelRef; // AOT元数据DLL标签
        public AssetReference hotUpdateMainSceneRef; // 热更主场景
        
        // 记录加载过的热更程序集，用于后续 RuntimeInit 扫描
        private readonly List<Assembly> _hotfixAssemblies = new List<Assembly>();

        // 热更入口 从这里开始
        private void Start()
        {
            //在Start中调用执行更新检查的任务
            _check_update().Forget();
        }

        private async UniTask _check_update()
        {
            //检查资源更新任务
            await _update_address_ables();
            //加载AOT元数据DLL任务
            await _load_meta_data_for_aot_dlls();
            //加载热更DLL任务
            await _load_hotfix_dlls();
            //进入热更主场景任务
            await _enter_hotfix_main_scene();
        }

        private async UniTask _update_address_ables()
        {
            // 初始化Addressables
            await Addressables.InitializeAsync();

            // 检查文件更新
            // 这一步会根据Addressables中的资源组来依次检查更新
            // 打包后 会 从配置中的RemoteBuildPath中下载资源
            // Addressables 会自动根据catalog中各个资源的hash值来判断是否需要更新
            List<string> catalogs = await Addressables.CheckForCatalogUpdates();

            if (catalogs.Count <= 0)
            {
                //没有需要更新的资源
                Debug.Log("没有需要更新的资源");
                return;
            }

            //需要更新资源  则 根据catalogs 拿到需要更新的资源位置 
            List<IResourceLocator> resourceLocators = await Addressables.UpdateCatalogs(catalogs);
            Debug.Log($"需要更新:{resourceLocators.Count}个资源");

            foreach (IResourceLocator resourceLocator in resourceLocators)
            {
                Debug.Log($"开始下载资源:{resourceLocator}");
                await _download(resourceLocator);
                Debug.Log($"下载资源:{resourceLocator}完成");
            }
        }

        private async UniTask _download(IResourceLocator resourceLocator)
        {
            var size = await Addressables.GetDownloadSizeAsync(resourceLocator.Keys);
            Debug.Log($"更新:{resourceLocator}资源,总大小:{size}");
            if (size <= 0) return;
            var downloadHandle =
                Addressables.DownloadDependenciesAsync(resourceLocator.Keys, Addressables.MergeMode.Union);
            float progress = 0;
            while (downloadHandle.Status == AsyncOperationStatus.None)
            {
                float percentageComplete = downloadHandle.GetDownloadStatus().Percent;
                if (percentageComplete > progress * 1.01) // Report at most every 10% or so
                {
                    progress = percentageComplete; // More accurate %
                    print($"下载百分比：{progress * 100}%");
                }

                await UniTask.WaitForFixedUpdate();
            }

            await downloadHandle;

            Debug.Log("更新完毕!");
            Addressables.Release(downloadHandle);
        }

        private async UniTask _load_hotfix_dlls()
        {
            // 加载热更DLL
            // 这里使用标签来加载资源 Addressables会自动根据标签来加载所有资源
            var dlls = await Addressables.LoadAssetsAsync<TextAsset>(hotUpdateDllLabelRef, null);
            foreach (var asset in dlls)
            {
                Debug.Log("加载热更DLL:" + asset.name);
                var asm = Assembly.Load(asset.bytes);
                Debug.Log("加载热更DLL:" + asset.name + "完成");
                _hotfixAssemblies.Add(asm);
            }
            
            // 在 IL2CPP Player 中手动执行热更里的 RuntimeInitializeOnLoadMethod
#if !UNITY_EDITOR
            _invoke_hotfix_runtime_initialize_methods();
#endif
        }
        
        /// <summary>
        /// 扫描热更程序集里的 [RuntimeInitializeOnLoadMethod] 静态方法，
        /// 额外读取 [HotfixInitOrder] 做排序，然后依次执行。
        /// 只在 Player（非编辑器）下调用，避免与 Unity 自己的调用重复。
        /// </summary>
        private void _invoke_hotfix_runtime_initialize_methods()
        {
            var runtimeInitAttrType = typeof(RuntimeInitializeOnLoadMethodAttribute);
            var orderAttrType       = typeof(HotfixInitOrderAttribute);

            var initMethods = new List<(int order, RuntimeInitializeLoadType loadType, MethodInfo method)>();

            foreach (var asm in _hotfixAssemblies)
            {
                if (asm == null) continue; 

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }

                foreach (var type in types)
                {
                    var methods = type.GetMethods(
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                    foreach (var method in methods)
                    {
                        if (method.GetParameters().Length != 0)  // 只要无参
                            continue;
                        if (method.IsGenericMethodDefinition)    // 不支持开放泛型方法
                            continue;

                        // 是否带 RuntimeInitializeOnLoadMethod
                        var initAttrs = (RuntimeInitializeOnLoadMethodAttribute[])
                            method.GetCustomAttributes(runtimeInitAttrType, inherit: false);
                        if (initAttrs == null || initAttrs.Length == 0)
                            continue;

                        // 读取我们自己的排序 Attribute（可选）
                        int order = 0;
                        var orderAttr = (HotfixInitOrderAttribute)
                            method.GetCustomAttribute(orderAttrType, inherit: false);
                        if (orderAttr != null)
                        {
                            order = orderAttr.Order;
                        }

                        foreach (var initAttr in initAttrs)
                        {
                            initMethods.Add((order, initAttr.loadType, method));
                        }
                    }
                }
            }

            // 先按 order 排，再按 loadType / 名称兜个稳定顺序
            initMethods.Sort((a, b) =>
            {
                int cmp = a.order.CompareTo(b.order);
                if (cmp != 0) return cmp;

                cmp = a.loadType.CompareTo(b.loadType);
                if (cmp != 0) return cmp;

                string an = a.method.DeclaringType.FullName + "." + a.method.Name;
                string bn = b.method.DeclaringType.FullName + "." + b.method.Name;
                return string.Compare(an, bn, StringComparison.Ordinal);
            });

            foreach (var (order, loadType, method) in initMethods)
            {
                try
                {
                    Debug.Log(
                        $"[HotfixInit] Invoke (order={order}, loadType={loadType}) {method.DeclaringType.FullName}.{method.Name}()");
                    method.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[HotfixInit] 调用 {method.DeclaringType.FullName}.{method.Name} 失败：{ex}");
                }
            }
        }
        
        private async UniTask _load_meta_data_for_aot_dlls()
        {
            // 这一步实际上是为了解决 AOT 泛型类的问题
            HomologousImageMode mode = HomologousImageMode.SuperSet;

            // 如果当前没有需要补元数据的 AOT 程序集，直接返回
            if (AOTGenericReferences.PatchedAOTAssemblyList == null ||
                AOTGenericReferences.PatchedAOTAssemblyList.Count == 0)
            {
                Debug.Log("AOTGenericReferences.PatchedAOTAssemblyList 为空，本次不补元数据");
                return;
            }

            // 做一个快查表
            var needPatch = new HashSet<string>(AOTGenericReferences.PatchedAOTAssemblyList);

            // 加载 Addressables 中标记为 AOT 元数据的所有 TextAsset
            var aotAssets = await Addressables.LoadAssetsAsync<TextAsset>(aotMetadataDllLabelRef, null);

            foreach (var asset in aotAssets)
            {
                // 统一成 xxx.dll 的形式和 PatchedAOTAssemblyList 对上
                string dllName = asset.name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? asset.name
                    : asset.name + ".dll";

                // 不在列表里的直接跳过
                if (!needPatch.Contains(dllName))
                {
                    continue;
                }

                LoadImageErrorCode errorCode = RuntimeApi.LoadMetadataForAOTAssembly(asset.bytes, mode);
                if (errorCode != LoadImageErrorCode.OK)
                {
                    Debug.LogError($"加载AOT元数据DLL:{dllName}失败,错误码:{errorCode}");
                }
            }
        }

        private async UniTask _enter_hotfix_main_scene()
        {
            // 等待用户输入
            await _wait_for_enter_input();
            // 加载热更主场景
            var scene = await Addressables.LoadSceneAsync(hotUpdateMainSceneRef);
            // 激活场景
            await scene.ActivateAsync();
        }

        private async UniTask _wait_for_enter_input()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsPlayer:
                    while (!Input.GetKey(KeyCode.Space))
                    {
                        await UniTask.WaitForFixedUpdate();
                    }
                    break;
                case RuntimePlatform.Android:
                    while (Input.touchCount == 0)
                    {
                        await UniTask.WaitForFixedUpdate();
                    }
                    break;
            }
        }
    }
}