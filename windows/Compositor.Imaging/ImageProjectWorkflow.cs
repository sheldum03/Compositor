using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;

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

    public static TileRaster RenderFlatNormal(string projectDirectory)
    {
        var session = ProjectStore.Open(projectDirectory);
        var manifest = session.Current;
        if (manifest["version"]!.GetValue<int>() != 8 ||
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
            string image = Path.Combine(session.SourceDirectory, "images", layer["imageFile"]!.GetValue<string>());
            TileRaster raster = ImageCodec.Load(image);
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
            if (layer["isVisible"]!.GetValue<bool>()) result = RasterCompositor.SourceOver(result, raster);
        }
        return result;
    }

    public static void Save(ProjectSession session, string projectDirectory)
    {
        RequireRaster(session);
        ProjectStore.Save(session, projectDirectory, EncodeRaster);
    }

    public static void ExportPng(ProjectSession session, string output)
    {
        ImageCodec.SavePng(RequireRaster(session), output);
    }

    public static void ExportJpeg(ProjectSession session, string output, int quality,
        (byte R, byte G, byte B) background)
    {
        ImageCodec.SaveJpeg(RequireRaster(session), output, quality, background);
    }

    private static TileRaster RequireRaster(ProjectSession session) => session.CanEdit && session.Raster is { } raster
        ? raster : throw new NotSupportedException("Open the editable project through ImageProjectWorkflow first.");

    private static bool IsFlatNormalLayer(JsonObject layer, int width, int height)
    {
        if (!layer.All(pair => new[] { "blendMode", "id", "imageFile", "isGroup", "isVisible", "maskEnabled", "maskFile", "name", "opacity", "transform" }.Contains(pair.Key)) ||
            layer["imageFile"] is null || layer["isVisible"] is null ||
            layer["maskEnabled"] is not null && layer["maskFile"] is null ||
            layer["isGroup"] is { } group && group.GetValue<bool>() ||
            layer["opacity"] is { } opacity && opacity.GetValue<double>() != 1 ||
            layer["blendMode"] is { } blend && blend.GetValue<string>() != "Normal") return false;
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
