using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Converts the AI-authored Tide asset sheet into independent transparent sprites.
/// The generated source is preserved verbatim; this tool only performs deterministic
/// chroma removal, trimming, and import setup so gameplay code can compose each object.
/// </summary>
public static class TideFormalAssetSheetImporter
{
    private const string FormalSourcePath = "Assets/Art/GeneratedAI/TideFormalAssetSheetV1.png";
    private const string EnvironmentSourcePath = "Assets/Art/GeneratedAI/TideEnvironmentAssetSheetV1.png";
    private const string OutputFolder = "Assets/Resources/StiltFirstSliceAI";

    private static readonly string[,] FormalOutputNames =
    {
        { "AIStiltHouse", "AISailboat", "AIPlayer", "AINet" },
        { "AIShipwreck", "AILighthouse", "AIMoon", "AICloudBank" }
    };

    private static readonly string[,] EnvironmentOutputNames =
    {
        { "AIDaySeaSky", "AINightSeaSky", "AIWaterSurface", "AIVortex" },
        { "AIWetBoardwalk", "AIHarvestFish", "AIRepairTimber", "AIRouteRelic" }
    };

    [MenuItem("Tide/Import Formal AI Asset Sheet")]
    public static void Import()
    {
        ImportSheet(FormalSourcePath, FormalOutputNames);
        ImportSheet(EnvironmentSourcePath, EnvironmentOutputNames);
        Debug.Log("Imported sixteen formal Tide AI sprites into " + OutputFolder + ".");
    }

    private static void ImportSheet(string sourcePath, string[,] outputNames)
    {
        TextureImporter sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
        if (sourceImporter == null)
        {
            throw new FileNotFoundException("Formal Tide asset sheet is missing.", sourcePath);
        }

        ConfigureReadableSource(sourceImporter);
        Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
        if (source == null)
        {
            throw new InvalidOperationException("Unity could not read the formal Tide asset sheet: " + sourcePath);
        }

        List<Vector2Int> verticalSeparators = FindSeparatorRuns(source, true);
        List<Vector2Int> horizontalSeparators = FindSeparatorRuns(source, false);
        if (verticalSeparators.Count != 3 || horizontalSeparators.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected a 4x2 sheet, found {verticalSeparators.Count} vertical and " +
                $"{horizontalSeparators.Count} horizontal separator runs.");
        }

        Directory.CreateDirectory(OutputFolder);
        int[] xMin =
        {
            0,
            verticalSeparators[0].y + 1,
            verticalSeparators[1].y + 1,
            verticalSeparators[2].y + 1
        };
        int[] xMax =
        {
            verticalSeparators[0].x - 1,
            verticalSeparators[1].x - 1,
            verticalSeparators[2].x - 1,
            source.width - 1
        };

