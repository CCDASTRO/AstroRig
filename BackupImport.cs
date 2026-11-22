using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AstroRig;

internal sealed class BackupImport : IDisposable
{
    private IntPtr _db;
    private const string Sqlite = "winsqlite3.dll";
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string file, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport(Sqlite, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_count(IntPtr statement);

    private BackupImport(string path)
    {
        if (sqlite3_open_v2(path, out _db, 1, IntPtr.Zero) == 0) return;
        var error = Error();
        Dispose();
        throw error;
    }

    private Exception Error() => new InvalidDataException(Marshal.PtrToStringUTF8(sqlite3_errmsg(_db)) ?? "Cannot read backup.");
    public void Dispose()
    {
        if (_db != IntPtr.Zero) sqlite3_close(_db);
        _db = IntPtr.Zero;
    }

    private IEnumerable<string[]> Query(string sql)
    {
        if (sqlite3_prepare_v2(_db, sql, -1, out var statement, IntPtr.Zero) != 0) throw Error();
        try
        {
            int status;
            while ((status = sqlite3_step(statement)) == 100)
            {
                var row = new string[sqlite3_column_count(statement)];
                for (int i = 0; i < row.Length; i++)
                    row[i] = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, i)) ?? "";
                yield return row;
            }
            if (status != 101) throw Error();
        }
        finally { sqlite3_finalize(statement); }
    }

    private static double Number(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result) ? result : 0;

    private static Role Category(string value) => value.Trim().ToLowerInvariant() switch
    {
        "mount" => Role.Mount,
        "telescope / lens / ota" or "ota" => Role.Optics,
        "main camera" => Role.MainCamera,
        "reducer" or "reducer / flattener" => Role.Reducer,
        "filter" or "filters" => Role.Filter,
        "filter wheel" => Role.FilterWheel,
        "guide scope / oag" or "guide scope" or "oag" => Role.GuideScope,
        "guide camera" => Role.GuideCamera,
        "tilt plate" => Role.TiltPlate,
        "autofocuser" or "auto focuser" or "focuser" => Role.Focuser,
        "controller" => Role.Controller,
        "temperature control" or "power" => Role.Power,
        "tripod" or "pier" => Role.Tripod,
        _ => Role.Accessory
    };

    private static Equipment ParseEquipment(string[] row)
    {
        using var specs = JsonDocument.Parse(string.IsNullOrWhiteSpace(row[8]) ? "{}" : row[8]);
        string Value(string key) => specs.RootElement.ValueKind == JsonValueKind.Object &&
            specs.RootElement.TryGetProperty(key, out var value) ? value.ToString() : "";
        double Spec(string key) => Number(Value(key));
        bool Yes(string key) => Value(key).ToLowerInvariant() is "yes" or "true" or "1";
        var category = Category(row[3]);
        return new Equipment
        {
            Model = row[1], Brand = row[2], Category = category, WeightKg = Number(row[4]),
            Notes = row[5], Status = row[6], PurchaseCost = Number(row[7]), ImportedSpecs = row[8],
            ApertureMm = Spec("aperture_mm"), FocalLengthMm = Spec("focal_length_mm"),
            FocalMultiplier = Spec("reduction_factor") is > 0 and var factor ? factor : 1,
            ImageCircleMm = Spec("image_circle_mm"), PixelSizeUm = Spec("pixel_size_um"),
            SensorWidthMm = Spec("sensor_width_mm"), SensorHeightMm = Spec("sensor_height_mm"),
            ResolutionX = (int)Spec("resolution_x"), ResolutionY = (int)Spec("resolution_y"),
            SpacingMm = Spec("spacing_mm"), InputThread = Value("input_thread_type"), OutputThread = Value("output_thread_type"),
            RatedPayloadKg = Spec("payload_capacity_kg"), ImagingTargetKg = Spec("recommended_imaging_payload_kg"),
            MountType = Value("mount_type"), OpticalDesign = Value("optical_type"),
            Cooled = Yes("cooled"), BuiltInGuider = Yes("built_in_guider"),
            GuideDeviceType = Value("device_type").ToLowerInvariant() is "oag" or "off-axis guider"
                ? GuideDeviceKind.OffAxisGuider : GuideDeviceKind.GuideScope,
            RequiredBackfocusMm = Spec("required_backfocus_mm"), TravelMm = Spec("travel_mm"),
            AdjustmentType = Value("tilt_plate_type")
        };
    }

    public static Inventory Read(string path)
    {
        using var db = new BackupImport(path);
        var equipment = db.Query("SELECT id,name,brand,category,weight_kg,notes,ownership_status,purchase_price,specs_json FROM equipment")
            .ToDictionary(row => row[0], ParseEquipment);
        var result = new Inventory { Equipment = equipment.Values.ToList() };
        var rigRows = db.Query("SELECT id,name,notes,mount_id,ota_id,main_camera_id,reducer_id,filter_id,guide_scope_id,guide_camera_id,controller_id,temperature_control_id,autofocuser_id,accessory_id FROM rigs").ToArray();
        var rigs = rigRows.ToDictionary(row => row[0], row => new Rig { Name = row[1], Notes = row[2] });
        var tables = db.Query("SELECT name FROM sqlite_master WHERE type='table'").Select(row => row[0]).ToHashSet();
        if (tables.Contains("rig_items"))
            foreach (var row in db.Query("SELECT rig_id,equipment_id,placement_group,item_role,notes FROM rig_items ORDER BY rig_id,placement_group,position_index,id"))
                if (rigs.TryGetValue(row[0], out var rig) && equipment.TryGetValue(row[1], out var item))
                {
                    var assignment = Assignment.For(item, row[2] == "main_train");
                    var role = Category(row[3]);
                    if (role != Role.Accessory) assignment.Role = role;
                    assignment.Notes = row[4];
                    rig.Items.Add(assignment);
                }

        void AddMissing(Rig rig, string id, string notes = "")
        {
            if (equipment.TryGetValue(id, out var item) && rig.Items.All(entry => entry.EquipmentId != item.Id))
            {
                var assignment = Assignment.For(item);
                assignment.Notes = notes;
                rig.Items.Add(assignment);
            }
        }

        foreach (var row in rigRows)
            foreach (var id in row.Skip(3))
                AddMissing(rigs[row[0]], id);
        if (tables.Contains("rig_accessories"))
            foreach (var row in db.Query("SELECT rig_id,equipment_id,notes FROM rig_accessories ORDER BY id"))
                if (rigs.TryGetValue(row[0], out var rig)) AddMissing(rig, row[1], row[2]);
        result.Rigs.AddRange(rigs.Values);
        return result;
    }
}

