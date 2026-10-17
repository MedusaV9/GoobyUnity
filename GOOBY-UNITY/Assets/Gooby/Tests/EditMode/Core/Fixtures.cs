using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Gooby.Tests.Core
{
    /// <summary>Locates Assets/Gooby/Tests/Fixtures from any working directory (dotnet test or Unity EditMode).</summary>
    public static class Fixtures
    {
        private static string _root;

        public static string Root
        {
            get
            {
                if (_root != null) return _root;
                var candidates = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
                foreach (var start in candidates)
                {
                    var dir = new DirectoryInfo(start);
                    while (dir != null)
                    {
                        var probe = System.IO.Path.Combine(dir.FullName, "Assets", "Gooby", "Tests", "Fixtures");
                        if (Directory.Exists(probe)) return _root = probe;
                        dir = dir.Parent;
                    }
                }
                throw new DirectoryNotFoundException("Assets/Gooby/Tests/Fixtures not found above " + string.Join(", ", candidates));
            }
        }

        /// <summary>Assets/Gooby/Content/Resources/gooby — the content root the ContentSource reads from.</summary>
        public static string ResourcesRoot => System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, "..", "..", "Content", "Resources", "gooby"));

        /// <summary>Installs a file-system ContentSource over the shipped Resources (Resources semantics: no extension).</summary>
        public static void InstallContentSource()
        {
            Gooby.Core.Content.ContentSource.Install(rel =>
            {
                var path = System.IO.Path.Combine(ResourcesRoot, rel + ".json");
                return File.Exists(path) ? File.ReadAllText(path) : null;
            });
        }

        public static string Path(string relative) => System.IO.Path.Combine(Root, relative);

        public static JObject LoadObject(string relative) => JObject.Parse(File.ReadAllText(Path(relative)));

        public static JObject Golden() => LoadObject("golden_values.json");
    }
}