        // Texture coordinates start at the bottom-left. outputNames uses the visual
        // top row first, so row bounds are intentionally reversed here.
        Vector2Int horizontal = horizontalSeparators[0];
        int[] yMin = { horizontal.y + 1, 0 };
        int[] yMax = { source.height - 1, horizontal.x - 1 };

        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                Texture2D cutout = ExtractCutout(source, xMin[column], xMax[column], yMin[row], yMax[row]);
                string outputPath = $"{OutputFolder}/{outputNames[row, column]}.png";
                File.WriteAllBytes(outputPath, cutout.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(cutout);
            }
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureOutputSprites(outputNames);
    }

    private static void ConfigureReadableSource(TextureImporter importer)
    {
        bool changed = importer.textureType != TextureImporterType.Default ||
            !importer.isReadable ||
            importer.textureCompression != TextureImporterCompression.Uncompressed ||
            importer.mipmapEnabled;

        importer.textureType = TextureImporterType.Default;
        importer.isReadable = true;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 4096;
        if (changed)
        {
            importer.SaveAndReimport();
        }
    }

    private static void ConfigureOutputSprites(string[,] outputNames)
    {
        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                string path = $"{OutputFolder}/{outputNames[row, column]}.png";
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.spritePivot = new Vector2(0.5f, 0.5f);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                // Chroma-keyed edges are especially vulnerable to block compression;
                // keep these eight small cutouts uncompressed to preserve clean alpha.
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
        }
    }

    private static List<Vector2Int> FindSeparatorRuns(Texture2D source, bool vertical)
    {
        int lineCount = vertical ? source.width : source.height;
        int sampleCount = vertical ? source.height : source.width;
        List<Vector2Int> runs = new List<Vector2Int>();
        int runStart = -1;

        for (int line = 0; line < lineCount; line++)
        {
            int whiteSamples = 0;
            for (int sample = 0; sample < sampleCount; sample += 2)
            {
                Color32 pixel = vertical ? source.GetPixel(line, sample) : source.GetPixel(sample, line);
                if (pixel.r > 245 && pixel.g > 245 && pixel.b > 245)
                {
                    whiteSamples++;
                }
            }

            int testedSamples = Mathf.CeilToInt(sampleCount / 2f);
            bool separator = whiteSamples >= testedSamples * 0.92f;
            if (separator && runStart < 0)
            {
                runStart = line;
            }
            else if (!separator && runStart >= 0)
            {
                runs.Add(new Vector2Int(runStart, line - 1));
                runStart = -1;
            }
        }

        if (runStart >= 0)
        {
            runs.Add(new Vector2Int(runStart, lineCount - 1));
        }

        // Antialiasing can split one white gutter into several one-pixel runs.
        // Merge runs separated by at most two pixels before interpreting the grid.
        List<Vector2Int> mergedRuns = new List<Vector2Int>();
        for (int i = 0; i < runs.Count; i++)
        {
            Vector2Int run = runs[i];
            if (mergedRuns.Count == 0 || run.x - mergedRuns[mergedRuns.Count - 1].y > 3)
            {
                mergedRuns.Add(run);
                continue;
            }

            Vector2Int previous = mergedRuns[mergedRuns.Count - 1];
            mergedRuns[mergedRuns.Count - 1] = new Vector2Int(previous.x, run.y);
        }

        return mergedRuns;
    }

    private static Texture2D ExtractCutout(Texture2D source, int xMin, int xMax, int yMin, int yMax)
    {
        int width = xMax - xMin + 1;
        int height = yMax - yMin + 1;
        Color32[] pixels = source.GetPixels32();
        Color32[] keyed = new Color32[width * height];
        int visibleMinX = width;
        int visibleMinY = height;
        int visibleMaxX = -1;
        int visibleMaxY = -1;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color32 pixel = pixels[(yMin + y) * source.width + xMin + x];
                // The generator antialiases pure magenta into pale pink near the
                // object and white gutters. A ratio test removes that spill while
                // preserving rust red, amber windows, gray moon rock, and cream sail.
                bool border = x < 5 || y < 5 || x >= width - 5 || y >= height - 5;
                int magentaFloor = Mathf.Min(pixel.r, pixel.b);
                bool chroma = pixel.r > 48 && pixel.b > 48 && magentaFloor > pixel.g + 9;
                if (border || chroma)
                {
                    // Transparent magenta still bleeds through bilinear filtering.
                    // Clearing RGB as well as alpha prevents a purple fringe in Game view.
                    pixel = new Color32(0, 0, 0, 0);
                }
                else
                {
                    // Generated cutouts can retain a thin magenta fringe. Lowering
                    // only the unsupported red/blue excess keeps rust and warm light.
                    int shared = Mathf.Max(pixel.g, Mathf.Min(pixel.r, pixel.b));
                    if (pixel.r > pixel.g * 2 && pixel.b > pixel.g * 2)
                    {
                        pixel.r = (byte)Mathf.Min(pixel.r, shared + 24);
                        pixel.b = (byte)Mathf.Min(pixel.b, shared + 24);
                    }

                    pixel.a = 255;
                    visibleMinX = Mathf.Min(visibleMinX, x);
                    visibleMinY = Mathf.Min(visibleMinY, y);
                    visibleMaxX = Mathf.Max(visibleMaxX, x);
                    visibleMaxY = Mathf.Max(visibleMaxY, y);
                }

                keyed[y * width + x] = pixel;
            }
        }

        if (visibleMaxX < visibleMinX || visibleMaxY < visibleMinY)
        {
            throw new InvalidOperationException("A formal Tide asset cell did not contain a visible object.");
        }

        const int padding = 6;
        visibleMinX = Mathf.Max(0, visibleMinX - padding);
        visibleMinY = Mathf.Max(0, visibleMinY - padding);
        visibleMaxX = Mathf.Min(width - 1, visibleMaxX + padding);
        visibleMaxY = Mathf.Min(height - 1, visibleMaxY + padding);
        int trimmedWidth = visibleMaxX - visibleMinX + 1;
        int trimmedHeight = visibleMaxY - visibleMinY + 1;
        Color32[] trimmed = new Color32[trimmedWidth * trimmedHeight];

        for (int y = 0; y < trimmedHeight; y++)
        {
            Array.Copy(
                keyed,
                (visibleMinY + y) * width + visibleMinX,
                trimmed,
                y * trimmedWidth,
                trimmedWidth);
        }

        Texture2D output = new Texture2D(trimmedWidth, trimmedHeight, TextureFormat.RGBA32, false);
        output.name = "TideFormalCutout";
        output.filterMode = FilterMode.Bilinear;
        output.wrapMode = TextureWrapMode.Clamp;
        output.SetPixels32(trimmed);
        output.Apply(false, false);
        return output;
    }
}
