using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

public static class ImageProjectWorkflow
{
    private sealed record FlatLayerRender(JsonObject Manifest, Guid Id, TileRaster? Raster,
        ExposureSettings? Exposure, LevelsSettings? Levels, HueSaturationSettings? HueSaturation,
        CurvesSettings? Curves, GradientMapSettings? GradientMap, GaussianBlurSettings? GaussianBlur,
        MotionBlurSettings? MotionBlur, NoiseSettings? Noise, LensCorrectionSettings? LensCorrection, GrainSettings? Grain);

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
                    TileRaster loadedRaster = ImageCodec.Load(image);
                    ProjectStore.CheckAssetHash(session, name, image);
                    rasters.Add(id, loadedRaster);
                }
                if (layer["maskFile"] is { } maskNode)
                {
                    string maskName = maskNode.GetValue<string>();
                    string maskPath = Path.Combine(session.SourceDirectory, "images", maskName);
                    ProjectStore.CheckAssetHash(session, maskName, maskPath);
                    GrayTileRaster loadedMask = ImageCodec.LoadGrayMask(maskPath);
                    ProjectStore.CheckAssetHash(session, maskName, maskPath);
                    masks.Add(id, loadedMask);
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

    public static TileRaster RenderFlatNormalLayers(ProjectSession session, IReadOnlySet<Guid> layerIds)
    {
        ArgumentNullException.ThrowIfNull(layerIds);
        if (!session.CanEdit) throw new NotSupportedException("Layer subset rendering requires an editable project.");
        if (session.HasGroups) throw new NotSupportedException("Layer subset rendering does not support groups.");
        if (layerIds.Count == 0 || session.Layers.Any(layer => layerIds.Contains(layer.Id) &&
            (layer.IsGroup || layer.IsAdjustment)))
            throw new ArgumentException("The selected layer set is invalid.", nameof(layerIds));
        return RenderFlatNormalCore(session, null, null, null, layerIds);
    }

    public static TileRaster RenderFlatNormalBeforeLayer(ProjectSession session, Guid layerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.CanEdit) throw new NotSupportedException("Layer subset rendering requires an editable project.");
        if (session.HasGroups) throw new NotSupportedException("Rendering before a layer does not support groups.");
        int index = session.Layers.ToList().FindIndex(layer => layer.Id == layerId);
        if (index < 0) throw new ArgumentException("Layer does not belong to this project.", nameof(layerId));
        var layerIds = session.Layers.Take(index).Select(layer => layer.Id).ToHashSet();
        return RenderFlatNormalCore(session, null, null, null, layerIds);
    }

    public static TileRaster RenderLayersForMerge(ProjectSession session, IReadOnlyList<Guid> layerIds)
    {
        ArgumentNullException.ThrowIfNull(layerIds);
        if (!session.CanEdit) throw new NotSupportedException("Layer merge rendering requires an editable project.");
        Guid[] distinctIds = layerIds.Distinct().ToArray();
        if (distinctIds.Length < 2) throw new InvalidOperationException("Please select at least two layers.");
        FlatLayerInfo[] layers = session.Layers.ToArray();
        (FlatLayerInfo Layer, int Index)[] selected = distinctIds.Select(id =>
        {
            int index = Array.FindIndex(layers, layer => layer.Id == id);
            if (index < 0) throw new ArgumentException("Layer does not belong to this project.", nameof(layerIds));
            return (Layer: layers[index], Index: index);
        }).OrderBy(item => item.Index).ToArray();
        Guid? parentId = selected[0].Layer.ParentId;
        if (selected.Any(item => item.Layer.ParentId != parentId))
            throw new NotSupportedException("Only sibling layers can be merged.");
        int[] siblingIndexes = layers.Select((layer, index) => (layer, index))
            .Where(item => item.layer.ParentId == parentId).Select(item => item.index).ToArray();
        int[] siblingPositions = selected.Select(item => Array.IndexOf(siblingIndexes, item.Index))
            .OrderBy(index => index).ToArray();
        if (siblingPositions.Any(index => index < 0) ||
            siblingPositions[^1] - siblingPositions[0] + 1 != siblingPositions.Length)
            throw new InvalidOperationException("Only contiguous sibling layers can be merged.");

        return RenderCachedCore(session, useLoadedAssets: true,
            renderRoots: selected.Select(item => item.Layer.Id).ToHashSet());
    }

    public static TileRaster RenderLayerForCopy(ProjectSession session, Guid layerId)
    {
        if (!session.CanEdit) throw new NotSupportedException("Layer copy requires an editable project.");
        FlatLayerInfo target = session.Layers.SingleOrDefault(layer => layer.Id == layerId)
            ?? throw new ArgumentException("Layer does not belong to this project.", nameof(layerId));
        if (session.HasGroups)
        {
            if (target.IsGroup)
                return RenderCachedCore(session, useLoadedAssets: true,
                    renderRoots: new HashSet<Guid> { layerId });
            if (HasCrossParentClipping(session, target))
            {
                ValidateCrossParentGroupedLeafCopy(session, target);
                return RenderCachedCore(session, useLoadedAssets: true,
                    renderRoots: new HashSet<Guid> { layerId },
                    maskSourceOverrides: BuildCrossParentMaskSourceOverrides(session, target));
            }
            IReadOnlyList<FlatLayerInfo> groupedStack = ValidateGroupedLeafCopy(session, target,
                allowTransformedAncestors: true);
            if (HasTransformedGroupedAncestor(session, target))
                return RenderCachedCore(session, useLoadedAssets: true,
                    renderRoots: groupedStack.Select(layer => layer.Id).ToHashSet());
            if (groupedStack.Count > 1)
                return RenderGroupedClippingStackForCopy(session, groupedStack);
        }
        if (target.IsGroup || target.IsAdjustment)
            throw new NotSupportedException("Layer copy only supports raster layers.");

        var resolving = new HashSet<Guid>();
        TileRaster Resolve(Guid id)
        {
            if (!resolving.Add(id)) throw new NotSupportedException("Clipping relationships contain a cycle.");
            FlatLayerInfo layer = session.Layers.Single(item => item.Id == id);
            TileRaster raster = session.GetLayerRaster(id);
            if (layer.HasMask && layer.MaskEnabled)
            {
                GrayTileRaster mask = session.GetLayerMask(id)
                    ?? throw new InvalidDataException("Layer mask asset is missing.");
                var manifest = session.Current["layers"]!.AsArray().Single(node =>
                    Guid.Parse(node!["id"]!.GetValue<string>()) == id)!.AsObject();
                raster = RasterCompositor.ApplyMask(raster, ResolveLayerMask(mask, manifest, raster.Width, raster.Height));
            }
            if (layer.MaskSourceId is { } sourceId)
            {
                FlatLayerInfo source = session.Layers.Single(item => item.Id == sourceId);
                raster = RasterCompositor.ApplyAlphaMask(raster, Resolve(sourceId), source.Opacity);
            }
            if (!session.IsLayerTransformIdentity(id))
            {
                JsonObject manifest = session.Current["layers"]!.AsArray().Single(node =>
                    Guid.Parse(node!["id"]!.GetValue<string>()) == id)!.AsObject();
                raster = TransformCachedRaster(raster, manifest["transform"]!.AsObject(), session.Width, session.Height);
            }
            resolving.Remove(id);
            return raster;
        }

        // Layer via Copy takes the layer's own pixels in document coordinates. The source
        // opacity/blend mode are not baked into the new default-Normal raster layer.
        return Resolve(layerId);
    }

    public static bool CanRenderLayerForCopy(ProjectSession session, Guid layerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        FlatLayerInfo? target = session.Layers.SingleOrDefault(layer => layer.Id == layerId);
        if (target is null || !session.CanEdit) return false;
        try
        {
            if (session.HasGroups && target.IsGroup) return true;
            if (target.IsGroup) return false;
            if (session.HasGroups)
            {
                if (HasCrossParentClipping(session, target)) ValidateCrossParentGroupedLeafCopy(session, target);
                else ValidateGroupedLeafCopy(session, target, allowTransformedAncestors: true);
            }
            else if (target.HasMask && session.GetLayerMask(target.Id) is null) return false;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException or NotSupportedException)
        {
            return false;
        }
    }

    public static int GetGroupedLayerCopyInsertionIndex(ProjectSession session, Guid layerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        FlatLayerInfo target = session.Layers.SingleOrDefault(layer => layer.Id == layerId)
            ?? throw new ArgumentException("Layer does not belong to this project.", nameof(layerId));
        IReadOnlyList<FlatLayerInfo> stack = ValidateGroupedLeafCopy(session, target);
        return session.Layers.ToList().FindIndex(layer => layer.Id == stack[^1].Id) + 1;
    }

    public static bool GroupedLayerCopyRequiresRootInsertion(ProjectSession session, Guid layerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        FlatLayerInfo target = session.Layers.SingleOrDefault(layer => layer.Id == layerId)
            ?? throw new ArgumentException("Layer does not belong to this project.", nameof(layerId));
        if (target.IsGroup) return false;
        if (HasCrossParentClipping(session, target))
        {
            ValidateCrossParentGroupedLeafCopy(session, target);
            return true;
        }
        _ = ValidateGroupedLeafCopy(session, target, allowTransformedAncestors: true);
        return HasTransformedGroupedAncestor(session, target);
    }

    private static bool HasCrossParentClipping(ProjectSession session, FlatLayerInfo target)
    {
        Guid? targetParent = target.ParentId;
        if (targetParent is null) return false;
        var seen = new HashSet<Guid>();
        FlatLayerInfo current = target;
        while (current.MaskSourceId is { } sourceId)
        {
            if (!seen.Add(current.Id)) throw new NotSupportedException("Clipping relationships contain a cycle.");
            FlatLayerInfo source = session.Layers.SingleOrDefault(layer => layer.Id == sourceId)
                ?? throw new InvalidDataException("Clipping source is missing.");
            if (source.ParentId != targetParent) return true;
            current = source;
        }
        return false;
    }

    private static void ValidateCrossParentGroupedLeafCopy(ProjectSession session, FlatLayerInfo target)
    {
        if (target.ParentId is null)
            throw new NotSupportedException("A grouped layer copy must stay inside a parent group.");
        if (target.IsGroup || target.IsText)
            throw new NotSupportedException("Only raster layers support cross-parent clipping copy.");

        var seen = new HashSet<Guid>();
        FlatLayerInfo current = target;
        while (true)
        {
            if (!seen.Add(current.Id))
                throw new NotSupportedException("Clipping relationships contain a cycle.");
            if (current.IsGroup || current.IsText)
                throw new NotSupportedException("Only raster clipping relationships support cross-parent copy.");
            if (current.HasMask && session.GetLayerMask(current.Id) is null)
                throw new InvalidDataException("Source layer mask asset is missing.");
            if (current.MaskSourceId is not { } sourceId) break;
            FlatLayerInfo source = session.Layers.SingleOrDefault(layer => layer.Id == sourceId)
                ?? throw new InvalidDataException("Clipping source is missing.");
            int sourceIndex = session.Layers.ToList().FindIndex(layer => layer.Id == source.Id);
            int currentIndex = session.Layers.ToList().FindIndex(layer => layer.Id == current.Id);
            if (sourceIndex < 0 || currentIndex < 0 || sourceIndex >= currentIndex)
                throw new NotSupportedException("A clipping source must precede its target.");
            if (source.ParentId != target.ParentId)
                ValidateExternalSourceContext(session, source);
            current = source;
        }
    }

    private static void ValidateExternalSourceContext(ProjectSession session, FlatLayerInfo source)
    {
        Guid? parentId = source.ParentId;
        while (parentId is { } groupId)
        {
            FlatLayerInfo group = session.Layers.SingleOrDefault(layer => layer.Id == groupId)
                ?? throw new InvalidDataException("Clipping source parent is missing.");
            if (!group.IsGroup)
                throw new InvalidDataException("Clipping source parent is not a group.");
            if (group.HasMask && session.GetLayerMask(group.Id) is null)
                throw new InvalidDataException("Cross-parent clipping source group mask asset is missing.");
            parentId = group.ParentId;
        }
    }

    private static IReadOnlyDictionary<Guid, TileRaster> BuildCrossParentMaskSourceOverrides(
        ProjectSession session, FlatLayerInfo target)
    {
        var overrides = new Dictionary<Guid, TileRaster>();
        var rendering = new HashSet<Guid>();

        TileRaster RenderExternal(Guid sourceId)
        {
            if (overrides.TryGetValue(sourceId, out TileRaster? cached)) return cached;
            if (!rendering.Add(sourceId))
                throw new NotSupportedException("Clipping relationships contain a cycle.");

            FlatLayerInfo source = session.Layers.Single(layer => layer.Id == sourceId);
            PrepareExternalDependencies(source);
            TileRaster raster = RenderCachedCore(session, useLoadedAssets: true,
                renderRoots: new HashSet<Guid> { sourceId }, maskSourceOverrides: overrides);
            rendering.Remove(sourceId);
            overrides[sourceId] = raster;
            return raster;
        }

        void PrepareExternalDependencies(FlatLayerInfo source)
        {
            if (source.MaskSourceId is not { } dependencyId) return;
            FlatLayerInfo dependency = session.Layers.Single(layer => layer.Id == dependencyId);
            if (dependency.ParentId != source.ParentId)
                _ = RenderExternal(dependency.Id);
            else
                PrepareExternalDependencies(dependency);
        }

        FlatLayerInfo current = target;
        while (current.MaskSourceId is { } sourceId)
        {
            FlatLayerInfo source = session.Layers.Single(layer => layer.Id == sourceId);
            if (source.ParentId != target.ParentId)
                _ = RenderExternal(source.Id);
            current = source;
        }
        return overrides;
    }

    private static IReadOnlyList<FlatLayerInfo> ValidateGroupedLeafCopy(ProjectSession session,
        FlatLayerInfo target, bool allowTransformedAncestors = false)
    {
        if (target.ParentId is not { } parentId)
            throw new NotSupportedException("A grouped layer copy must stay inside its parent group.");
        if (target.IsText)
            throw new NotSupportedException("Text layers require the text renderer for grouped layer copy.");
        FlatLayerInfo root = target;
        var sourceChain = new HashSet<Guid>();
        while (root.MaskSourceId is { } sourceId)
        {
            if (!sourceChain.Add(root.Id))
                throw new NotSupportedException("Clipping relationships contain a cycle.");
            FlatLayerInfo source = session.Layers.SingleOrDefault(layer => layer.Id == sourceId)
                ?? throw new InvalidDataException("Clipping source is missing.");
            if (source.ParentId != parentId)
                throw new NotSupportedException("A grouped clipping stack must remain in one parent group.");
            root = source;
        }
        int rootIndex = session.Layers.ToList().FindIndex(layer => layer.Id == root.Id);
        if (rootIndex < 0) throw new ArgumentException("Layer does not belong to this project.", nameof(target));
        var stack = new List<FlatLayerInfo>();
        for (FlatLayerInfo? current = target; current is not null;)
        {
            stack.Add(current);
            current = current.MaskSourceId is { } sourceId
                ? session.Layers.SingleOrDefault(layer => layer.Id == sourceId)
                : null;
        }
        stack.Reverse();
        var stackIds = stack.Select(layer => layer.Id).ToHashSet();
        foreach (FlatLayerInfo layer in stack)
        {
            if (layer.IsGroup || layer.IsText)
                throw new NotSupportedException("Only raster clipping stacks can be copied inside a group.");
            if (!session.IsLayerTransformIdentity(layer.Id))
                throw new NotSupportedException("A transformed grouped layer must be copied with its group.");
            if (layer.HasMask && session.GetLayerMask(layer.Id) is null)
                throw new InvalidDataException("Source layer mask asset is missing.");
            if (layer.MaskSourceId is { } source)
            {
                int sourceIndex = session.Layers.ToList().FindIndex(candidate => candidate.Id == source);
                int layerIndex = session.Layers.ToList().FindIndex(candidate => candidate.Id == layer.Id);
                if (!stackIds.Contains(source) || sourceIndex < rootIndex || sourceIndex >= layerIndex)
                    throw new NotSupportedException("A grouped clipping stack must reference an earlier layer in the same stack.");
            }
        }
        Guid? currentId = parentId;
        while (currentId is { } groupId)
        {
            FlatLayerInfo group = session.Layers.SingleOrDefault(layer => layer.Id == groupId)
                ?? throw new InvalidDataException("Grouped layer parent is missing.");
            if (!group.IsGroup)
                throw new InvalidDataException("Grouped layer parent is not a group.");
            if (!allowTransformedAncestors && !session.IsGroupTransformIdentity(group.Id))
                throw new NotSupportedException("A layer inside a transformed group must be copied with its group.");
            if (group.HasMask && group.MaskEnabled && session.GetLayerMask(group.Id) is null)
                throw new InvalidDataException("Group mask asset is missing.");
            currentId = group.ParentId;
        }
        return stack;
    }

    private static bool HasTransformedGroupedAncestor(ProjectSession session, FlatLayerInfo target)
    {
        Guid? currentId = target.ParentId;
        while (currentId is { } groupId)
        {
            FlatLayerInfo group = session.Layers.Single(layer => layer.Id == groupId);
            if (!group.IsGroup) throw new InvalidDataException("Grouped layer parent is not a group.");
            if (!session.IsGroupTransformIdentity(group.Id)) return true;
            currentId = group.ParentId;
        }
        return false;
    }

    private static TileRaster RenderGroupedClippingStackForCopy(ProjectSession session,
        IReadOnlyList<FlatLayerInfo> stack)
    {
        int width = session.Width, height = session.Height;
        var resolving = new HashSet<Guid>();
        TileRaster ResolveLeaf(Guid id)
        {
            if (!resolving.Add(id)) throw new NotSupportedException("Clipping relationships contain a cycle.");
            FlatLayerInfo layer = session.Layers.Single(item => item.Id == id);
            TileRaster raster = session.GetLayerRaster(id);
            if (layer.HasMask && layer.MaskEnabled)
            {
                var manifest = session.Current["layers"]!.AsArray().Single(node =>
                    Guid.Parse(node!["id"]!.GetValue<string>()) == id)!.AsObject();
                GrayTileRaster mask = session.GetLayerMask(id) ?? throw new InvalidDataException("Layer mask asset is missing.");
                raster = RasterCompositor.ApplyMask(raster, ResolveLayerMask(mask, manifest, raster.Width, raster.Height));
            }
            resolving.Remove(id);
            return raster;
        }

        FlatLayerInfo baseLayer = stack[0];
        TileRaster basePixels = ResolveLeaf(baseLayer.Id);
        double baseOpacity = baseLayer.Opacity;
        string baseMode = baseLayer.BlendMode;
        ValidateAppearance(baseOpacity, baseMode);
        TileRaster alpha = LayerCompositor.Composite(new TileRaster(width, height), basePixels, baseOpacity, baseMode);
        TileRaster result = UnpremultiplyOpaque(alpha);
        foreach (FlatLayerInfo child in stack.Skip(1))
        {
            ValidateAppearance(child.Opacity, child.BlendMode);
            result = LayerCompositor.Composite(result, ResolveLeaf(child.Id), child.Opacity, child.BlendMode);
        }
        return RestoreAlpha(result, alpha);

        static void ValidateAppearance(double opacity, string mode)
        {
            if (!double.IsFinite(opacity) || opacity is < 0 or > 1 ||
                !ProjectSession.SupportedBlendModes.Contains(mode))
                throw new NotSupportedException("Grouped clipping appearance is not supported.");
        }
    }

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
        GrayTileRaster? overrideMask, IReadOnlySet<Guid>? renderOnly = null)
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
            Guid id = Guid.Parse(layer["id"]!.GetValue<string>());
            if (layer["adjustment"] is { } adjustmentNode)
            {
                string? kind = adjustmentNode["kind"]?.GetValue<string>();
                if (kind == "Exposure" && ExposureSettings.TryRead(adjustmentNode["exposureSettings"], out var exposure))
                    prepared.Add(new FlatLayerRender(layer, id, null, exposure, null, null, null, null, null, null, null, null, null));
                else if (kind == "Levels" && LevelsSettings.TryRead(adjustmentNode["levelsSettings"], out var levels))
                    prepared.Add(new FlatLayerRender(layer, id, null, null, levels, null, null, null, null, null, null, null, null));
                else if (kind == "Hue/Saturation" && HueSaturationSettings.TryRead(adjustmentNode["hueSaturationSettings"], out var hueSaturation))
                    prepared.Add(new FlatLayerRender(layer, id, null, null, null, hueSaturation, null, null, null, null, null, null, null));
                else if (kind == "Curves" && CurvesSettings.TryRead(adjustmentNode["curvesSettings"], out var curves))
                    prepared.Add(new FlatLayerRender(layer, id, null, null, null, null, curves, null, null, null, null, null, null));
                else if (kind == "Gradient Map" && GradientMapSettings.TryRead(adjustmentNode["gradientMapSettings"], out var gradientMap))
                    prepared.Add(new FlatLayerRender(layer, id, null, null, null, null, null, gradientMap, null, null, null, null, null));
                else if (kind == "Gaussian Blur" && GaussianBlurSettings.TryRead(adjustmentNode["gaussianBlurSettings"], out var gaussianBlur))
                    prepared.Add(new FlatLayerRender(layer, id, null, null, null, null, null, null, gaussianBlur, null, null, null, null));
                else if (kind == "Motion Blur" && MotionBlurSettings.TryRead(adjustmentNode["motionBlurSettings"], out var motionBlur))
                    prepared.Add(new FlatLayerRender(layer, id, null, null, null, null, null, null, null, motionBlur, null, null, null));
                else if (kind == "Add Noise" && NoiseSettings.TryRead(adjustmentNode["noiseSettings"], out var noise))
                    prepared.Add(new FlatLayerRender(layer, id, null, null, null, null, null, null, null, null, noise, null, null));
                else if (kind == "Lens Correction" && LensCorrectionSettings.TryRead(adjustmentNode["lensCorrectionSettings"], out var lensCorrection))
                    prepared.Add(new FlatLayerRender(layer, id, null, null, null, null, null, null, null, null, null, lensCorrection, null));
                else if (kind == "Grain" && GrainSettings.TryRead(adjustmentNode["grainSettings"], out var grain))
                    prepared.Add(new FlatLayerRender(layer, id, null, null, null, null, null, null, null, null, null, null, grain));
                else throw new NotSupportedException("Only valid Exposure, Levels, Hue/Saturation, Curves, Gradient Map, Gaussian Blur, Motion Blur, Add Noise, Lens Correction and Grain adjustment layers are supported.");
                continue;
            }
            string imageName = layer["imageFile"]!.GetValue<string>();
            TileRaster raster;
            if (overrideLayerId == id) raster = overrideRaster!;
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
            if (layer["text"] is not null)
            {
                TextLayerMetadata text = session.TextLayers.Single(metadata => metadata.Id == Guid.Parse(layer["id"]!.GetValue<string>()));
                if (TextLayerWorkflow.Inspect(session).Single(status => status.Metadata.Id == text.Id).FontAvailable)
                    raster = TextLayerWorkflow.RenderRaster(text, raster, session.Resolution);
            }
            if (raster.Width != width || raster.Height != height)
                throw new InvalidDataException("Layer image dimensions do not match the canvas.");
            if (layer["maskFile"] is { } maskFile)
            {
                string maskPath = Path.Combine(session.SourceDirectory, "images", maskFile.GetValue<string>());
                Guid layerId = id;
                GrayTileRaster mask = overrideLayerId == layerId && overrideMask is not null
                    ? overrideMask
                    : session.TryGetLoadedLayerMask(layerId, out var loadedMask)
                    ? loadedMask : ImageCodec.LoadGrayMask(maskPath);
                if (session.CanEdit && session.AssetHashes.ContainsKey(maskFile.GetValue<string>()))
                    ProjectStore.CheckAssetHash(session, maskFile.GetValue<string>(), maskPath);
                if (layer["maskEnabled"]?.GetValue<bool>() ?? true)
                    raster = RasterCompositor.ApplyMask(raster, ResolveLayerMask(mask, layer, raster.Width, raster.Height));
            }
            var transform = layer["transform"]!.AsObject();
            if (!IsIdentityTransform(transform, width, height))
                raster = TransformCachedRaster(raster, transform, width, height);
            prepared.Add(new FlatLayerRender(layer, id, raster, null, null, null, null, null, null, null, null, null, null));
        }
        var byId = prepared.ToDictionary(layer => layer.Id);
        var resolved = new Dictionary<Guid, TileRaster>();
        var resolving = new HashSet<Guid>();
        TileRaster Resolve(Guid layerId)
        {
            if (resolved.TryGetValue(layerId, out var cached)) return cached;
            if (!resolving.Add(layerId)) throw new InvalidDataException("Invalid mask source cycle.");
            var layer = byId[layerId];
            TileRaster raster = layer.Raster
                ?? throw new InvalidDataException("Adjustment layers cannot be clipping sources.");
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

        TileRaster RenderClipStack(FlatLayerRender baseLayer, IReadOnlyList<FlatLayerRender> clipped)
        {
            double baseOpacity = baseLayer.Manifest["opacity"]?.GetValue<double>() ?? 1;
            string baseMode = baseLayer.Manifest["blendMode"]?.GetValue<string>() ?? "Normal";
            if (baseLayer.Raster is null) throw new InvalidDataException("Adjustment layers cannot be clipping bases.");
            var stack = LayerCompositor.Composite(new TileRaster(width, height), baseLayer.Raster, baseOpacity, baseMode);
            var alpha = stack;
            stack = UnpremultiplyOpaque(stack);
            foreach (FlatLayerRender child in clipped)
            {
                double opacity = child.Manifest["opacity"]?.GetValue<double>() ?? 1;
                string mode = child.Manifest["blendMode"]?.GetValue<string>() ?? "Normal";
                if (child.Raster is null) throw new InvalidDataException("Adjustment layers cannot be clipped targets.");
                stack = LayerCompositor.Composite(stack, child.Raster, opacity, mode);
            }
            return RestoreAlpha(stack, alpha);
        }

        var result = new TileRaster(width, height);
        for (int index = 0; index < prepared.Count; index++)
        {
            var layer = prepared[index];
            if ((renderOnly is null || renderOnly.Contains(layer.Id)) && layer.Manifest["isVisible"]!.GetValue<bool>())
            {
                if (layer.Exposure is not null || layer.Levels is not null || layer.HueSaturation is not null ||
                    layer.Curves is not null || layer.GradientMap is not null || layer.GaussianBlur is not null ||
                    layer.MotionBlur is not null || layer.Noise is not null || layer.LensCorrection is not null || layer.Grain is not null)
                {
                    result = ApplyAdjustment(result, layer.Exposure, layer.Levels, layer.HueSaturation, layer.Curves, layer.GradientMap, layer.GaussianBlur, layer.MotionBlur, layer.Noise, layer.LensCorrection, layer.Grain,
                        layer.Manifest["opacity"]?.GetValue<double>() ?? 1);
                    continue;
                }
                var clipped = new List<FlatLayerRender>();
                int end = index + 1;
                while (end < prepared.Count && (renderOnly is null || renderOnly.Contains(prepared[end].Id)) &&
                    prepared[end].Manifest["isVisible"]!.GetValue<bool>() &&
                    prepared[end].Manifest["maskSourceID"] is { } source &&
                    Guid.Parse(source.GetValue<string>()) == layer.Id)
                {
                    clipped.Add(prepared[end]);
                    end++;
                }
                TileRaster raster = clipped.Count == 0 ? Resolve(layer.Id) : RenderClipStack(layer, clipped);
                result = LayerCompositor.Composite(result, raster,
                    clipped.Count == 0 ? layer.Manifest["opacity"]?.GetValue<double>() ?? 1 : 1,
                    clipped.Count == 0 ? layer.Manifest["blendMode"]?.GetValue<string>() ?? "Normal" : "Normal");
                index = end - 1;
            }
        }
        return result;

        static TileRaster ApplyAdjustment(TileRaster source, ExposureSettings? exposure, LevelsSettings? levels,
            HueSaturationSettings? hueSaturation, CurvesSettings? curves, GradientMapSettings? gradientMap,
            GaussianBlurSettings? gaussianBlur, MotionBlurSettings? motionBlur, NoiseSettings? noise, LensCorrectionSettings? lensCorrection, GrainSettings? grain, double opacity)
        {
            if (!double.IsFinite(opacity) || opacity is < 0 or > 1)
                throw new InvalidDataException("Adjustment layer opacity is invalid.");
            if (opacity == 0) return source;
            TileRaster adjusted = exposure is { } exposureSettings
                ? RasterCompositor.ApplyExposure(source, exposureSettings)
                : levels is { } levelsSettings
                ? RasterCompositor.ApplyLevels(source, levelsSettings)
                : hueSaturation is { } hueSaturationSettings
                ? RasterCompositor.ApplyHueSaturation(source, hueSaturationSettings)
                : curves is { } curvesSettings
                ? RasterCompositor.ApplyCurves(source, curvesSettings)
                : gradientMap is { } gradientMapSettings
                ? RasterCompositor.ApplyGradientMap(source, gradientMapSettings)
                : gaussianBlur is { } gaussianBlurSettings
                ? RasterCompositor.ApplyGaussianBlur(source, gaussianBlurSettings)
                : motionBlur is { } motionBlurSettings
                ? RasterCompositor.ApplyMotionBlur(source, motionBlurSettings)
                : noise is { } noiseSettings
                ? RasterCompositor.ApplyNoise(source, noiseSettings)
                : lensCorrection is { } lensCorrectionSettings
                ? RasterCompositor.ApplyLensCorrection(source, lensCorrectionSettings)
                : grain is { } grainSettings
                ? RasterCompositor.ApplyGrain(source, grainSettings)
                : throw new InvalidDataException("Adjustment settings are missing.");
            if (opacity == 1) return adjusted;
            var result = new TileRaster(source.Width, source.Height);
            for (int row = 0; row * TileRaster.TileSize < source.Height; row++)
            for (int column = 0; column * TileRaster.TileSize < source.Width; column++)
            {
                byte[] original = source.ReadTileCopy(column, row);
                byte[] changed = adjusted.ReadTileCopy(column, row);
                for (int i = 0; i < original.Length; i += 4)
                    for (int channel = 0; channel < 3; channel++)
                        original[i + channel] = (byte)Math.Round(
                            original[i + channel] * (1 - opacity) + changed[i + channel] * opacity,
                            MidpointRounding.AwayFromZero);
                result = result.ReplaceTile(column, row, original);
            }
            return result;
        }
    }

    private sealed record CachedLayer(JsonObject Manifest, Guid Id, TileRaster? Raster, GrayTileRaster? Mask);

    private static TileRaster RenderCachedCore(ProjectSession session, bool useLoadedAssets = false,
        Guid? overrideLayerId = null, TileRaster? overrideRaster = null, GrayTileRaster? overrideMask = null,
        Guid? rootOnly = null, bool applyRootAppearance = false, IReadOnlySet<Guid>? renderRoots = null,
        IReadOnlyDictionary<Guid, TileRaster>? maskSourceOverrides = null)
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
            }
            if (isGroup)
            {
                if (layer["imageFile"] is not null || layer["adjustment"] is not null)
                    throw new NotSupportedException("Cached group metadata is not supported.");
                var groupTransform = layer["transform"]?.AsObject()
                    ?? throw new NotSupportedException("Cached group transform data is missing.");
                if (mask is not null) mask = ResolveLayerMask(mask, layer, width, height);
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
            if (layer["text"] is not null)
            {
                TextLayerMetadata text = session.TextLayers.Single(metadata => metadata.Id == id);
                TextLayerStatus status = TextLayerWorkflow.Inspect(session).Single(item => item.Metadata.Id == id);
                if (status.FontAvailable) raster = TextLayerWorkflow.RenderRaster(text, raster, session.Resolution);
            }
            if (mask is not null && (layer["maskEnabled"]?.GetValue<bool>() ?? true))
                raster = RasterCompositor.ApplyMask(raster, ResolveLayerMask(mask, layer, raster.Width, raster.Height));
            var layerTransform = layer["transform"]?.AsObject()
                ?? throw new NotSupportedException("Cached layer transform data is missing.");
            raster = TransformCachedRaster(raster, layerTransform, width, height);
            prepared.Add(id, new CachedLayer(layer, id, raster, mask));
        }

        var children = prepared.Values
            .GroupBy(layer => layer.Manifest["parentID"] is { } parent
                ? Guid.Parse(parent.GetValue<string>()) : Guid.Empty)
            .ToDictionary(group => group.Key, group => group.ToArray());
        HashSet<Guid>? renderIds = null;
        if (renderRoots is { Count: > 0 })
        {
            renderIds = renderRoots.ToHashSet();
            foreach (Guid selectedRootId in renderRoots)
            {
                if (!prepared.ContainsKey(selectedRootId))
                    throw new ArgumentException("The requested layer does not exist.", nameof(renderRoots));
                foreach (CachedLayer candidate in prepared.Values)
                {
                    Guid? parentId = candidate.Manifest["parentID"] is { } parent
                        ? Guid.Parse(parent.GetValue<string>()) : null;
                    var seen = new HashSet<Guid>();
                    while (parentId is { } current && seen.Add(current))
                    {
                        if (current == selectedRootId)
                        {
                            renderIds.Add(candidate.Id);
                            break;
                        }
                        parentId = prepared.TryGetValue(current, out var parentLayer) &&
                            parentLayer.Manifest["parentID"] is { } nextParent
                            ? Guid.Parse(nextParent.GetValue<string>()) : null;
                    }
                }
                Guid? ancestorId = prepared[selectedRootId].Manifest["parentID"] is { } parentNode
                    ? Guid.Parse(parentNode.GetValue<string>()) : null;
                var ancestorSeen = new HashSet<Guid>();
                while (ancestorId is { } ancestor && ancestorSeen.Add(ancestor))
                {
                    renderIds.Add(ancestor);
                    ancestorId = prepared.TryGetValue(ancestor, out var ancestorLayer) &&
                        ancestorLayer.Manifest["parentID"] is { } nextParent
                        ? Guid.Parse(nextParent.GetValue<string>()) : null;
                }
            }
        }
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
                TileRaster sourceRaster = maskSourceOverrides is not null &&
                    maskSourceOverrides.TryGetValue(sourceId, out TileRaster? overrideSource)
                    ? overrideSource
                    : ResolveLeaf(sourceId);
                double sourceOpacity = maskSourceOverrides is not null &&
                    maskSourceOverrides.ContainsKey(sourceId)
                    ? 1
                    : source.Manifest["opacity"]?.GetValue<double>() ?? 1;
                raster = RasterCompositor.ApplyAlphaMask(raster, sourceRaster, sourceOpacity);
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
                    if (renderIds is not null && !renderIds.Contains(child.Id)) continue;
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
            if (!prepared.TryGetValue(rootId, out var root))
                throw new ArgumentException("The requested layer does not exist.", nameof(rootOnly));
            TileRaster rootRaster = root.Raster is null
                ? RenderNode(root, Array.Empty<GrayTileRaster>())
                : ResolveLeaf(root.Id);
            return applyRootAppearance
                ? CompositeChild(new TileRaster(width, height), root, rootRaster)
                : rootRaster;
        }

        var output = new TileRaster(width, height);
        if (children.TryGetValue(Guid.Empty, out var roots))
            foreach (var root in roots)
            {
                if (renderIds is not null && !renderIds.Contains(root.Id)) continue;
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
        TileRaster maskRaster = new(source.Width, source.Height);
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

    private static GrayTileRaster ResolveLayerMask(GrayTileRaster source, JsonObject layer, int width, int height)
    {
        var placement = layer["maskPlacement"] as JsonObject;
        if (placement is null && source.Width == width && source.Height == height) return source;
        LayerTransformInfo layerTransform = LayerTransformInfo.Read(layer["transform"]!.AsObject());
        LayerTransformInfo maskTransform = placement is null ? layerTransform : LayerTransformInfo.Read(placement);
        (double X, double Y) Map(double x, double y)
        {
            var document = maskTransform.ToDocument(x, y);
            var unit = layerTransform.FromDocument(document.X, document.Y);
            return (unit.X * width, unit.Y * height);
        }
        var start = Map(0, 0);
        var horizontal = Map(1, 0);
        var vertical = Map(0, 1);
        using var sourceBitmap = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var targetBitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        long edgeTotal = 0, edgeCount = 0;
        for (int row = 0; row * TileRaster.TileSize < source.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < source.Width; column++)
        {
            var size = source.TileDimensions(column, row);
            byte[] coverage = source.ReadTileCopy(column, row);
            byte[] pixels = new byte[size.Width * 4];
            for (int y = 0; y < size.Height; y++)
            {
                for (int x = 0; x < size.Width; x++)
                {
                    byte value = coverage[y * size.Width + x];
                    pixels[x * 4] = pixels[x * 4 + 1] = pixels[x * 4 + 2] = value;
                    pixels[x * 4 + 3] = 255;
                    int sourceX = column * TileRaster.TileSize + x, sourceY = row * TileRaster.TileSize + y;
                    if (sourceX == 0 || sourceX == source.Width - 1 || sourceY == 0 || sourceY == source.Height - 1)
                    {
                        edgeTotal += value;
                        edgeCount++;
                    }
                }
                Marshal.Copy(pixels, 0, sourceBitmap.GetPixels() +
                    (row * TileRaster.TileSize + y) * sourceBitmap.RowBytes + column * TileRaster.TileSize * 4, pixels.Length);
            }
        }
        using (var canvas = new SKCanvas(targetBitmap))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
        {
            canvas.Clear(edgeTotal * 2 >= edgeCount * 255 ? SKColors.White : SKColors.Black);
            canvas.SetMatrix(new SKMatrix
            {
                ScaleX = (float)((horizontal.X - start.X) / source.Width),
                SkewY = (float)((horizontal.Y - start.Y) / source.Width),
                SkewX = (float)((vertical.X - start.X) / source.Height),
                ScaleY = (float)((vertical.Y - start.Y) / source.Height),
                TransX = (float)start.X, TransY = (float)start.Y, Persp2 = 1
            });
            canvas.DrawBitmap(sourceBitmap, 0, 0, paint);
        }
        byte[] result = new byte[width * height];
        byte[] targetRow = new byte[width * 4];
        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(targetBitmap.GetPixels() + y * targetBitmap.RowBytes, targetRow, 0, targetRow.Length);
            for (int x = 0; x < width; x++) result[y * width + x] = targetRow[x * 4];
        }
        return GrayTileRaster.FromCoverage(width, height, result);
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
        foreach (var layer in session.Layers.Where(layer => !layer.IsGroup && !layer.IsAdjustment))
            session.GetLayerRaster(layer.Id);
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
        var manifest = session.Current["layers"]!.AsArray()
            .Single(node => Guid.Parse(node!["id"]!.GetValue<string>()) == layerId)!.AsObject();
        var transform = manifest["transform"]!.AsObject();
        TileRaster source = session.GetLayerRaster(layerId);
        TileRaster baked;
        GrayTileRaster? bakedMask = null;
        if (session.GetLayerMask(layerId) is { } mask)
        {
            mask = ResolveLayerMask(mask, manifest, source.Width, source.Height);
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

    public static void ApplyFreeDistort(ProjectSession session, Guid layerId,
        IReadOnlyList<(double X, double Y)> corners)
    {
        if (!session.CanEdit) throw new NotSupportedException("This project is read-only.");
        FlatLayerInfo layer = session.Layers.SingleOrDefault(item => item.Id == layerId)
            ?? throw new ArgumentException("Layer does not belong to this project.", nameof(layerId));
        if (layer.IsGroup || layer.IsAdjustment || layer.IsText || layer.HasMask)
            throw new NotSupportedException("自由扭曲目前只支持无组、无蒙版的平面栅格图层。");
        TileRaster source = session.GetLayerRaster(layerId);
        if (!session.IsLayerTransformIdentity(layerId))
        {
            JsonObject manifest = session.Current["layers"]!.AsArray()
                .Single(node => Guid.Parse(node!["id"]!.GetValue<string()) == layerId)!.AsObject();
            source = TransformCachedRaster(source, manifest["transform"]!.AsObject(), session.Width, session.Height);
        }
        TileRaster warped = RasterCompositor.ApplyPerspectiveWarp(source, corners, session.Width, session.Height);
        session.ReplaceLayerTransformWithRaster(layerId, warped, null);
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
        if (!layer.All(pair => new[] { "adjustment", "blendMode", "id", "imageFile", "isGroup", "isVisible", "maskEnabled", "maskFile", "maskLinked", "maskPlacement", "maskSourceID", "name", "opacity", "shape", "text", "transform" }.Contains(pair.Key)) ||
            layer["isVisible"] is null ||
            layer["maskEnabled"] is not null && layer["maskFile"] is null ||
            layer["maskLinked"] is not null && layer["maskFile"] is null ||
            layer["maskPlacement"] is not null && layer["maskFile"] is null ||
            layer["maskSourceID"] is { } source && !Guid.TryParse(source.GetValue<string>(), out _) ||
            layer["isGroup"] is { } group && group.GetValue<bool>() ||
            layer["opacity"] is { } opacity && (!double.IsFinite(opacity.GetValue<double>()) || opacity.GetValue<double>() is < 0 or > 1) ||
            layer["blendMode"] is { } blend && !ProjectSession.SupportedBlendModes.Contains(blend.GetValue<string>())) return false;
        if (layer["adjustment"] is { } adjustment)
        {
            var node = adjustment.AsObject();
            string? kind = node["kind"]?.GetValue<string>();
            bool settingsValid = kind switch
            {
                "Exposure" => ExposureSettings.TryRead(node["exposureSettings"], out _),
                "Levels" => LevelsSettings.TryRead(node["levelsSettings"], out _),
                "Hue/Saturation" => HueSaturationSettings.TryRead(node["hueSaturationSettings"], out _),
                "Curves" => CurvesSettings.TryRead(node["curvesSettings"], out _),
                "Gradient Map" => GradientMapSettings.TryRead(node["gradientMapSettings"], out _),
                "Gaussian Blur" => GaussianBlurSettings.TryRead(node["gaussianBlurSettings"], out _),
                "Motion Blur" => MotionBlurSettings.TryRead(node["motionBlurSettings"], out _),
                "Add Noise" => NoiseSettings.TryRead(node["noiseSettings"], out _),
                "Lens Correction" => LensCorrectionSettings.TryRead(node["lensCorrectionSettings"], out _),
                "Grain" => GrainSettings.TryRead(node["grainSettings"], out _),
                _ => false
            };
            if (!settingsValid || layer["blendMode"]?.GetValue<string>() is { } adjustmentBlend && adjustmentBlend != "Normal" ||
                layer["imageFile"] is not null || layer["text"] is not null ||
                layer["maskFile"] is not null || layer["maskSourceID"] is not null) return false;
        }
        else if (layer["imageFile"] is null) return false;
        if (layer["shape"] is { } shape && (!ShapeSettings.TryRead(shape, out _) ||
            layer["adjustment"] is not null || layer["text"] is not null)) return false;
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
