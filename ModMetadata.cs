using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace HowaType64;

// SPT 4.1: AbstractModMetadata (class) was replaced by the IModMetadata interface.
//  - no "override" keywords
//  - IsBundleMod is gone (the server detects bundles.json by itself)
//  - HasPrepatcher is new
public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.howa.type64";
    public string Name { get; init; } = "Howa Type 64";
    public string Author { get; init; } = "You";
    public List<string>? Contributors { get; init; }
    public Version Version { get; init; } = new("1.0.0");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public List<string>? LoadBefore { get; init; }
    public List<string>? LoadAfter { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public bool HasPrepatcher { get; init; } = false;
    public string License { get; init; } = "MIT";
}
