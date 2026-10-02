using System.Text.Json;
using Compositor.Imaging;

internal static class PngIntegrityChecks
{
    public static int Run(string fixtures, string output)
    {
        string[] names = JsonSerializer.Deserialize<string[]>(
            File.ReadAllText(Path.Combine(fixtures, "png-invalid.json")))!;
        var accepted = new List<string>();
        foreach (string name in names)
        {
            try { ImageCodec.Load(Path.Combine(fixtures, name)); accepted.Add(name); }
            catch (InvalidDataException) { }
        }
        File.WriteAllText(Path.Combine(output, "png-integrity.json"), JsonSerializer.Serialize(new
        {
            checkedCases = names.Length, unexpectedlyAccepted = accepted, passed = accepted.Count == 0
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (accepted.Count != 0)
            throw new InvalidOperationException("Damaged PNG accepted: " + string.Join(", ", accepted));
        return names.Length;
    }
}
