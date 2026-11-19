using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace HowaType64;

public class ItemDef
{
    [JsonPropertyName("newId")] public string NewId { get; set; } = "";
    [JsonPropertyName("clone")] public string Clone { get; set; } = "";
    [JsonPropertyName("parentId")] public string ParentId { get; set; } = "";
    [JsonPropertyName("handbookParentId")] public string HandbookParentId { get; set; } = "";
    [JsonPropertyName("handbookPrice")] public double HandbookPrice { get; set; }
    [JsonPropertyName("fleaPrice")] public double FleaPrice { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("shortName")] public string ShortName { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("props")] public JsonElement Props { get; set; }
}

// SPT 4.1 changes applied here:
//  - DatabaseService is gone: tables are injected directly (TemplateTable, GlobalTable, LocaleTable, TradersTable)
//  - OnLoad() -> OnLoadAsync(CancellationToken)
//  - OnLoadOrder.PostDBModLoader no longer exists. New items MUST be added during Preload,
//    otherwise the server throws DatabaseModifiedAfterCutoffException after profiles load.
//  - ISptLogger moved to SPTarkov.Common.Models.Logging, ModHelper to Helpers.Server
[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public class Type64Mod(
    ISptLogger<Type64Mod> logger,
    TemplateTable templateTable,
    GlobalTable globalTable,
    LocaleTable localeTable,
    TradersTable tradersTable,
    CustomItemService customItemService,
    ModHelper modHelper,
    JsonUtil jsonUtil) : IOnLoad
{
    private const string WeaponId = "67f757e6e60606b56610261d";
    private const string MountId = "67f757e26cf3bc1bfc968982";
    private const string MarksmanPresetId = "6806545d567504f92dfd75e4";

    // ---- Muzzle slot (suppressors) ----
    // Needs a transform named "mod_muzzle" in weapon_howa_type64_container.bundle, otherwise the slot
    // has no attachment point and does not show in the weapon builder.
    // Fresh slot id for the Type 64's new mod_muzzle slot (must be unique, 24 hex chars)
    private const string MuzzleSlotId = "67f7610a0b1c2d3e4f5a6b7c";
    // Base class of suppressors, used to keep only silencers from the donor muzzle filters.
    // If this id is wrong the code falls back to every muzzle device and logs a warning.
    private const string SilencerParentId = "550aa4cd4bdc2dd8348b456c";
    // Rifles whose mod_muzzle filters are merged: 7.62x51, 7.62x39 and 7.62x54R.
    // Ids that don't exist in the DB are skipped, so you can add more here.
    private static readonly string[] MuzzleDonorWeapons =
    [
        // 7.62x51
        "5b0bbe4e5acfc40dc528a72d", // DS Arms SA-58
        "5aafa857e5b5b00018480968", // M1A
        "5df8ce05b11454561e39243b", // SR-25
        // 7.62x39
        "59d6088586f774275f37482f", // AKM
        "5a0ec13bfcdbcb00165aa685", // AKMN
        "59ff346386f77477562ff5e2", // AKMS
        "5ac66d2d5acfc43b321d4b53", // AK-103
        "5ac66d725acfc43b321d4b60", // AK-104
        "574d967124597745970e7c94", // SKS
        "59e6152586f77473dc057aa1", // VPO-136
        "59e6687d86f77411d949b251", // VPO-209
        // 7.62x54R
        "5ae08f0a5acfc408fb1398a1", // Mosin infantry
        "5bfd297f0db834001a669119", // Mosin sniper
        "55801eed4bdc2d89578b4588", // SV-98
        "5c46fbd72e2216398b5a8c9c", // SVDS
        "588892092459774ac91d4b11", // DVL-10
        "5df24cf80dee1b22f862e9bc", // T-5000
        "5c501a4d2e221602b412b540"  // VPO-101
    ];

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var dataPath = Path.Combine(modPath, "Data");

        // Scope filter list copied from a vanilla scope-accepting slot (used by the Type 64 mount)
        var scopeFilter = templateTable.Items[ItemTpl.RECEIVER_MK18_338_LM_UPPER]
            .Properties!.Slots!.First().Properties!.Filters!.First().Filter!;

        // ---- Items ----
        var defs = jsonUtil.Deserialize<List<ItemDef>>(File.ReadAllText(Path.Combine(dataPath, "items.json")))!;
        foreach (var def in defs)
        {
            var props = jsonUtil.Deserialize<TemplateItemProperties>(def.Props.GetRawText())!;

            if (def.NewId == MountId)
            {
                props.Slots!.First().Properties!.Filters!.First().Filter = new HashSet<MongoId>(scopeFilter);
            }

            var details = new NewItemFromCloneDetails
            {
                ItemTplToClone = ResolveClone(def.Clone),
                OverrideProperties = props,
                ParentId = def.ParentId,
                NewId = def.NewId,
                NewItemName = def.Name,
                HandbookParentId = def.HandbookParentId,
                HandbookPriceRoubles = def.HandbookPrice,
                FleaPriceRoubles = def.FleaPrice,
                Locales = new Dictionary<string, LocaleDetails>
                {
                    ["en"] = new LocaleDetails
                    {
                        Name = def.Name,
                        ShortName = def.ShortName,
                        Description = def.Description
                    }
                }
            };

            var result = customItemService.CreateItemFromClone(details);
            if (result.Success != true)
            {
                logger.Error($"[Howa Type 64] Failed to create {def.Name}: {string.Join(", ", result.Errors ?? [])}");
            }
        }

        // ---- Muzzle slot so 7.62x51 suppressors can be attached ----
        AddMuzzleSlot();

        // ---- Accept every 7.62x51 magazine in the mod_magazine slot ----
        AddMagazineCompat();

        // ---- Mastering ----
        // Mastering is an array in 4.1, so it has to be rebuilt rather than appended to
        globalTable.Configuration.Mastering = globalTable.Configuration.Mastering
            .Append(new Mastering
            {
                Name = "Howa Type 64 Battlerifle",
                Templates = [WeaponId],
                Level2 = 450,
                Level3 = 1200
            })
            .ToArray();

        // ---- Preset name locale ----
        foreach (var (_, lazyLocale) in localeTable.Global)
        {
            lazyLocale.AddTransformer(localeData =>
            {
                if (localeData is not null)
                {
                    localeData[MarksmanPresetId] = "Marksman";
                }
                return localeData;
            });
        }

        // ---- Global weapon presets ----
        MergePresets(globalTable.ItemPresets, Path.Combine(dataPath, "global_item_preset.json"));

        // ---- Prapor assort (rifle 45,000 RUB) ----
        var assort = tradersTable[Traders.PRAPOR].Assort!;
        var extra = jsonUtil.Deserialize<TraderAssort>(File.ReadAllText(Path.Combine(dataPath, "Item_Preset.json")))!;
        assort.Items.AddRange(extra.Items);
        foreach (var (id, scheme) in extra.BarterScheme)
        {
            assort.BarterScheme[id] = scheme;
        }
        foreach (var (id, level) in extra.LoyalLevelItems)
        {
            assort.LoyalLevelItems[id] = level;
        }

        // ---- Allow in primary / secondary equipment slots ----
        var inventorySlots = templateTable.Items[ItemTpl.INVENTORY_DEFAULT].Properties!.Slots!.ToList();
        inventorySlots[0].Properties!.Filters!.First().Filter!.Add(WeaponId);
        inventorySlots[1].Properties!.Filters!.First().Filter!.Add(WeaponId);

        // PMC bot usage: enable if your 4.1.x CustomItemService exposes it
        // customItemService.AddCustomWeaponToPMCs(WeaponId, 4, "FirstPrimaryWeapon");

        // ---- Debug report: which slots does the Type 64 really have after loading? ----
        try
        {
            var report = new List<string>(debugLines) { "", "Slots on the Type 64 after load:" };
            foreach (var slot in templateTable.Items[WeaponId].Properties!.Slots!)
            {
                var filterItems = slot.Properties?.Filters?.Sum(f => f.Filter?.Count ?? 0) ?? 0;
                report.Add($"  {slot.Name}  id={slot.Id}  filterItems={filterItems}");
            }
            File.WriteAllLines(Path.Combine(modPath, "slot_debug.txt"), report);
        }
        catch (Exception ex)
        {
            logger.Warning($"[Howa Type 64] Could not write slot_debug.txt: {ex.Message}");
        }

        logger.Info("[Howa Type 64] Loaded.");
        return Task.CompletedTask;
    }

    // Everything the muzzle-slot code reports is also kept here and written to slot_debug.txt in the mod folder
    private readonly List<string> debugLines = [];

    private void Note(string message, bool warn = false)
    {
        debugLines.Add((warn ? "WARNING: " : "") + message);
        if (warn)
        {
            logger.Warning($"[Howa Type 64] {message}");
        }
        else
        {
            logger.Info($"[Howa Type 64] {message}");
        }
    }

    // The donor rifles have no mod_muzzle slot themselves; it lives on their barrels (weapon -> mod_barrel -> mod_muzzle).
    // So walk down through the slot filters of each donor rifle and collect every mod_muzzle slot found.
    private IEnumerable<Slot> FindMuzzleSlots(MongoId rootId, int maxDepth)
    {
        var seen = new HashSet<MongoId>();
        var queue = new Queue<(MongoId Id, int Depth)>();
        queue.Enqueue((rootId, 0));

        while (queue.Count > 0)
        {
            var (id, depth) = queue.Dequeue();
            if (!seen.Add(id) || !templateTable.Items.TryGetValue(id, out var template))
            {
                continue;
            }

            var templateSlots = template.Properties?.Slots;
            if (templateSlots is null)
            {
                continue;
            }

            foreach (var slot in templateSlots)
            {
                if (slot.Name == "mod_muzzle")
                {
                    yield return slot;
                }

                if (depth >= maxDepth || slot.Properties?.Filters is null)
                {
                    continue;
                }

                foreach (var filter in slot.Properties.Filters)
                {
                    if (filter.Filter is null)
                    {
                        continue;
                    }
                    foreach (var child in filter.Filter)
                    {
                        queue.Enqueue((child, depth + 1));
                    }
                }
            }
        }
    }

    private void AddMuzzleSlot()
    {
        var weapon = templateTable.Items[WeaponId];
        var slots = weapon.Properties!.Slots!.ToList();
        if (slots.Any(s => s.Name == "mod_muzzle"))
        {
            return;
        }

        // Collect mod_muzzle slots reachable from the SA-58 (the clone source) and the other donor rifles
        var muzzleSlots = new List<Slot>();
        muzzleSlots.AddRange(FindMuzzleSlots(ItemTpl.ASSAULTRIFLE_DS_ARMS_SA58_762X51_ASSAULT_RIFLE, 3));
        foreach (var donorId in MuzzleDonorWeapons)
        {
            muzzleSlots.AddRange(FindMuzzleSlots(new MongoId(donorId), 3));
        }

        if (muzzleSlots.Count == 0)
        {
            Note("No mod_muzzle slot found on the donor rifles or their barrels; suppressor slot not added.", true);
            return;
        }

        // Merge the filters of every muzzle slot found
        var allMuzzleDevices = new HashSet<MongoId>();
        foreach (var muzzleSlot in muzzleSlots)
        {
            if (muzzleSlot.Properties?.Filters is null)
            {
                continue;
            }

            foreach (var filter in muzzleSlot.Properties.Filters)
            {
                if (filter.Filter is null)
                {
                    continue;
                }
                foreach (var id in filter.Filter)
                {
                    allMuzzleDevices.Add(id);
                }
            }
        }

        Note($"Found {muzzleSlots.Count} muzzle slots with {allMuzzleDevices.Count} muzzle devices in total.");

        // Keep only suppressors; fall back to all muzzle devices if the filter matches nothing
        var silencerParent = new MongoId(SilencerParentId);
        var silencers = allMuzzleDevices
            .Where(id => templateTable.Items.TryGetValue(id, out var item) && item.Parent == silencerParent)
            .ToHashSet();
        if (silencers.Count == 0)
        {
            Note("No suppressors matched the silencer base class; allowing all donor muzzle devices.", true);
            silencers = allMuzzleDevices;
        }

        // Deep copy the first muzzle slot found so no vanilla slot is modified
        var slot = jsonUtil.Deserialize<Slot>(jsonUtil.Serialize(muzzleSlots[0]))!;
        slot.Id = MuzzleSlotId;
        slot.Parent = WeaponId;
        slot.Required = false;
        slot.Properties!.Filters!.First().Filter = silencers;

        slots.Add(slot);
        weapon.Properties.Slots = slots;

        Note($"Added mod_muzzle slot with {silencers.Count} compatible muzzle devices.");
    }

    // Base class of magazines and the id of 7.62x51 M80 (used to detect 7.62x51 magazines)
    private const string MagazineParentId = "5448bc234bdc2d3c308b4569";
    private const string Ammo762x51Id = "58dd3ad986f77403051cba8f";

    private void AddMagazineCompat()
    {
        var weapon = templateTable.Items[WeaponId];
        var magSlot = weapon.Properties!.Slots!.FirstOrDefault(s => s.Name == "mod_magazine");
        if (magSlot?.Properties?.Filters?.FirstOrDefault()?.Filter is not { } magFilter)
        {
            Note("Type 64 has no mod_magazine slot filter; magazine compatibility not added.", true);
            return;
        }

        var magParent = new MongoId(MagazineParentId);
        var ammo = new MongoId(Ammo762x51Id);
        var added = 0;

        foreach (var (id, item) in templateTable.Items)
        {
            if (item.Parent != magParent)
            {
                continue;
            }

            var takes762x51 = item.Properties?.Cartridges?.Any(c =>
                c.Properties?.Filters?.Any(f => f.Filter?.Contains(ammo) == true) == true) == true;

            if (takes762x51 && magFilter.Add(id))
            {
                added++;
            }
        }

        Note($"Added {added} extra 7.62x51 magazines to the Type 64 mod_magazine slot.");
    }

    // Generic so the code never has to name the preset type (it moved between 4.0 and 4.1).
    private void MergePresets<T>(IDictionary<MongoId, T> target, string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var raw = doc.RootElement.GetProperty("ItemPresets").GetRawText();
        var presets = jsonUtil.Deserialize<Dictionary<MongoId, T>>(raw)!;
        foreach (var (id, preset) in presets)
        {
            target[id] = preset;
        }
    }

    private static MongoId ResolveClone(string clone) => clone switch
    {
        "ItemTpl:ASSAULTRIFLE_DS_ARMS_SA58_762X51_ASSAULT_RIFLE" => ItemTpl.ASSAULTRIFLE_DS_ARMS_SA58_762X51_ASSAULT_RIFLE,
        "ItemTpl:MAGAZINE_762X51_SA58FAL_20RND" => ItemTpl.MAGAZINE_762X51_SA58FAL_20RND,
        "ItemTpl:HANDGUARD_SA58FAL_BELGIAN_STYLE" => ItemTpl.HANDGUARD_SA58FAL_BELGIAN_STYLE,
        "ItemTpl:BIPOD_SV98" => ItemTpl.BIPOD_SV98,
        _ => new MongoId(clone)
    };
}
