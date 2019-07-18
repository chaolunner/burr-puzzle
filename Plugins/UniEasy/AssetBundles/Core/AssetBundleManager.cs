﻿using System.Collections.Generic;
using System.Collections;
using UnityEngine;

namespace UniEasy
{
    public class AssetBundleManager : MonoBehaviour
    {
        private static AssetBundleManager instance;
        private ulong contentBytes;
        private ulong downloadedBytes;
        private float downloadProgress;
        private Dictionary<string, MultiABLoader> container = new Dictionary<string, MultiABLoader>();
        private Dictionary<string, ABDownloader> downloadContainer = new Dictionary<string, ABDownloader>();
        private AssetBundleManifest manifest = null;

        public static AssetBundleManager GetInstance()
        {
            if (instance == null)
            {
                instance = new GameObject("AssetBundleManager").AddComponent<AssetBundleManager>();
            }
            return instance;
        }

        /// <param name="unit">0=byte, 1=KB, 2=MB, 3=GB</param>
        public float GetContentSize(int unit = 1)
        {
            return contentBytes / Mathf.Pow(1024f, unit);
        }

        /// <param name="unit">0=byte, 1=KB, 2=MB, 3=GB</param>
        public float GetDownloadedSize(int unit = 1)
        {
            return downloadedBytes / Mathf.Pow(1024f, unit);
        }

        public int GetDownloadProgress()
        {
            return Mathf.RoundToInt(downloadProgress * 100);
        }

        void Awake()
        {
            StartCoroutine(ABManifestLoader.GetInstance().LoadMainifestFile());
        }

        private IEnumerator WaitUntilMainifestLoad()
        {
            while (!ABManifestLoader.GetInstance().IsLoadCompleted)
            {
                yield return null;
            }

            manifest = ABManifestLoader.GetInstance().GetABManifest();
            if (manifest == null)
            {
                Debug.LogError(GetType() + "/LoadAssetBundle()/manifest(field) is null, please make sure manifest file loaded first!");
                yield return null;
            }
        }

        public IEnumerator DownloadAssetBundle()
        {
            yield return StartCoroutine(WaitUntilMainifestLoad());

            foreach (var abName in manifest.GetAllAssetBundles())
            {
                var sceneName = abName.Substring(0, abName.IndexOf("/"));
                var abHash = manifest.GetAssetBundleHash(abName);
                var downloader = new ABDownloader(abName, abHash);
                downloadContainer.Add(abName, downloader);
                StartCoroutine(downloader.LoadAssetBundle());
            }
            downloadProgress = 0;
            while (downloadProgress < 1)
            {
                contentBytes = 0;
                downloadedBytes = 0;
                var progress = 0f;
                foreach (var downloader in downloadContainer.Values)
                {
                    contentBytes += downloader.ContentBytes;
                    downloadedBytes += downloader.DownloadedBytes;
                    progress += downloader.DownloadProgress;
                }
                downloadProgress = progress / downloadContainer.Count;
                yield return null;
            }
            downloadContainer.Clear();
            Resources.UnloadUnusedAssets();
            System.GC.Collect();
        }

        public IEnumerator LoadAssetBundle(string sceneName, string abName, ABLoadStart onLoadStart, ABLoadCompleted onLoadCompleted)
        {
            if (string.IsNullOrEmpty(sceneName) || string.IsNullOrEmpty(abName))
            {
                Debug.LogError(GetType() + "/LoadAssetBundle()/sceneName or abName is null, please check it!");
                yield return null;
            }

            yield return StartCoroutine(WaitUntilMainifestLoad());

            if (!container.ContainsKey(sceneName))
            {
                container.Add(sceneName, new MultiABLoader(sceneName, abName, manifest, onLoadCompleted));
            }

            var loader = container[sceneName];
            if (loader == null)
            {
                Debug.LogError(GetType() + "/LoadAssetBundle()/MultiABLoader is null, please check it!");
            }
            yield return loader.LoadAssetBundleByRecursive(abName);
        }

        public Object LoadAsset(string sceneName, string abName, string assetName, bool isCache)
        {
            if (container.ContainsKey(sceneName))
            {
                return container[sceneName].LoadAsset(abName, assetName, isCache);
            }
            Debug.LogError(GetType() + "/LoadAsset()/can't found the scene, can't load assets in the bundle, please check it! sceneName=" + sceneName);
            return null;
        }

        public void Dispose(string sceneName)
        {
            if (container.ContainsKey(sceneName))
            {
                container[sceneName].Dispose();
            }
            else
            {
                Debug.LogError(GetType() + "/Dispose()/can't found the scene, can't dispose assets in the bundle, please check it! sceneName=" + sceneName);
            }
        }
    }
}