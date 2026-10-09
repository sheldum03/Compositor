namespace Compositor.Core;

public sealed class TileRaster
{
    public const int TileSize = 256;
    private readonly Dictionary<int, byte[]> tiles;
    private int Columns => (Width + TileSize - 1) / TileSize;
    public int Width { get; }
    public int Height { get; }
    public int TileCount => tiles.Count;
    public long StoredBytes => tiles.Values.Sum(tile => (long)tile.Length);
    internal IEnumerable<byte[]> Buffers => tiles.Values;

    public TileRaster(int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        tiles = [];
    }

    private TileRaster(int width, int height, Dictionary<int, byte[]> tiles)
    {
        Width = width;
        Height = height;
        this.tiles = tiles;
    }

    public (int Width, int Height) TileDimensions(int column, int row)
    {
        if (column < 0 || row < 0 || (long)column * TileSize >= Width || (long)row * TileSize >= Height)
            throw new ArgumentOutOfRangeException(nameof(column));
        return (Math.Min(TileSize, Width - column * TileSize), Math.Min(TileSize, Height - row * TileSize));
    }

    public byte[] ReadTileCopy(int column, int row)
    {
        var size = TileDimensions(column, row);
        return tiles.TryGetValue(row * Columns + column, out var tile)
            ? (byte[])tile.Clone() : new byte[size.Width * size.Height * 4];
    }

    public (byte Red, byte Green, byte Blue, byte Alpha) ReadPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(x));
        int column = x / TileSize, row = y / TileSize;
        var size = TileDimensions(column, row);
        byte[] tile = ReadTileCopy(column, row);
        int offset = ((y - row * TileSize) * size.Width + x - column * TileSize) * 4;
        int alpha = tile[offset + 3];
        if (alpha == 0) return (0, 0, 0, 0);
        static byte Unpremultiply(byte value, int alpha) =>
            (byte)Math.Clamp((value * 255 + alpha / 2) / alpha, 0, 255);
        return (Unpremultiply(tile[offset], alpha), Unpremultiply(tile[offset + 1], alpha),
            Unpremultiply(tile[offset + 2], alpha), (byte)alpha);
    }

    public TileRaster ReplaceTile(int column, int row, ReadOnlySpan<byte> premultipliedRgba)
    {
        var size = TileDimensions(column, row);
        if (premultipliedRgba.Length != size.Width * size.Height * 4)
            throw new ArgumentException("Tile data length does not match its dimensions.", nameof(premultipliedRgba));
        var next = new Dictionary<int, byte[]>(tiles)
        {
            [row * Columns + column] = premultipliedRgba.ToArray()
        };
        return new TileRaster(Width, Height, next);
    }

    public int SharedTileCount(TileRaster other)
    {
        if (Width != other.Width || Height != other.Height) return 0;
        return tiles.Count(pair => other.tiles.TryGetValue(pair.Key, out var tile) && ReferenceEquals(pair.Value, tile));
    }
}
