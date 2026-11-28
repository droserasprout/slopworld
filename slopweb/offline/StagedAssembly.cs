using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Mono.Cecil;

// The launcher publishes the complete stage after all patch passes succeed.
internal static class StagedAssembly
{
    internal static void Write(AssemblyDefinition assembly, string path)
    {
        var manifestPath = Path.Combine(Path.GetDirectoryName(path)!, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsArray();
        var entry = manifest.Single(node =>
            node!["file"]!.GetValue<string>() == Path.GetFileName(path)
        )!;
        var partial = path + ".partial";
        assembly.Write(partial);
        using (var stream = File.OpenRead(partial))
            entry["stagedSha256"] = Convert.ToHexStringLower(SHA256.HashData(stream));
        File.Move(partial, path, true);
        File.WriteAllText(manifestPath, manifest.ToJsonString());
    }

    internal static void RecordAdaptations(string root, params string[] descriptions)
    {
        var path = Path.Combine(root, "game", "adaptations.json");
        var adaptations = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        foreach (var description in descriptions)
            adaptations.Add(description);
        File.WriteAllText(path, adaptations.ToJsonString());
    }
}
