using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Compositor.Core;

namespace Compositor.App;

public static class RasterBitmap
{
    public static WriteableBitmap Create(TileRaster raster)
    {
        var bitmap = new WriteableBitmap(new PixelSize(raster.Width, raster.Height), new Vector(96, 96),
            PixelFormat.Rgba8888, AlphaFormat.Premul);
        try
        {
            using var frame = bitmap.Lock();
            for (int row = 0; row * TileRaster.TileSize < raster.Height; row++)
            for (int column = 0; column * TileRaster.TileSize < raster.Width; column++)
            {
                var size = raster.TileDimensions(column, row);
                byte[] bytes = raster.ReadTileCopy(column, row);
                for (int y = 0; y < size.Height; y++)
                    Marshal.Copy(bytes, y * size.Width * 4,
                        frame.Address + (row * TileRaster.TileSize + y) * frame.RowBytes + column * TileRaster.TileSize * 4,
                        size.Width * 4);
            }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
