using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;

internal static class BlendChecks
{
    public static void Run(string output, string fixtures)
    {
        string directory = Path.Combine(output, "blend");
        Directory.CreateDirectory(directory);
        string source = Path.Combine(fixtures, "B01.comp", "images", "00000000-0000-4000-8000-000000000001.png");
        TileRaster seed = ImageCodec.Load(source);
        Require(seed.Width == 64 && seed.Height == 48, "M0 blend seed dimensions changed.");
        TileRaster lower = Repeat(seed, 0, 0), upper = Repeat(seed, 17, 11);
        byte[] originalUpper = Bytes(upper);
        var identities = Directory.GetFiles(fixtures, "B*-mac.png").Concat(
            Directory.EnumerateFiles(fixtures, "*", SearchOption.AllDirectories).Where(path =>
                Path.GetRelativePath(fixtures, path).StartsWith("B", StringComparison.Ordinal) && path.Contains(".comp")))
            .ToDictionary(path => Path.GetRelativePath(fixtures, path), Hash);
        File.WriteAllText(Path.Combine(directory, "fixture-adaptation.json"), JsonSerializer.Serialize(new
        {
            sources = identities, canvas = new[] { 300, 257 }, seed = Path.GetRelativePath(fixtures, source),
            lower = "Repeat original 64x48 premultiplied M0 seed", upper = "Repeat seed with integer phase +17,+11",
            legacyGeometry = "Original B projects include a pass-through group and 64x48->44x32 scale at (10,6); they remain read-only. Their original Mac images are not treated as equivalent to these adapted full-canvas projects.",
            macAcceptance = "Actual Mac renders of adapted production projects must be compared separately with exact channel equality; no tolerance is approved."
        }, new JsonSerializerOptions { WriteIndented = true }));
        int index = 0;
        foreach (string mode in ProjectSession.SupportedBlendModes)
        {
            index++;
            var session = ProjectSession.CreateBlank(lower.Width, lower.Height);
            Guid bottomId = session.Layers[0].Id;
            session.ReplaceLayerRaster(bottomId, lower);
            Guid topId = session.AddBlankLayer("Overlay", 1);
            session.ReplaceLayerRaster(topId, upper);
            session.SetLayerOpacity(topId, 0.55);
            session.SetLayerBlendMode(topId, mode);
            string name = $"B{index:00}-flat";
            string project = Path.Combine(directory, name + ".comp");
            ImageProjectWorkflow.Save(session, project);
            TileRaster baseline = ImageProjectWorkflow.RenderFlatNormal(session);
            // WholeM1 is retained below as a reference helper; exact local equality is asserted between
            // tiled preview, export and reopened production output, while Mac cross-backend equality remains separate.
            ImageProjectWorkflow.ExportPng(session, Path.Combine(directory, name + ".png"));
            Equal(baseline, ImageCodec.Load(Path.Combine(directory, name + ".png")));
            var reopened = ImageProjectWorkflow.OpenEditable(project);
            Require(!reopened.IsDirty && reopened.Layers[1].BlendMode == mode && reopened.Layers[1].Opacity == 0.55,
                "Blend appearance did not reopen.");
            Equal(baseline, ImageProjectWorkflow.RenderFlatNormal(reopened));
            var hashes = Directory.GetFiles(Path.Combine(project, "images")).ToDictionary(
                path => Guid.Parse(Path.GetFileNameWithoutExtension(path)), Hash);
            session.SetLayerOpacity(topId, 0.55); session.SetLayerBlendMode(topId, mode);
            foreach (Action invalid in new Action[]
            {
                () => session.SetLayerOpacity(topId, double.NaN), () => session.SetLayerOpacity(topId, -0.1),
                () => session.SetLayerOpacity(topId, 1.1), () => session.SetLayerBlendMode(topId, "Unknown"),
                () => session.SetLayerBlendMode(Guid.NewGuid(), mode)
            })
            {
                try { invalid(); throw new Exception("Invalid appearance edit was accepted."); }
                catch (ArgumentException) { }
            }
            Require(!session.IsDirty, "Rejected/no-op appearance edit changed the save point.");
            session.SetLayerOpacity(topId, 0);
            Equal(lower, ImageProjectWorkflow.RenderFlatNormal(session));
            Require(session.Undo() && !session.IsDirty, "Opacity undo lost the save point.");
            session.SetLayerVisible(topId, false);
            Equal(lower, ImageProjectWorkflow.RenderFlatNormal(session));
            Require(session.Undo() && !session.IsDirty, "Hidden layer undo lost appearance.");
            session.MoveLayer(topId, 0);
            Equal(RasterCompositor.SourceOver(LayerCompositor.Composite(new TileRaster(300, 257), upper, 0.55, mode), lower),
                ImageProjectWorkflow.RenderFlatNormal(session));
            Require(session.Undo() && !session.IsDirty, "Layer order undo lost appearance.");
            byte[] changedTile = upper.ReadTileCopy(1, 1);
            new byte[] { 90, 80, 70, 255 }.CopyTo(changedTile, 0);
            TileRaster changed = upper.ReplaceTile(1, 1, changedTile);
            TileRaster temporary = ImageProjectWorkflow.RenderFlatNormal(session, topId, changed);
            session.ReplaceLayerRaster(topId, changed);
            Equal(temporary, ImageProjectWorkflow.RenderFlatNormal(session));
            session.SetLayerOpacity(topId, 0.25);
            session.RenameLayer(topId, "Edited overlay");
            Require(session.Undo() && session.Undo() && session.Undo() && !session.IsDirty,
                "Mixed pixel/opacity/name history did not undo to the save point.");
            Equal(baseline, ImageProjectWorkflow.RenderFlatNormal(session));
            Require(session.Redo() && session.Redo() && session.Redo(), "Mixed appearance history did not redo.");
            string edited = Path.Combine(directory, name + "-edited.comp");
            ImageProjectWorkflow.Save(session, edited);
            var editedSession = ImageProjectWorkflow.OpenEditable(edited);
            Require(editedSession.Layers[1].Opacity == 0.25 && editedSession.Layers[1].BlendMode == mode &&
                editedSession.Layers[1].Name == "Edited overlay", "Edited appearance was lost in save/readback.");
            Equal(ImageProjectWorkflow.RenderFlatNormal(session), ImageProjectWorkflow.RenderFlatNormal(editedSession));
            Require(hashes[bottomId] ==
                Hash(Directory.GetFiles(Path.Combine(edited, "images")).Single(path =>
                    Guid.Parse(Path.GetFileNameWithoutExtension(path)) == bottomId)), "Appearance/pixel edit rewrote the other layer.");
            Require(Bytes(upper).SequenceEqual(originalUpper), "Appearance/pixel edit changed an older source snapshot.");
        }
        Require(index == 13, "The baseline blend list does not contain all 13 modes.");
        File.WriteAllText(Path.Combine(directory, "local-results.json"), JsonSerializer.Serialize(new
        {
            localChecksPassed = true, modes = index, tiledExportReopenExact = true,
            macExactComparisonExecuted = true, macBlendAcceptance = "not passed; 5 exact, 8 maximum RGB difference 1; no tolerance approved",
            history = "appearance/pixel/name, visibility, order and save point", tileEdges = "300x257; all four tiles"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: all 13 blend modes and opacity local tiled/export/history checks; Mac exact comparison recorded separately");
    }

    private static TileRaster Repeat(TileRaster seed, int phaseX, int phaseY)
    {
        byte[] source = seed.ReadTileCopy(0, 0);
        var raster = new TileRaster(300, 257);
        for (int row = 0; row < 2; row++)
        for (int column = 0; column < 2; column++)
        {
            var size = raster.TileDimensions(column, row);
            var tile = new byte[size.Width * size.Height * 4];
            for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
            {
                int sx = (column * 256 + x + phaseX) % seed.Width, sy = (row * 256 + y + phaseY) % seed.Height;
                source.AsSpan((sy * seed.Width + sx) * 4, 4).CopyTo(tile.AsSpan((y * size.Width + x) * 4, 4));
            }
            raster = raster.ReplaceTile(column, row, tile);
        }
        return raster;
    }

    private static TileRaster WholeM1(TileRaster lower, TileRaster upper, double opacity, string mode)
    {
        using var srgb = SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(lower.Width, lower.Height, SKColorType.Rgba8888, SKAlphaType.Premul, srgb);
        using var bottom = new SKBitmap(info); using var top = new SKBitmap(info);
        Marshal.Copy(Bytes(lower), 0, bottom.GetPixels(), bottom.ByteCount);
        Marshal.Copy(Bytes(upper), 0, top.GetPixels(), top.ByteCount);
        if (opacity != 1)
        {
            var raw = new byte[top.ByteCount];
            Marshal.Copy(top.GetPixels(), raw, 0, raw.Length);
            for (int i = 0; i < raw.Length; i += 4)
                for (int channel = 0; channel < 4; channel++)
                    raw[i + channel] = (byte)Math.Round(raw[i + channel] * opacity, MidpointRounding.AwayFromZero);
            Marshal.Copy(raw, 0, top.GetPixels(), raw.Length);
        }
        using var canvas = new SKCanvas(bottom);
        using var paint = new SKPaint
        {
            BlendMode = mode == "Normal" ? SKBlendMode.SrcOver : Enum.Parse<SKBlendMode>(mode.Replace(" ", "")),
            Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)), FilterQuality = SKFilterQuality.None
        };
        canvas.DrawBitmap(top, 0, 0, paint); canvas.Flush();
        var result = new TileRaster(lower.Width, lower.Height);
        for (int row = 0; row < 2; row++)
        for (int column = 0; column < 2; column++)
        {
            var size = result.TileDimensions(column, row); var tile = new byte[size.Width * size.Height * 4];
            for (int y = 0; y < size.Height; y++)
                Marshal.Copy(bottom.GetPixels() + (row * 256 + y) * bottom.RowBytes + column * 256 * 4,
                    tile, y * size.Width * 4, size.Width * 4);
            result = result.ReplaceTile(column, row, tile);
        }
        return result;
    }

    private static byte[] Bytes(TileRaster raster)
    {
        var bytes = new byte[raster.Width * raster.Height * 4];
        for (int row = 0; row * 256 < raster.Height; row++)
        for (int column = 0; column * 256 < raster.Width; column++)
        {
            var size = raster.TileDimensions(column, row); byte[] tile = raster.ReadTileCopy(column, row);
            for (int y = 0; y < size.Height; y++)
                tile.AsSpan(y * size.Width * 4, size.Width * 4).CopyTo(bytes.AsSpan(((row * 256 + y) * raster.Width + column * 256) * 4));
        }
        return bytes;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Equal(TileRaster expected, TileRaster actual)
    {
        byte[] a = Bytes(expected), b = Bytes(actual);
        if (a.SequenceEqual(b)) return;
        int offset = Enumerable.Range(0, a.Length).First(index => a[index] != b[index]);
        throw new Exception($"Blend pixels differ at channel {offset}: {a[offset]} vs {b[offset]}.");
    }
    private static void Require(bool passed, string message) { if (!passed) throw new Exception(message); }
}
