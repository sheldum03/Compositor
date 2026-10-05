using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

public static class ImageProjectWorkflow
{
    private sealed record FlatLayerRender(JsonObject Manifest, Guid Id, TileRaster Raster);

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
            var masks = new Dictionary<Guid, GrayTileRaster>();
            foreach (JsonNode? node in session.Current["layers"]!.AsArray())
            {
                var layer = node!.AsObject();
                Guid id = Guid.Parse(layer["id"]!.GetValue<string>());
                if (layer["imageFile"] is { } imageNode)
                {
                    string name = imageNode.GetValue<string>();
                    string image = Path.Combine(session.SourceDirectory, "images", name);
                    ProjectStore.CheckAssetHash(session, name, image);
                    TileRaster raster = ImageCodec.Load(image);
                    ProjectStore.CheckAssetHash(session, name, image);
                    rasters.Add(id, raster);
                }
                if (layer["maskFile"] is { } maskNode)
                {
                    string maskName = maskNode.GetValue<string>();
                    string maskPath = Path.Combine(session.SourceDirectory, "images", maskName);
                    ProjectStore.CheckAssetHash(session, maskName, maskPath);
                    GrayTileRaster mask = ImageCodec.LoadGrayMask(maskPath);
                    if (mask.Width != session.Width || mask.Height != session.Height)
                        throw new InvalidDataException("Layer mask dimensions do not match the canvas.");
                    ProjectStore.CheckAssetHash(session, maskName, maskPath);
                    masks.Add(id, mask);
                }
            }
            session.AttachLayerRasters(rasters, masks.Count == 0 ? null : masks);
            return session;
        }
        string imagePath = Path.Combine(session.SourceDirectory, "images", session.ImageName);
        ProjectStore.CheckAssetHash(session, session.ImageName, imagePath);
        TileRaster raster = ImageCodec.Load(imagePath);
        ProjectStore.CheckAssetHash(session, session.ImageName, imagePath);
        GrayTileRaster? mask = null;
        if (session.Layers[0].HasMask)
        {
            var layer = session.Current["layers"]![0]!.AsObject();
            string maskName = layer["maskFile"]!.GetValue<string>();
            string maskPath = Path.Combine(session.SourceDirectory, "images", maskName);
            ProjectStore.CheckAssetHash(session, maskName, maskPath);
            mask = ImageCodec.LoadGrayMask(maskPath);
            if (mask.Width != session.Width || mask.Height != session.Height)
                throw new InvalidDataException("Layer mask dimensions do not match the canvas.");
        }
        session.AttachRaster(raster, mask);
        return session;
    }

    public static TileRaster RenderFlatNormal(string projectDirectory) =>
        RenderFlatNormal(ProjectStore.Open(projectDirectory));

    public static TileRaster RenderFlatNormal(ProjectSession session) =>
        session.CanEdit
            ? session.HasGroups ? RenderCachedCore(session, useLoadedAssets: true) : RenderFlatNormalCore(session, null, null, null)
            : RenderCachedCore(session);

    public static TileRaster RenderFlatNormal(ProjectSession session, Guid layerId, TileRaster overrideRaster)
    {
        if (!session.CanEdit) throw new NotSupportedException("Temporary pixel previews require an editable project.");
        FlatLayerInfo layerInfo = session.Layers.SingleOrDefault(layer => layer.Id == layerId)
            ?? throw new ArgumentException("Layer does not belong to this project.", nameof(layerId));
        TileRaster? current = layerInfo.IsGroup ? null : session.GetLayerRaster(layerId);
        if ((current is not null && (overrideRaster.Width != current.Width || overrideRaster.Height != current.Height)) ||
            (current is null && (overrideRaster.Width != session.Width || overrideRaster.Height != session.Height)))
            throw new ArgumentException("Preview raster dimensions do not match the layer.", nameof(overrideRaster));
        return session.HasGroups
            ? RenderCachedCore(session, true, layerId, overrideRaster, null)
            : RenderFlatNormalCore(session, layerId, overrideRaster, null);
    }

    public static TileRaster RenderFlatNormal(ProjectSession session, Guid layerId, TileRaster overrideRaster,
        GrayTileRaster overrideMask)
    {
        if (!session.CanEdit) throw new NotSupportedException("Temporary previews require an editable project.");
        FlatLayerInfo layerInfo = session.Layers.SingleOrDefault(layer => layer.Id == layerId)
            ?? throw new ArgumentException("Layer does not belong to this project.", nameof(layerId));
        TileRaster? current = layerInfo.IsGroup ? null : session.GetLayerRaster(layerId);
        if ((current is not null && (overrideRaster.Width != current.Width || overrideRaster.Height != current.Height)) ||
            (current is null && (overrideRaster.Width != session.Width || overrideRaster.Height != session.Height)))
            throw new ArgumentException("Preview raster dimensions do not match the layer.", nameof(overrideRaster));
        if (overrideMask.Width != session.Width || overrideMask.Height != session.Height)
            throw new ArgumentException("Preview mask dimensions do not match the canvas.", nameof(overrideMask));
        return session.HasGroups
            ? RenderCachedCore(session, true, layerId, overrideRaster, overrideMask)
            : RenderFlatNormalCore(session, layerId, overrideRaster, overrideMask);
    }

    private static TileRaster RenderFlatNormalCore(ProjectSession session, Guid? overrideLayerId, TileRaster? overrideRaster,
        GrayTileRaster? overrideMask)
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
        var prepared = new List<FlatLayerRender>(layers.Count);
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
                Guid layerId = Guid.Parse(layer["id"]!.GetValue<string>());
                GrayTileRaster mask = overrideLayerId == layerId && overrideMask is not null
                    ? overrideMask
                    : session.TryGetLoadedLayerMask(layerId, out var loadedMask)
                    ? loadedMask : ImageCodec.LoadGrayMask(maskPath);
                if (session.CanEdit && session.AssetHashes.ContainsKey(maskFile.GetValue<string>()))
                    ProjectStore.CheckAssetHash(session, maskFile.GetValue<string>(), maskPath);
                if (mask.Width != width || mask.Height != height)
                    throw new NotSupportedException("Only full-canvas masks at the default placement are supported.");
                if (layer["maskEnabled"]?.GetValue<bool>() ?? true)
                    raster = RasterCompositor.ApplyMask(raster, mask);
            }
            var transform = layer["transform"]!.AsObject();
            if (!IsIdentityTransform(transform, width, height))
                raster = TransformCachedRaster(raster, transform, width, height);
            prepared.Add(new FlatLayerRender(layer, Guid.Parse(layer["id"]!.GetValue<string>()), raster));
        }
        var byId = prepared.ToDictionary(layer => layer.Id);
        var resolved = new Dictionary<Guid, TileRaster>();
        var resolving = new HashSet<Guid>();
        TileRaster Resolve(Guid layerId)
        {
            if (resolved.TryGetValue(layerId, out var cached)) return cached;
            if (!resolving.Add(layerId)) throw new InvalidDataException("Invalid mask source cycle.");
            var layer = byId[layerId];
            TileRaster raster = layer.Raster;
            if (layer.Manifest["maskSourceID"] is { } sourceNode)
            {
                Guid sourceId = Guid.Parse(sourceNode.GetValue<string>());
                if (!byId.ContainsKey(sourceId)) throw new InvalidDataException("Invalid mask source.");
                var source = byId[sourceId];
                raster = RasterCompositor.ApplyAlphaMask(raster, Resolve(sourceId),
                    source.Manifest["opacity"]?.GetValue<double>() ?? 1);
            }
            resolving.Remove(layerId);
            resolved[layerId] = raster;
            return raster;
        }

        var result = new TileRaster(width, height);
        foreach (var layer in prepared)
        {
            if (layer.Manifest["isVisible"]!.GetValue<bool>())
                result = LayerCompositor.Composite(result, Resolve(layer.Id), layer.Manifest["opacity"]?.GetValue<double>() ?? 1,
                    layer.Manifest["blendMode"]?.GetValue<string>() ?? "Normal");
        }
        return result;
    }

    private sealed record CachedLayer(JsonObject Manifest, Guid Id, TileRaster? Raster, GrayTileRaster? Mask);

    private static TileRaster RenderCachedCore(ProjectSession session, bool useLoadedAssets = false,
        Guid? overrideLayerId = null, TileRaster? overrideRaster = null, GrayTileRaster? overrideMask = null,
        Guid? rootOnly = null)
    {
        var manifest = session.Current;
        int version = manifest["version"]!.GetValue<int>();
        int width = manifest["width"]!.GetValue<int>(), height = manifest["height"]!.GetValue<int>();
        if (version is < 1 or > 8 ||
            (long)manifest["layers"]!.AsArray().Count * width * height > 100_000_000)
            throw new NotSupportedException("This project exceeds the cached preview limits.");

        var prepared = new Dictionary<Guid, CachedLayer>();
        foreach (JsonNode? node in manifest["layers"]!.AsArray())
        {
            var layer = node!.AsObject();
            Guid id = Guid.Parse(layer["id"]!.GetValue<string>());
            bool isGroup = layer["isGroup"]?.GetValue<bool>() == true;
            GrayTileRaster? mask = null;
            if (layer["maskFile"] is { } maskNode)
            {
                string maskName = maskNode.GetValue<string>();
                Guid layerId = Guid.Parse(layer["id"]!.GetValue<string>());
                mask = overrideLayerId == layerId && overrideMask is not null
                    ? overrideMask
                    : useLoadedAssets && session.TryGetLoadedLayerMask(layerId, out var loadedMask)
                    ? loadedMask
                    : ImageCodec.LoadGrayMask(Path.Combine(session.SourceDirectory, "images", maskName));
                if (mask.Width != width || mask.Height != height)
                    throw new NotSupportedException("Only full-canvas masks are supported in cached previews.");
            }
            if (isGroup)
            {
                if (layer["imageFile"] is not null || layer["adjustment"] is not null)
                    throw new NotSupportedException("Cached group metadata is not supported.");
                var groupTransform = layer["transform"]?.AsObject()
                    ?? throw new NotSupportedException("Cached group transform data is missing.");
                prepared.Add(id, new CachedLayer(layer, id, null, mask));
                continue;
            }
            if (layer["imageFile"] is not { } imageNode || layer["adjustment"] is not null)
                throw new NotSupportedException("Cached non-raster layers are not supported.");
            string imageName = imageNode.GetValue<string>();
            TileRaster raster = overrideLayerId == id && overrideRaster is not null
                ? overrideRaster
                : useLoadedAssets && session.TryGetLoadedLayerRaster(id, out var loadedRaster)
                ? loadedRaster
                : ImageCodec.Load(Path.Combine(session.SourceDirectory, "images", imageName));
            if (mask is not null && (layer["maskEnabled"]?.GetValue<bool>() ?? true))
                raster = RasterCompositor.ApplyMask(raster, mask);
            var layerTransform = layer["transform"]?.AsObject()
                ?? throw new NotSupportedException("Cached layer transform data is missing.");
            raster = TransformCachedRaster(raster, layerTransform, width, height);
            prepared.Add(id, new CachedLayer(layer, id, raster, mask));
        }

        var children = prepared.Values
            .GroupBy(layer => layer.Manifest["parentID"] is { } parent
                ? Guid.Parse(parent.GetValue<string>()) : Guid.Empty)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var resolved = new Dictionary<Guid, TileRaster>();
        var resolving = new HashSet<Guid>();
        TileRaster ResolveLeaf(Guid layerId)
        {
            if (resolved.TryGetValue(layerId, out var cached)) return cached;
            if (!prepared.TryGetValue(layerId, out var layer) || layer.Raster is null)
                throw new InvalidDataException("Invalid cached mask source.");
            if (!resolving.Add(layerId)) throw new InvalidDataException("Invalid mask source cycle.");
            TileRaster raster = layer.Raster;
            if (layer.Manifest["maskSourceID"] is { } sourceNode)
            {
                Guid sourceId = Guid.Parse(sourceNode.GetValue<string>());
                if (!prepared.TryGetValue(sourceId, out var source) || source.Raster is null)
                    throw new InvalidDataException("Invalid cached mask source.");
                raster = RasterCompositor.ApplyAlphaMask(raster, ResolveLeaf(sourceId),
                    source.Manifest["opacity"]?.GetValue<double>() ?? 1);
            }
            resolving.Remove(layerId);
            resolved[layerId] = raster;
            return raster;
        }

        TileRaster RenderClipStack(CachedLayer baseLayer, IReadOnlyList<CachedLayer> clipped)
        {
            if (baseLayer.Raster is null) throw new InvalidDataException("Clipping source is not a raster layer.");
            TileRaster basePixels = ResolveLeaf(baseLayer.Id);
            double baseOpacity = baseLayer.Manifest["opacity"]?.GetValue<double>() ?? 1;
            string baseMode = baseLayer.Manifest["blendMode"]?.GetValue<string>() ?? "Normal";
            ValidateAppearance(baseOpacity, baseMode);
            var stack = LayerCompositor.Composite(new TileRaster(width, height), basePixels, baseOpacity, baseMode);
            var alpha = stack;
            stack = UnpremultiplyOpaque(stack);
            foreach (var child in clipped)
            {
                if (child.Raster is null) throw new InvalidDataException("Clipping target is not a raster layer.");
                double opacity = child.Manifest["opacity"]?.GetValue<double>() ?? 1;
                string mode = child.Manifest["blendMode"]?.GetValue<string>() ?? "Normal";
                ValidateAppearance(opacity, mode);
                // A clipped child is restricted by the base stack once, so its own live source
                // is deliberately not applied a second time here.
                stack = LayerCompositor.Composite(stack, child.Raster, opacity, mode);
            }
            return RestoreAlpha(stack, alpha);
        }

        TileRaster RenderNode(CachedLayer layer, IReadOnlyList<GrayTileRaster> inheritedMasks)
        {
            if (!(layer.Manifest["isVisible"]?.GetValue<bool>() ?? true))
                return new TileRaster(width, height);
            if (layer.Raster is not null)
            {
                TileRaster raster = ResolveLeaf(layer.Id);
                foreach (GrayTileRaster groupMask in inheritedMasks)
                    raster = RasterCompositor.ApplyMask(raster, groupMask);
                return raster;
            }
            var groupMasks = inheritedMasks;
            if (layer.Mask is not null && (layer.Manifest["maskEnabled"]?.GetValue<bool>() ?? true))
                groupMasks = inheritedMasks.Append(layer.Mask).ToArray();
            var result = new TileRaster(width, height);
            if (children.TryGetValue(layer.Id, out var descendants))
            {
                for (int index = 0; index < descendants.Length; index++)
                {
                    var child = descendants[index];
                    if (!(child.Manifest["isVisible"]?.GetValue<bool>() ?? true)) continue;
                    if (child.Raster is null)
                    {
                        result = CompositeChild(result, child, RenderNode(child, groupMasks));
                        continue;
                    }
                    var clipped = new List<CachedLayer>();
                    int end = index + 1;
                    while (end < descendants.Length && descendants[end].Raster is not null &&
                           (descendants[end].Manifest["isVisible"]?.GetValue<bool>() ?? true) &&
                           descendants[end].Manifest["maskSourceID"] is { } source &&
                           Guid.Parse(source.GetValue<string>()) == child.Id)
                    {
                        clipped.Add(descendants[end]);
                        end++;
                    }
                    TileRaster childRaster = clipped.Count == 0
                        ? ResolveLeaf(child.Id)
                        : RenderClipStack(child, clipped);
                    foreach (GrayTileRaster groupMask in groupMasks)
                        childRaster = RasterCompositor.ApplyMask(childRaster, groupMask);
                    result = CompositeChild(result, child, childRaster, clipped.Count != 0);
                    index = end - 1;
                }
            }
            var transform = layer.Manifest["transform"]!.AsObject();
            return IsIdentityTransform(transform, width, height)
                ? result
                : TransformCachedRaster(result, transform, width, height);
        }

        TileRaster CompositeChild(TileRaster bottom, CachedLayer layer, TileRaster top, bool stackAlreadyStyled = false)
        {
            if (stackAlreadyStyled) return LayerCompositor.Composite(bottom, top, 1, "Normal");
            double opacity = layer.Manifest["opacity"]?.GetValue<double>() ?? 1;
            string mode = layer.Manifest["blendMode"]?.GetValue<string>() ?? "Normal";
            ValidateAppearance(opacity, mode);
            return LayerCompositor.Composite(bottom, top, opacity, mode);
        }

        void ValidateAppearance(double opacity, string mode)
        {
            if (!double.IsFinite(opacity) || opacity is < 0 or > 1 ||
                !ProjectSession.SupportedBlendModes.Contains(mode))
                throw new NotSupportedException("Cached layer appearance is not supported.");
        }

        if (rootOnly is { } rootId)
        {
            if (!prepared.TryGetValue(rootId, out var root) || root.Raster is not null)
                throw new ArgumentException("The requested layer is not a group.", nameof(rootOnly));
            return RenderNode(root, Array.Empty<GrayTileRaster>());
        }

        var output = new TileRaster(width, height);
        if (children.TryGetValue(Guid.Empty, out var roots))
            foreach (var root in roots)
            {
                TileRaster raster = RenderNode(root, Array.Empty<GrayTileRaster>());
                output = CompositeChild(output, root, raster);
            }
        return output;
    }

    private static TileRaster UnpremultiplyOpaque(TileRaster source)
    {
        var result = new TileRaster(source.Width, source.Height);
        for (int row = 0; row * TileRaster.TileSize < source.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < source.Width; column++)
        {
            byte[] pixels = source.ReadTileCopy(column, row);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int alpha = pixels[i + 3];
                for (int channel = 0; channel < 3; channel++)
                    pixels[i + channel] = alpha == 0 ? (byte)0 : (byte)Math.Min(255, (pixels[i + channel] * 255 + alpha / 2) / alpha);
                pixels[i + 3] = 255;
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
    }

    private static TileRaster RestoreAlpha(TileRaster source, TileRaster alphaSource)
    {
        var result = new TileRaster(source.Width, source.Height);
        for (int row = 0; row * TileRaster.TileSize < source.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < source.Width; column++)
        {
            byte[] pixels = source.ReadTileCopy(column, row);
            byte[] alpha = alphaSource.ReadTileCopy(column, row);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int coverage = alpha[i + 3];
                for (int channel = 0; channel < 3; channel++)
                    pixels[i + channel] = (byte)((pixels[i + channel] * coverage + 127) / 255);
                pixels[i + 3] = (byte)coverage;
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
    }

    private static bool IsIdentityTransform(JsonObject transform, int width, int height)
    {
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        return origin?.Count == 2 && size?.Count == 2 &&
            origin[0]!.GetValue<double>() == 0 && origin[1]!.GetValue<double>() == 0 &&
            size[0]!.GetValue<double>() == width && size[1]!.GetValue<double>() == height &&
            (transform["rotation"]?.GetValue<double>() ?? 0) == 0 &&
            (transform["flipX"]?.GetValue<bool>() ?? false) == false &&
            (transform["flipY"]?.GetValue<bool>() ?? false) == false;
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

    private static GrayTileRaster TransformCachedMask(GrayTileRaster source, JsonObject transform, int width, int height)
    {
        TileRaster maskRaster = new(width, height);
        for (int row = 0; row * TileRaster.TileSize < source.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < source.Width; column++)
        {
            byte[] coverage = source.ReadTileCopy(column, row);
            byte[] rgba = new byte[coverage.Length * 4];
            for (int index = 0; index < coverage.Length; index++)
            {
                byte value = coverage[index];
                rgba[index * 4] = value;
                rgba[index * 4 + 1] = value;
                rgba[index * 4 + 2] = value;
                rgba[index * 4 + 3] = value;
            }
            maskRaster = maskRaster.ReplaceTile(column, row, rgba);
        }
        TileRaster transformed = TransformCachedRaster(maskRaster, transform, width, height);
        var result = new GrayTileRaster(width, height);
        for (int row = 0; row * TileRaster.TileSize < height; row++)
        for (int column = 0; column * TileRaster.TileSize < width; column++)
        {
            var size = transformed.TileDimensions(column, row);
            byte[] pixels = transformed.ReadTileCopy(column, row);
            byte[] coverage = new byte[size.Width * size.Height];
            for (int index = 0; index < coverage.Length; index++) coverage[index] = pixels[index * 4 + 3];
            result = result.ReplaceTile(column, row, coverage);
        }
        return result;
    }

    private static TileRaster RestoreMaskedRaster(TileRaster target, GrayTileRaster mask)
    {
        var result = new TileRaster(target.Width, target.Height);
        for (int row = 0; row * TileRaster.TileSize < target.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < target.Width; column++)
        {
            byte[] pixels = target.ReadTileCopy(column, row);
            byte[] coverage = mask.ReadTileCopy(column, row);
            for (int pixel = 0; pixel < coverage.Length; pixel++)
            {
                int offset = pixel * 4;
                byte alpha = RestoreMaskedChannel(pixels[offset + 3], coverage[pixel]);
                pixels[offset + 3] = alpha;
                for (int channel = 0; channel < 3; channel++)
                    pixels[offset + channel] = Math.Min(alpha,
                        RestoreMaskedChannel(pixels[offset + channel], coverage[pixel]));
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
    }

    private static byte RestoreMaskedChannel(byte target, byte coverage)
    {
        if (coverage == 0) return 0;
        int estimate = Math.Clamp((target * 255 + coverage / 2) / coverage, 0, 255);
        int best = estimate, bestError = int.MaxValue;
        for (int candidate = Math.Max(0, estimate - 2); candidate <= Math.Min(255, estimate + 2); candidate++)
        {
            int value = (candidate * coverage + 127) / 255;
            int error = Math.Abs(value - target);
            if (error < bestError) { best = candidate; bestError = error; }
        }
        return (byte)best;
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
        foreach (var layer in session.Layers.Where(layer => !layer.IsGroup)) session.GetLayerRaster(layer.Id);
        foreach (var layer in session.Layers)
            if (layer.HasMask) session.GetLayerMask(layer.Id);
        ProjectStore.Save(session, projectDirectory, EncodeRaster, EncodeMask);
    }

    public static void BakeGroupTransform(ProjectSession session, Guid groupId)
    {
        if (!session.CanEdit) throw new NotSupportedException("This project is read-only.");
        FlatLayerInfo group = session.Layers.SingleOrDefault(layer => layer.Id == groupId)
            ?? throw new ArgumentException("Layer does not belong to this project.", nameof(groupId));
        if (!group.IsGroup) throw new ArgumentException("Layer is not a group.", nameof(groupId));
        if (session.IsGroupTransformIdentity(groupId))
            throw new InvalidOperationException("Identity groups can be ungrouped without baking.");
        TileRaster baked = RenderCachedCore(session, useLoadedAssets: true, rootOnly: groupId);
        session.ReplaceGroupWithRaster(groupId, baked);
    }

    public static void BakeLayerTransform(ProjectSession session, Guid layerId)
    {
        if (!session.CanEdit) throw new NotSupportedException("This project is read-only.");
        FlatLayerInfo layer = session.Layers.SingleOrDefault(layer => layer.Id == layerId)
            ?? throw new ArgumentException("Layer does not belong to this project.", nameof(layerId));
        if (layer.IsGroup) throw new ArgumentException("Group layers must use bake-ungroup.", nameof(layerId));
        if (session.IsLayerTransformIdentity(layerId))
            throw new InvalidOperationException("Identity layers do not need baking.");
        var transform = session.Current["layers"]!.AsArray()
            .Single(node => Guid.Parse(node!["id"]!.GetValue<string>()) == layerId)!["transform"]!.AsObject();
        TileRaster source = session.GetLayerRaster(layerId);
        TileRaster baked;
        GrayTileRaster? bakedMask = null;
        if (session.GetLayerMask(layerId) is { } mask)
        {
            bakedMask = TransformCachedMask(mask, transform, session.Width, session.Height);
            TileRaster visible = layer.MaskEnabled ? RasterCompositor.ApplyMask(source, mask) : source;
            TileRaster transformedVisible = TransformCachedRaster(visible, transform, session.Width, session.Height);
            baked = layer.MaskEnabled ? RestoreMaskedRaster(transformedVisible, bakedMask) : transformedVisible;
        }
        else
        {
            baked = TransformCachedRaster(source, transform, session.Width, session.Height);
        }
        session.ReplaceLayerTransformWithRaster(layerId, baked, bakedMask);
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
        if (!layer.All(pair => new[] { "blendMode", "id", "imageFile", "isGroup", "isVisible", "maskEnabled", "maskFile", "maskSourceID", "name", "opacity", "transform" }.Contains(pair.Key)) ||
            layer["imageFile"] is null || layer["isVisible"] is null ||
            layer["maskEnabled"] is not null && layer["maskFile"] is null ||
            layer["maskSourceID"] is { } source && !Guid.TryParse(source.GetValue<string>(), out _) ||
            layer["isGroup"] is { } group && group.GetValue<bool>() ||
            layer["opacity"] is { } opacity && (!double.IsFinite(opacity.GetValue<double>()) || opacity.GetValue<double>() is < 0 or > 1) ||
            layer["blendMode"] is { } blend && !ProjectSession.SupportedBlendModes.Contains(blend.GetValue<string>())) return false;
        var transform = layer["transform"]?.AsObject();
        if (transform is null || !transform.All(pair => new[] { "flipX", "flipY", "origin", "rotation", "sampling", "size" }.Contains(pair.Key))) return false;
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        if (origin?.Count != 2 || size?.Count != 2) return false;
        double x = origin[0]!.GetValue<double>(), y = origin[1]!.GetValue<double>();
        double targetWidth = size[0]!.GetValue<double>(), targetHeight = size[1]!.GetValue<double>();
        double rotation = transform["rotation"]?.GetValue<double>() ?? 0;
        return double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(targetWidth) &&
            double.IsFinite(targetHeight) && double.IsFinite(rotation) && targetWidth > 0 && targetHeight > 0;
    }

    private static void EncodeRaster(TileRaster raster, string path)
    {
        ImageCodec.SavePng(raster, path);
        var decoded = ImageCodec.Load(path);
        if (decoded.Width != raster.Width || decoded.Height != raster.Height)
            throw new InvalidDataException("Encoded project image has the wrong dimensions.");
    }

    private static void EncodeMask(GrayTileRaster mask, string path)
    {
        ImageCodec.SaveGrayMask(mask, path);
        GrayTileRaster decoded = ImageCodec.LoadGrayMask(path);
        if (decoded.Width != mask.Width || decoded.Height != mask.Height)
            throw new InvalidDataException("Encoded project mask has the wrong dimensions.");
    }
}
