using System.Text.Json;
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

    private static void EncodeRaster(TileRaster raster, string path)
    {
        ImageCodec.SavePng(raster, path);
        var decoded = ImageCodec.Load(path);
        if (decoded.Width != raster.Width || decoded.Height != raster.Height)
            throw new InvalidDataException("Encoded project image has the wrong dimensions.");
    }
}
