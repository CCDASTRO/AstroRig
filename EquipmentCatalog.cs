using System.Text.Json;

namespace AstroRig;

internal static class EquipmentCatalog
{
    private static readonly Dictionary<string, Equipment[]> Cached = new();

    public static IReadOnlyList<Equipment> For(Role type)
    {
        var name = type switch
        {
            Role.Mount => "mount",
            Role.MainCamera or Role.GuideCamera => "camera",
            Role.Optics or Role.GuideScope => "optics",
            Role.Filter => "filter",
            Role.FilterWheel => "filter-wheel",
            _ => null
        };
        if (name == null) return [];
        if (Cached.TryGetValue(name, out var items)) return items;
        using var data = typeof(EquipmentCatalog).Assembly.GetManifestResourceStream($"AstroRig.catalog-{name}.json")
            ?? throw new InvalidDataException($"Missing equipment catalog: {name}.");
        items = JsonSerializer.Deserialize<Equipment[]>(data) ?? [];
        Cached.Add(name, items);
        return items;
    }

    public static void Apply(Equipment preset, Equipment item)
    {
        item.Brand = preset.Brand;
        item.Model = preset.Model;
        item.WeightKg = preset.WeightKg;
        item.Notes = preset.Notes;
        item.ApertureMm = preset.ApertureMm;
        item.FocalLengthMm = preset.FocalLengthMm;
        item.ImageCircleMm = preset.ImageCircleMm;
        item.OpticalDesign = preset.OpticalDesign;
        item.PixelSizeUm = preset.PixelSizeUm;
        item.SensorWidthMm = preset.SensorWidthMm;
        item.SensorHeightMm = preset.SensorHeightMm;
        item.ResolutionX = preset.ResolutionX;
        item.ResolutionY = preset.ResolutionY;
        item.SpacingMm = preset.SpacingMm;
        item.OutputThread = preset.OutputThread;
        item.Cooled = preset.Cooled;
        item.BuiltInGuider = preset.BuiltInGuider;
        item.MountType = preset.MountType;
        item.RatedPayloadKg = preset.RatedPayloadKg;
        item.FilterSpecification = preset.FilterSpecification;
        item.FilterSlotCount = preset.FilterSlotCount;
        if (item.Category == Role.FilterWheel) item.PowerConnection = preset.PowerConnection;
    }
}

