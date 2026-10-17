using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Loads the Blender-built GLBs (StreamingAssets/gooby/…) at runtime through glTFast. When the glTFast package
    /// is unavailable (compile-check builds) the loader reports "not available" and callers keep their procedural
    /// placeholders — the game never depends on the meshes to function.
    /// </summary>
    public static class AssetLoader
    {
        public static string Root => Path.Combine(Application.streamingAssetsPath, "gooby");
        public static string PropPath(string archetype) => Path.Combine(Root, "props", archetype + ".glb");
        public static string CharacterPath => Path.Combine(Root, "character", "gooby.glb");
        public static string VehiclePath(string name) => Path.Combine(Root, "vehicles", name + ".glb");
        public static string SceneryPath(string name) => Path.Combine(Root, "scenery", name + ".glb");

        private static JObject _manifest;

        /// <summary>props_manifest.json — catalog item id → archetype (built by tools/blender/build_props.py).</summary>
        public static JObject PropsManifest()
        {
            if (_manifest != null) return _manifest;
            try
            {
                var path = Path.Combine(Root, "props", "props_manifest.json");
                _manifest = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[assets] props manifest unreadable: " + e.Message);
                _manifest = new JObject();
            }
            return _manifest;
        }

        public static string ArchetypeFor(string itemId)
        {
            var items = PropsManifest()["items"] as JObject;
            return items != null ? J.Str(items[itemId]) : "";
        }

        public static bool Available
        {
            get
            {
#if GLTFAST
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Instantiates a GLB under parent; returns null when glTFast is missing or the file fails to load.</summary>
        public static async Task<GameObject> InstantiateAsync(string path, Transform parent, bool legacyAnimation = false)
        {
            if (!File.Exists(path)) return null;
#if GLTFAST
            try
            {
                var settings = new GLTFast.ImportSettings { AnimationMethod = legacyAnimation ? GLTFast.AnimationMethod.Legacy : GLTFast.AnimationMethod.None };
                var gltf = new GLTFast.GltfImport();
                if (!await gltf.Load(new Uri("file://" + path), settings))
                {
                    Debug.LogWarning("[assets] glTF load failed: " + path);
                    return null;
                }
                var go = new GameObject(Path.GetFileNameWithoutExtension(path));
                go.transform.SetParent(parent, false);
                if (!await gltf.InstantiateMainSceneAsync(go.transform))
                {
                    Debug.LogWarning("[assets] glTF instantiate failed: " + path);
                    UnityEngine.Object.Destroy(go);
                    return null;
                }
                Debug.Log("[assets] loaded " + Path.GetFileName(path) + " renderers=" + go.GetComponentsInChildren<Renderer>().Length);
                return go;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[assets] glTF load failed for " + path + ": " + e.Message);
                return null;
            }
#else
            await Task.CompletedTask;
            return null;
#endif
        }

        private static readonly Dictionary<string, Task<GameObject>> TemplateCache = new Dictionary<string, Task<GameObject>>();

        /// <summary>Loads (and caches a hidden template of) a prop archetype, returning a fresh clone.</summary>
        public static Task<GameObject> PropInstanceAsync(string archetype, Transform parent) =>
            string.IsNullOrEmpty(archetype) ? Task.FromResult<GameObject>(null) : InstanceAsync(PropPath(archetype), parent, "Prop:" + archetype);

        /// <summary>Clone of a scenery GLB (tools/blender/build_scenery.py: houses, trees, fences, signs …).</summary>
        public static Task<GameObject> SceneryInstanceAsync(string name, Transform parent) =>
            InstanceAsync(SceneryPath(name), parent, "Scenery:" + name);

        /// <summary>
        /// Every GLB is imported once into a hidden template; instances are plain clones, so a street of forty
        /// houses costs one glTF parse. Returns null when the parent vanished while loading.
        /// </summary>
        public static async Task<GameObject> InstanceAsync(string path, Transform parent, string name, bool legacyAnimation = false)
        {
            if (!Available) return null;
            var template = await TemplateAsync(path, legacyAnimation);
            if (template == null || parent == null) return null;
            var clone = UnityEngine.Object.Instantiate(template, parent, false);
            clone.SetActive(true);
            clone.name = name;
            return clone;
        }

        /// <summary>Cached hidden template for a GLB path (starts the import on first use); the animated character keeps its own template.</summary>
        public static Task<GameObject> TemplateAsync(string path, bool legacyAnimation = false)
        {
            var key = legacyAnimation ? path + "#anim" : path;
            if (!TemplateCache.TryGetValue(key, out var task))
            {
                task = LoadTemplate(path, legacyAnimation);
                TemplateCache[key] = task;
            }
            return task;
        }

        /// <summary>Imports the rigged Gooby once (loading screen warm-up): the player's avatar and every passer-by are clones of it.</summary>
        public static void WarmUpCharacter()
        {
            if (Available && File.Exists(CharacterPath)) _ = TemplateAsync(CharacterPath, true);
        }

        /// <summary>Imports every GLB of a StreamingAssets folder into the template cache (loading screen warm-up).</summary>
        public static void WarmUp(string folder)
        {
            if (!Available || !Directory.Exists(folder)) return;
            foreach (var path in Directory.GetFiles(folder, "*.glb")) _ = TemplateAsync(path);
        }

        /// <summary>Templates started so far (warm-up and on-demand).</summary>
        public static int TemplatesStarted => TemplateCache.Count;

        /// <summary>Templates still importing — the loading card waits for zero, so the car and the town do not pop in later.</summary>
        public static int TemplatesPending
        {
            get
            {
                var n = 0;
                foreach (var t in TemplateCache.Values) if (!t.IsCompleted) n++;
                return n;
            }
        }

        private static async Task<GameObject> LoadTemplate(string path, bool legacyAnimation = false)
        {
            var holder = GameObject.Find("PropTemplates") ?? new GameObject("PropTemplates");
            UnityEngine.Object.DontDestroyOnLoad(holder);
            var go = await InstantiateAsync(path, holder.transform, legacyAnimation);
            if (go != null)
            {
                // surfaces of the new look (grain + relief per palette material) once per template; clones share them
                MaterialDresser.Dress(go, MaterialDresser.KindOf(path));
                go.SetActive(false);
            }
            return go;
        }
    }
}
