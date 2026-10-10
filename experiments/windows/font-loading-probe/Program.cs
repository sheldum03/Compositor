using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using SkiaSharp;

if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--raw")
    throw new ArgumentException("Usage: FontLoadingProbe <font-fixtures-directory> [--raw]");
AppBuilder.Configure<Application>().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
bool raw = args.Length == 2;
var collection = new LocalFonts(raw);
FontManager.Current.AddFontCollection(collection);
var results = new List<object>();
int failed = 0;
foreach (var (file, index, weight, advance) in new[] {
    ("fixture.ttf", 0, 400, 60d), ("fixture.otf", 0, 400, 60d),
    ("two-faces.ttc", 0, 400, 60d), ("two-faces.ttc", 1, 700, 80d) })
{
    string path = Path.Combine(args[0], file);
    string beforeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    var face = collection.Load(path, index);
    using var indexed = SKTypeface.FromFile(path, index);
    using var indexedFont = new SKFont(indexed, 100) { LinearMetrics = true };
    double skiaIndexedAdvance = indexed is null ? -1 : indexedFont.MeasureText(new ushort[] { indexedFont.GetGlyph('A') });
    bool loaded = face is not null;
    double actualAdvance = loaded ? face!.GetGlyphAdvance(face.GetGlyph('A')) * 100d / face.Metrics.DesignEmHeight : -1;
    using var layout = loaded && (int)face!.Weight == weight ? new TextLayout("AB",
        new Typeface(new FontFamily(collection.Key + "#" + face.FamilyName), weight: (FontWeight)weight),
        100, Brushes.Black) : null;
    bool exactFace = layout is not null && layout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>()
        .All(run => ReferenceEquals(run.GlyphRun.GlyphTypeface, face));
    bool sourceUnchanged = beforeHash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    bool passed = loaded && (int)face!.Weight == weight && actualAdvance == advance
        && layout?.WidthIncludingTrailingWhitespace == 2 * advance && exactFace && sourceUnchanged;
    results.Add(new { file, index, expectedWeight = weight, expectedAdvance = advance, loaded,
        family = face?.FamilyName, actualWeight = (int?)face?.Weight, actualAdvance,
        skiaIndexedWeight = indexed?.FontStyle.Weight, skiaIndexedAdvance,
        layoutWidth = layout?.WidthIncludingTrailingWhitespace, exactFace, sourceUnchanged,
        sourceSha256 = beforeHash.ToLowerInvariant(), passed });
    if (!passed) failed++;
}
var rejectedFiles = new List<string>();
foreach (string file in new[] { "empty.otf", "damaged.ttf", "truncated.ttc" })
{
    try
    {
        if (collection.Load(Path.Combine(args[0], file), 0) is null) rejectedFiles.Add(file);
        else failed++;
    }
    catch (InvalidDataException) { rejectedFiles.Add(file); }
}
Console.WriteLine(JsonSerializer.Serialize(new { status = failed == 0 ? "passed" : "failed",
    windowsExecuted = OperatingSystem.IsWindows(), processId = Environment.ProcessId,
    runtime = Environment.Version.ToString(), raw, results, rejectedFiles },
    new JsonSerializerOptions { WriteIndented = true }));
return failed == 0 ? 0 : 1;

sealed class LocalFonts(bool raw) : FontCollectionBase
{
    private IFontManagerImpl manager = null!;
    private readonly List<FontFamily> families = [];
    public override Uri Key { get; } = new("fonts:compositor-probe");
    public override int Count => families.Count;
    public override FontFamily this[int index] => families[index];
    public override void Initialize(IFontManagerImpl fontManager) => manager = fontManager;
    public IGlyphTypeface? Load(string path, int faceIndex)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (!raw) SelectFixtureFace(bytes, faceIndex);
        using var stream = new MemoryStream(bytes, writable: false);
        if (!manager.TryCreateGlyphTypeface(stream, FontSimulations.None, out var face)) return null;
        var cache = _glyphTypefaceCache.GetOrAdd(face.FamilyName, _ =>
        {
            families.Add(new FontFamily(Key + "#" + face.FamilyName));
            return new();
        });
        var key = new FontCollectionKey(face.Style, face.Weight, face.Stretch);
        if (!cache.TryAdd(key, face)) { face.Dispose(); return cache[key]; }
        return face;
    }
    public override bool TryGetGlyphTypeface(string familyName, FontStyle style, FontWeight weight,
        FontStretch stretch, [NotNullWhen(true)] out IGlyphTypeface? glyphTypeface)
    {
        glyphTypeface = null;
        return _glyphTypefaceCache.TryGetValue(familyName, out var cache)
            && cache.TryGetValue(new FontCollectionKey(style, weight, stretch), out glyphTypeface)
            && glyphTypeface is not null;
    }
    public override IEnumerator<FontFamily> GetEnumerator() => families.GetEnumerator();

    // Diagnostic adapter for the unsigned TTC v1 fixture only, not a production font importer.
    // Reorder directory offsets in a private copy; font tables and the source file stay intact.
    internal static void SelectFixtureFace(byte[] bytes, int index)
    {
        if (bytes.Length < 12) throw new InvalidDataException("Font header is truncated");
        if (!bytes.AsSpan(0, 4).SequenceEqual("ttcf"u8))
        {
            if (index != 0) throw new InvalidDataException("Single font has no requested face");
            return;
        }
        uint version = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
        uint count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8));
        if (version != 0x00010000 || count == 0 || count > (bytes.Length - 12) / 4
            || index < 0 || index >= count)
            throw new InvalidDataException("Unsupported or invalid TTC fixture header/index");
        int selected = 12 + index * 4;
        uint first = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12));
        uint offset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(selected));
        if (first > bytes.Length - 12 || offset > bytes.Length - 12)
            throw new InvalidDataException("TTC directory outside file");
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), offset);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(selected), first);
    }
}
