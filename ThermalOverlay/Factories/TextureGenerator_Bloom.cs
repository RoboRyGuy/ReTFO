
using ReTFO.ThermalOverlay.Interfaces;
using UnityEngine;

namespace ReTFO.ThermalOverlay.Factories;

/// <summary>
/// Generates a red "bloom" texture, with full red at the center and fading to black at the edges.
/// Fading is shaped as either a circle or a square. You can also configure the size of the generated texture,
///  as well as the min and max values of the bloom texture.
/// Bloom([Square/Circle], [minRed, 0 to 1], [maxRed, 0 to 1], [textureSize, uint])
/// </summary>
public class TextureGenerator_Bloom : ITextureGenerator
{
    public const string Name = "Bloom";

    public virtual Texture GenerateTexture(string? thisName, ConversionContext context)
    {
        bool circle = true;
        float min = 0f;
        float max = 1f;
        int size = 256;
        string[] parameters = FactoryManager.GetParameters(thisName);
        if (parameters.Length > 0)
        {
            string item = parameters[0];
            if (item.Length == 0) { }
            else if (item == "Circle") circle = true;
            else if (item == "Square") circle = false;
            else context.Log.LogError($"TextureGenerator_Bloom expected either \"Circle\" or \"Square\" for its first parameter, but instead got \"{item}\"");
        }
        if (parameters.Length > 1)
        {
            string item = parameters[1];
            if (item.Length == 0) { }
            else if (float.TryParse(item, out float value)) min = value;
            else context.Log.LogError($"TextureGenerator_Bloom expected a float for its second parameter, but instead got \"{item}\"");
        }
        if (parameters.Length > 2)
        {
            string item = parameters[2];
            if (item.Length == 0) { }
            else if (float.TryParse(item, out float value)) max = value;
            else context.Log.LogError($"TextureGenerator_Bloom expected a float for its third parameter, but instead got \"{item}\"");
        }
        if (parameters.Length > 3)
        {
            string item = parameters[3];
            if (item.Length == 0) { }
            else if (uint.TryParse(item, out uint value)) size = (int)value;
            else context.Log.LogError($"TextureGenerator_Bloom expected a uint for its fourth parameter, but instead got \"{item}\"");
        }
        if (parameters.Length > 4)
            context.Log.LogWarning($"TextureGenerator_Bloom ignoring extra parameters: {FactoryManager.FormatParams(parameters[4..])}");

        float halfSize = .5f * (size - 1f);
        float inverseSize = 1f / halfSize;
        Texture2D texture = new(size, size, TextureFormat.RHalf, false);
        if (circle)
            texture.name = "Bloom - Circle";
        else
            texture.name = "Bloom - Square";

        for (int x = 0; x < size; x++)
        {
            float dx = x - halfSize;
            for (int y = 0; y < size; y++)
            {
                float dy = y - halfSize;
                float dist;
                if (circle) dist = Mathf.Sqrt(dx * dx + dy * dy) * inverseSize;
                else        dist = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) * inverseSize;
                dist = Mathf.Lerp(min, max, Mathf.Clamp(1 - dist, 0f, 1f));
                texture.SetPixel(x, y, new Color(dist, 0f, 0f));
            }
        }
        texture.Apply();

        return texture;
    }
}
