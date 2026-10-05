using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

public static class ImageProjectWorkflow
{
    public static ProjectSession Import(string imagePath, string projectDirectory)
    {
        TileRaster raster = ImageCodec.Load(imagePath);
        string destination = Path.GetFullPath(projectDirectory);
        string temporary = destination + ".import-" + Guid.NewGuid().ToString("N");
        Guid documentId = Guid.NewGuid(), layerId = Guid.NewGuid();
        string imageName = layerId.ToString("D").ToUpperInvariant() + ".png";
        try
        {
            string images = Path.Combine(temporary, "images");
            Directory.CreateDirectory(images);
            ImageCodec.SavePng(raster, Path.Combine(images, imageName));
            var manifest = new
            {
                format = "com.compositor.project", version = 8, documentID = documentId.ToString("D"),
                colorSpace = "sRGB", resolution = 72, width = raster.Width, height = raster.Height,
                activeLayerID = layerId.ToString("D"),
                layers = new[] { new
                {
                    id = layerId.ToString("D"), name = "Image", isVisible = true, imageFile = imageName,
                    transform = new
                    {
                        origin = new[] { 0, 0 }, size = new[] { raster.Width, raster.Height },
                        rotation = 0, flipX = false, flipY = false, sampling = "High quality"
                    }
                } }
            };
            File.WriteAllText(Path.Combine(temporary, "manifest.json"), JsonSerializer.Serialize(manifest));
            var session = ProjectStore.Open(temporary);
            if (!session.CanEdit) throw new InvalidDataException("Imported project failed validation.");
            session.AttachRaster(ImageCodec.Load(Path.Combine(images, imageName)));
            ProjectStore.SaveNew(session, destination);
            return session;
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    public static ProjectSession OpenEditable(string projectDirectory)
    {
        var session = ProjectStore.Open(projectDirectory);
        if (!session.CanEdit) throw new NotSupportedException("This project cannot be edited yet.");
        if (session.ImageName.Length == 0)
        {
            var rasters = new Dictionary<Guid, TileRaster>();
            foreach (JsonNode? node in session.Current["layers"]!.AsArray())
            {
                string name = node!["imageFile"]!.GetValue<string>();
                string image = Path.Combine(session.SourceDirectory, "images", name);
                ProjectStore.CheckAssetHash(session, name, image);
                TileRaster raster = ImageCodec.Load(image);
                ProjectStore.CheckAssetHash(session, name, image);
                rasters.Add(Guid.Parse(node["id"]!.GetValue<string>()), raster);
            }
            session.AttachLayerRasters(rasters);
            return session;
        }
        string temporary = Path.Combine(Path.GetTempPath(), "compositor-image-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            ProjectStore.ExportPng(session, temporary);
            session.AttachRaster(ImageCodec.Load(temporary));
            return session;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static TileRaster RenderFlatNormal(string projectDirectory) =>
        RenderFlatNormal(ProjectStore.Open(projectDirectory));

    public static TileRaster RenderFlatNormal(ProjectSession session) =>
        session.CanEdit ? RenderFlatNormalCore(session, null, null) : RenderCachedCore(session);

    public static TileRaster RenderFlatNormal(ProjectSession session, Guid layerId, TileRaster overrideRaster)
    {
        if (!session.CanEdit) throw new NotSupportedException("Temporary pixel previews require an editable project.");
        TileRaster current = session.GetLayerRaster(layerId);
        if (overrideRaster.Width != current.Width || overrideRaster.Height != current.Height)
            throw new ArgumentException("Preview raster dimensions do not match the layer.", nameof(overrideRaster));
        return RenderFlatNormalCore(session, layerId, overrideRaster);
    }

    private static TileRaster RenderFlatNormalCore(ProjectSession session, Guid? overrideLayerId, TileRaster? overrideRaster)
    {
        var manifest = session.Current;
        int version = manifest["version"]!.GetValue<int>();
        if (version is not (1 or 8) || version == 1 && manifest["layers"]!.AsArray().Count != 1 ||
            !manifest.All(pair => new[] { "activeLayerID", "colorSpace", "documentID", "format", "height", "layers", "resolution", "version", "width" }.Contains(pair.Key)))
            throw new NotSupportedException("This project cannot be rendered by the flat Normal renderer.");
        int width = manifest["width"]!.GetValue<int>(), height = manifest["height"]!.GetValue<int>();
        var layers = manifest["layers"]!.AsArray();
        if ((long)layers.Count * width * height > 100_000_000)
            throw new NotSupportedException("Flat layer source pixels exceed the rendering limit.");
        var result = new TileRaster(width, height);
        foreach (JsonNode? node in layers)
        {
            var layer = node!.AsObject();
            if (!IsFlatNormalLayer(layer, width, height))
                throw new NotSupportedException("This layer needs rendering features that are not implemented yet.");
            string imageName = layer["imageFile"]!.GetValue<string>();
            TileRaster raster;
            if (overrideLayerId == Guid.Parse(layer["id"]!.GetValue<string>())) raster = overrideRaster!;
            else if (session.Raster is { } memory && imageName == session.ImageName) raster = memory;
            else if (session.TryGetLoadedLayerRaster(Guid.Parse(layer["id"]!.GetValue<string>()), out var layerRaster))
                raster = layerRaster;
            else
            {
                string image = Path.Combine(session.SourceDirectory, "images", imageName);
                if (session.CanEdit) ProjectStore.CheckAssetHash(session, imageName, image);
                raster = ImageCodec.Load(image);
                if (session.CanEdit) ProjectStore.CheckAssetHash(session, imageName, image);
            }
            if (raster.Width != width || raster.Height != height)
                throw new InvalidDataException("Layer image dimensions do not match the canvas.");
            if (layer["maskFile"] is { } maskFile)
            {
                string maskPath = Path.Combine(session.SourceDirectory, "images", maskFile.GetValue<string>());
                GrayTileRaster mask = ImageCodec.LoadGrayMask(maskPath);
                if (mask.Width != width || mask.Height != height)
                    throw new NotSupportedException("Only full-canvas masks at the default placement are supported.");
                if (layer["maskEnabled"]?.GetValue<bool>() ?? true)
                    raster = RasterCompositor.ApplyMask(raster, mask);
            }
            if (layer["isVisible"]!.GetValue<bool>())
                result = LayerCompositor.Composite(result, raster, layer["opacity"]?.GetValue<double>() ?? 1,
                    layer["blendMode"]?.GetValue<string>() ?? "Normal");
        }
        return result;
    }

    private static TileRaster RenderCachedCore(ProjectSession session)
    {
        var manifest = session.Current;
        int width = manifest["width"]!.GetValue<int>(), height = manifest["height"]!.GetValue<int>();
        if (manifest["version"]!.GetValue<int>() is not (1 or 8) ||
            (long)manifest["layers"]!.AsArray().Count * width * height > 100_000_000)
            throw new NotSupportedException("This project exceeds the cached preview limits.");
        var result = new TileRaster(width, height);
        foreach (JsonNode? node in manifest["layers"]!.AsArray())
        {
            var layer = node!.AsObject();
            if (layer["imageFile"] is not { } imageNode) continue;
            if (layer["maskSourceID"] is not null || layer["adjustment"] is not null)
                throw new NotSupportedException("Cached previews do not yet render mask sources or adjustments.");
            string imageName = imageNode.GetValue<string>();
            string image = Path.Combine(session.SourceDirectory, "images", imageName);
            TileRaster raster = ImageCodec.Load(image);
            if (layer["maskFile"] is { } maskNode)
            {
                GrayTileRaster mask = ImageCodec.LoadGrayMask(Path.Combine(session.SourceDirectory, "images", maskNode.GetValue<string>()));
                if (mask.Width != width || mask.Height != height)
                    throw new NotSupportedException("Only full-canvas masks are supported in cached previews.");
                if (layer["maskEnabled"]?.GetValue<bool>() ?? true) raster = RasterCompositor.ApplyMask(raster, mask);
            }
            var transform = layer["transform"]?.AsObject()
                ?? throw new NotSupportedException("Cached layer transform data is missing.");
            var transformed = TransformCachedRaster(raster, transform, width, height);
            if (layer["isVisible"]?.GetValue<bool>() ?? true)
                result = LayerCompositor.Composite(result, transformed,
                    layer["opacity"]?.GetValue<double>() ?? 1,
                    layer["blendMode"]?.GetValue<string>() ?? "Normal");
        }
        return result;
    }

    private static TileRaster TransformCachedRaster(TileRaster source, JsonObject transform, int width, int height)
    {
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        if (origin?.Count != 2 || size?.Count != 2) throw new NotSupportedException("Cached layer transform data is invalid.");
        double x = origin[0]!.GetValue<double>(), y = origin[1]!.GetValue<double>();
        double targetWidth = size[0]!.GetValue<double>(), targetHeight = size[1]!.GetValue<double>();
        double rotation = transform["rotation"]?.GetValue<double>() ?? 0;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(targetWidth) || !double.IsFinite(targetHeight) ||
            !double.IsFinite(rotation) || targetWidth <= 0 || targetHeight <= 0)
            throw new NotSupportedException("Cached layer transform data is invalid.");
        using var srgb = SKColorSpace.CreateSrgb();
        using var sourceBitmap = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
        using var targetBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
        CopyToBitmap(source, sourceBitmap);
        using (var canvas = new SKCanvas(targetBitmap))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Save();
            canvas.Translate((float)(x + targetWidth / 2), (float)(y + targetHeight / 2));
            canvas.RotateDegrees((float)rotation);
            canvas.Scale(transform["flipX"]?.GetValue<bool>() == true ? -1 : 1,
                transform["flipY"]?.GetValue<bool>() == true ? -1 : 1);
            canvas.Translate((float)(-targetWidth / 2), (float)(-targetHeight / 2));
            canvas.DrawBitmap(sourceBitmap, new SKRect(0, 0, source.Width, source.Height),
                new SKRect(0, 0, (float)targetWidth, (float)targetHeight), paint);
            canvas.Restore();
        }
        return FromBitmap(targetBitmap, width, height);
    }

    private static void CopyToBitmap(TileRaster raster, SKBitmap bitmap)
    {
        for (int row = 0; row * TileRaster.TileSize < raster.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < raster.Width; column++)
        {
            var size = raster.TileDimensions(column, row);
            byte[] tile = raster.ReadTileCopy(column, row);
            for (int i = 0; i < tile.Length; i += 4)
                if (tile[i] > tile[i + 3] || tile[i + 1] > tile[i + 3] || tile[i + 2] > tile[i + 3])
                    throw new InvalidDataException("Layer contains invalid premultiplied RGBA.");
            for (int y = 0; y < size.Height; y++)
                Marshal.Copy(tile, y * size.Width * 4,
                    bitmap.GetPixels() + (row * TileRaster.TileSize + y) * bitmap.RowBytes + column * TileRaster.TileSize * 4,
                    size.Width * 4);
        }
    }

    private static TileRaster FromBitmap(SKBitmap bitmap, int width, int height)
    {
        var result = new TileRaster(width, height);
        for (int row = 0; row * TileRaster.TileSize < height; row++)
        for (int column = 0; column * TileRaster.TileSize < width; column++)
        {
            var size = result.TileDimensions(column, row);
            byte[] tile = new byte[size.Width * size.Height * 4];
            for (int y = 0; y < size.Height; y++)
                Marshal.Copy(bitmap.GetPixels() + (row * TileRaster.TileSize + y) * bitmap.RowBytes + column * TileRaster.TileSize * 4,
                    tile, y * size.Width * 4, size.Width * 4);
            result = result.ReplaceTile(column, row, tile);
        }
        return result;
    }

    public static void Save(ProjectSession session, string projectDirectory)
    {
        if (!session.CanEdit) throw new NotSupportedException("This project cannot be saved yet.");
        foreach (var layer in session.Layers) session.GetLayerRaster(layer.Id);
        ProjectStore.Save(session, projectDirectory, EncodeRaster);
    }

    public static void ExportPng(ProjectSession session, string output)
    {
        ImageCodec.SavePng(RenderFlatNormal(session), output);
    }

    public static void ExportJpeg(ProjectSession session, string output, int quality,
        (byte R, byte G, byte B) background)
    {
        ImageCodec.SaveJpeg(RenderFlatNormal(session), output, quality, background);
    }

    private static bool IsFlatNormalLayer(JsonObject layer, int width, int height)
    {
        if (!layer.All(pair => new[] { "blendMode", "id", "imageFile", "isGroup", "isVisible", "maskEnabled", "maskFile", "name", "opacity", "transform" }.Contains(pair.Key)) ||
            layer["imageFile"] is null || layer["isVisible"] is null ||
            layer["maskEnabled"] is not null && layer["maskFile"] is null ||
            layer["isGroup"] is { } group && group.GetValue<bool>() ||
            layer["opacity"] is { } opacity && (!double.IsFinite(opacity.GetValue<double>()) || opacity.GetValue<double>() is < 0 or > 1) ||
            layer["blendMode"] is { } blend && !ProjectSession.SupportedBlendModes.Contains(blend.GetValue<string>())) return false;
        var transform = layer["transform"]?.AsObject();
        if (transform is null || !transform.All(pair => new[] { "flipX", "flipY", "origin", "rotation", "sampling", "size" }.Contains(pair.Key))) return false;
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        return origin?.Count == 2 && size?.Count == 2 &&
            origin[0]!.GetValue<double>() == 0 && origin[1]!.GetValue<double>() == 0 &&
            size[0]!.GetValue<double>() == width && size[1]!.GetValue<double>() == height &&
            transform["rotation"]?.GetValue<double>() == 0 &&
            transform["flipX"]?.GetValue<bool>() == false && transform["flipY"]?.GetValue<bool>() == false;
    }

    private static void EncodeRaster(TileRaster raster, string path)
    {
        ImageCodec.SavePng(raster, path);
        var decoded = ImageCodec.Load(path);
        if (decoded.Width != raster.Width || decoded.Height != raster.Height)
            throw new InvalidDataException("Encoded project image has the wrong dimensions.");
    }
}
