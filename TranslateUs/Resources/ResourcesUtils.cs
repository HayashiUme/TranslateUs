using System.Reflection;
using UnityEngine;

namespace TranslateUs.Resources
{
    public static class ResourcesUtils
    {
        private static readonly Dictionary<string, Sprite> CachedSprites = new();
        public static Sprite? LoadSpriteFromAssembly(string path, float pixelsPerUnit = 100f)
        {
            try
            {
                if (CachedSprites.TryGetValue(path + pixelsPerUnit, out var sprite))
                    return sprite;
                var texture = LoadTextureFromAssembly("TranslateUs.Resources." + path);
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), pixelsPerUnit);
                sprite.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontSaveInEditor;
                return CachedSprites[path + pixelsPerUnit] = sprite;
            }
            catch (System.Exception e)
            {
                Main.Logger.LogError($"Error while loading {path} ({pixelsPerUnit}): {e}");
                return null;
            }
        }
        private static Texture2D LoadTextureFromAssembly(string path)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path);
            var texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            using MemoryStream ms = new();
            stream?.CopyTo(ms);
            var succeed = texture.LoadImage(ms.ToArray(), false);
            if (!succeed) Main.Logger.LogError("Failed to load texture: " + path);
            return texture;
        }
    }
}
