namespace Compositor.Core;

public sealed class GrayTileRaster
{
    private readonly Dictionary<int, byte[]> tiles;
    private int Columns => (Width + TileRaster.TileSize - 1) / TileRaster.TileSize;
    public int Width { get; }
    public int Height { get; }

    public GrayTileRaster(int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        tiles = [];
    }

    private GrayTileRaster(int width, int height, Dictionary<int, byte[]> tiles)
    {
        Width = width;
        Height = height;
        this.tiles = tiles;
    }

    public (int Width, int Height) TileDimensions(int column, int row)
    {
        if (column < 0 || row < 0 || (long)column * TileRaster.TileSize >= Width || (long)row * TileRaster.TileSize >= Height)
            throw new ArgumentOutOfRangeException(nameof(column));
        return (Math.Min(TileRaster.TileSize, Width - column * TileRaster.TileSize),
            Math.Min(TileRaster.TileSize, Height - row * TileRaster.TileSize));
    }

    public byte[] ReadTileCopy(int column, int row)
    {
        var size = TileDimensions(column, row);
        return tiles.TryGetValue(row * Columns + column, out var tile)
            ? (byte[])tile.Clone() : new byte[size.Width * size.Height];
    }

    public GrayTileRaster ReplaceTile(int column, int row, ReadOnlySpan<byte> coverage)
    {
        var size = TileDimensions(column, row);
        if (coverage.Length != size.Width * size.Height)
            throw new ArgumentException("Mask tile data length does not match its dimensions.", nameof(coverage));
        var next = new Dictionary<int, byte[]>(tiles)
        {
            [row * Columns + column] = coverage.ToArray()
        };
        return new GrayTileRaster(Width, Height, next);
    }
}
