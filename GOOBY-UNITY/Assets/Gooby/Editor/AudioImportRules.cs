using UnityEditor;
using UnityEngine;

namespace Gooby.Editor
{
    /// <summary>
    /// Import settings of the game audio by folder (Resources/gooby-audio/…): music streams from disk (Vorbis, no
    /// preload — 56 tracks would otherwise sit decoded in memory), the ambience beds stay compressed in memory
    /// (seekable loops), Gooby's voice syllables are tiny and decode on load. The SFX folder keeps its own settings.
    /// </summary>
    public sealed class AudioImportRules : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            var path = assetPath.Replace('\\', '/');
            const string root = "/Resources/gooby-audio/";
            var at = path.IndexOf(root, System.StringComparison.Ordinal);
            if (at < 0) return;
            var folder = path.Substring(at + root.Length);
            var importer = (AudioImporter)assetImporter;
            var s = importer.defaultSampleSettings;
            if (folder.StartsWith("music/"))
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.5f;
                s.preloadAudioData = false;
                importer.loadInBackground = true;
            }
            else if (folder.StartsWith("ambient/"))
            {
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.45f;
                s.preloadAudioData = false;
                importer.loadInBackground = true;
            }
            else if (folder.StartsWith("voice/"))
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.ADPCM;
                importer.forceToMono = true;
            }
            else return;
            importer.defaultSampleSettings = s;
        }
    }
}
