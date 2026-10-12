using UnityEditor;

// Importa as texturas de Assets/Art/Mapa/ sem alterar os pixels (sem redimensionar, comprimir ou filtrar)
public class MapaTexturaPostprocessor : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        string caminho = assetPath.Replace('\\', '/').ToLowerInvariant();
        if (!caminho.StartsWith("assets/art/mapa/")) return;

        var importador = (TextureImporter)assetImporter;
        importador.isReadable = true;
        importador.npotScale = TextureImporterNPOTScale.None;
        importador.textureCompression = TextureImporterCompression.Uncompressed;
        importador.filterMode = UnityEngine.FilterMode.Point;
        importador.mipmapEnabled = false;
        importador.maxTextureSize = 8192;
    }
}
