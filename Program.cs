using System.ComponentModel;

namespace AstroRig;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args is ["--verify", var database, var directory])
        {
            Directory.CreateDirectory(directory);
            try
            {
                var model = BackupImport.Read(database);
                var path = Path.Combine(directory, "roundtrip.json");
                model.Save(path);
                var loaded = Inventory.Load(path);
                var copyPath = Path.Combine(directory, "saved-as.json");
                loaded.Save(copyPath);
                if (!File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(copyPath)))
                    throw new InvalidDataException("Save As did not preserve the inventory.");
                loaded = Inventory.Load(copyPath);
                ExcelExport.Write(Path.Combine(directory, "verified.xlsx"), loaded);
                ApplicationConfiguration.Initialize();
                using var view = new MainWindow(loaded);
                view.CreateControl();
                view.VerifyAddEquipmentFlow(usePreset: true);
                view.VerifyAddEquipmentFlow(usePreset: false);
                foreach (var type in Enum.GetValues<Role>())
                {
                    var item = new Equipment { Category = type };
                    var properties = new EquipmentFields(item).GetProperties();
                    var addProperties = new EquipmentFields(item, adding: true).GetProperties();
                    if ((properties[nameof(Equipment.PixelSizeUm)] != null) != (type is Role.MainCamera or Role.GuideCamera) ||
                        (properties[nameof(Equipment.RatedPayloadKg)] != null) != (type == Role.Mount) ||
                        properties[nameof(Equipment.Category)] == null ||
                        addProperties[nameof(Equipment.Category)] != null || addProperties[nameof(Equipment.Model)] != null ||
                        !properties.Cast<PropertyDescriptor>().Where(p => p.Name is not (nameof(Equipment.Category) or nameof(Equipment.Model)))
                            .Select(p => p.Name).SequenceEqual(addProperties.Cast<PropertyDescriptor>().Select(p => p.Name)))
                        throw new InvalidDataException($"Incorrect editor fields for {type}.");
                }
                var camera = new Equipment { Category = Role.MainCamera, SpacingMm = 17.5 };
                var tiltPlate = new Equipment { Category = Role.TiltPlate, SpacingMm = 5 };
                var reducer = new Equipment { Category = Role.Reducer, RequiredBackfocusMm = 55 };
                var example = new Inventory { Equipment = [camera, tiltPlate, reducer] };
                var exampleRig = new Rig { Items = example.Equipment.Select(item => Assignment.For(item)).ToList() };
                var metrics = RigMetrics.Calculate(example, exampleRig);
                if (metrics.Spacing != 22.5 || metrics.RequiredBackfocus != 55 || metrics.Remaining != 32.5)
                    throw new InvalidDataException("Incorrect backfocus calculation.");
                if (!EquipmentCatalog.For(Role.MainCamera).Any(item => item.Model == "ASI6200MC Pro" && item.ResolutionX == 9576) ||
                    !EquipmentCatalog.For(Role.Optics).Any(item => item.Model == "130PHQ" && item.FocalLengthMm == 1000) ||
                    EquipmentCatalog.For(Role.Filter).Count < 100 || EquipmentCatalog.For(Role.Focuser).Count != 0)
                    throw new InvalidDataException("Equipment presets are missing or invalid.");
                File.WriteAllText(Path.Combine(directory, "ui-smoke.txt"), "Main window initialized with imported data.");
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(directory, "error.txt"), error.ToString());
                Environment.ExitCode = 1;
            }
            return;
        }

        ApplicationConfiguration.Initialize();
        try { Application.Run(new MainWindow()); }
        catch (Exception error) { MessageBox.Show(error.Message, "AstroRig", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}

internal sealed class MainWindow : Form
{
    private static readonly string AppDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImagingRig");
    private static readonly string DefaultPath = Path.Combine(AppDirectory, "inventory.json");
    private static readonly string LastPathFile = Path.Combine(AppDirectory, "last-inventory.txt");
    private static readonly string VersionLabel = typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
    private string _path = DefaultPath;
    private Inventory _inventory;
    private bool _dirty;
    private readonly ListBox _equipment = new() { Dock = DockStyle.Fill };
    private readonly ListBox _rigs = new() { Dock = DockStyle.Fill };
    private readonly PropertyGrid _equipmentEditor = new() { Dock = DockStyle.Fill, ToolbarVisible = false, HelpVisible = false };
    private readonly PropertyGrid _rigEditor = new() { Dock = DockStyle.Fill, ToolbarVisible = false, HelpVisible = false };
    private readonly DataGridView _components = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None
    };
    private readonly TextBox _metrics = new() { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(232, 240, 249), TabStop = false,
        ScrollBars = ScrollBars.Vertical };
    private readonly ToolStripStatusLabel _status = new();
    private bool _refreshing;
    private Rig? SelectedRig => _rigs.SelectedItem as Rig;

    public MainWindow(Inventory? initialInventory = null)
    {
        string? startupNotice = null;
        if (initialInventory == null)
        {
            try
            {
                if (File.Exists(LastPathFile))
                {
                    var recent = File.ReadAllText(LastPathFile);
                    if (File.Exists(recent)) _path = recent;
                    else startupNotice = "Last inventory unavailable. Open it when the folder is accessible.";
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                startupNotice = "Could not read the last inventory location.";
            }
            try { _inventory = Inventory.Load(_path); }
            catch (Exception error) when (_path != DefaultPath)
            {
                startupNotice = $"Could not open the last inventory. {error.Message}";
                _path = DefaultPath;
                _inventory = Inventory.Load(_path);
            }
        }
        else _inventory = initialInventory;
        Text = "AstroRig";
        MinimumSize = new Size(850, 650);
        Size = new Size(1120, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.White;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var rigs = new TabPage("Rigs") { BackColor = Color.White, Padding = new Padding(12) };
        var equipment = new TabPage("Equipment") { BackColor = Color.White, Padding = new Padding(12) };
        tabs.TabPages.AddRange([rigs, equipment]);
        rigs.Controls.Add(BuildRigView());
        equipment.Controls.Add(BuildEquipmentView());
        Controls.Add(tabs);

        var toolbar = Bar(("Open", OpenInventory), ("Save", Save), ("Save As", SaveAs),
            ("Import AstroArmory backup", Import), ("Export Excel", Export));
        toolbar.BackColor = Color.FromArgb(223, 235, 248);
        Controls.Add(toolbar);
        var status = new StatusStrip { ShowItemToolTips = true };
        status.Items.Add(_status);
        Controls.Add(status);

        _equipment.SelectedIndexChanged += (_, _) => { if (!_refreshing) ShowEquipmentFields(); };
        _equipmentEditor.PropertyValueChanged += (_, e) =>
        {
            if (_equipment.SelectedItem is not Equipment item) return;
            if (string.IsNullOrWhiteSpace(item.Model) || !ValidEquipment(item))
            {
                e.ChangedItem?.PropertyDescriptor?.SetValue(item, e.OldValue);
                ShowEquipmentFields();
                Notice("Enter a model name and finite, non-negative specifications. Focal multiplier must be positive.");
                return;
            }
            Changed();
            RefreshLists(item, SelectedRig);
            ShowEquipmentFields();
        };
        _rigEditor.PropertyValueChanged += (_, e) =>
        {
            var rig = SelectedRig;
            if (rig == null) return;
            if (rig.Binning < 1 || string.IsNullOrWhiteSpace(rig.Name) || !NonNegative(rig.RequiredBackfocusMm) ||
                _inventory.Rigs.Any(other => other != rig && string.Equals(other.Name, rig.Name, StringComparison.OrdinalIgnoreCase)))
            {
                e.ChangedItem?.PropertyDescriptor?.SetValue(_rigEditor.SelectedObject, e.OldValue);
                _rigEditor.Refresh();
                Notice("Use a unique rig name, binning of at least 1, and a non-negative backfocus.");
                return;
            }
            Changed();
            RefreshLists(_equipment.SelectedItem as Equipment, rig);
        };
        _rigs.SelectedIndexChanged += (_, _) => { if (!_refreshing) RefreshRig(); };
        _components.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_components.IsCurrentCellDirty && _components.CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell)
                _components.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _components.CellValueChanged += (_, e) =>
        {
            if (_refreshing || e.RowIndex < 0 || _components.Rows[e.RowIndex].Tag is not Assignment assignment) return;
            var row = _components.Rows[e.RowIndex];
            if (row.Cells[1].Value is Role role) assignment.Role = role;
            assignment.CountInPayload = row.Cells[3].Value is true;
            assignment.CountInBackfocus = row.Cells[4].Value is true;
            assignment.Notes = row.Cells[5].Value as string ?? "";
            Changed();
            UpdateMetrics();
        };
        FormClosing += (_, e) =>
        {
            e.Cancel = !ConfirmNavigation("closing");
        };
        RefreshLists();
        UpdateFileStatus(startupNotice);
    }

    private static bool NonNegative(double d) => double.IsFinite(d) && d >= 0;
    private void ShowEquipmentFields() => _equipmentEditor.SelectedObject =
        _equipment.SelectedItem is Equipment item ? new EquipmentFields(item) : null;
    private static bool ValidEquipment(Equipment e) => new[] { e.WeightKg, e.PurchaseCost, e.ApertureMm, e.FocalLengthMm,
        e.FocalMultiplier, e.ImageCircleMm, e.PixelSizeUm, e.SensorWidthMm, e.SensorHeightMm, e.ResolutionX, e.ResolutionY,
        e.SpacingMm, e.RatedPayloadKg, e.ImagingTargetKg, e.RequiredBackfocusMm, e.TravelMm }.All(NonNegative) && e.FocalMultiplier > 0;

    private ToolStrip Bar(params (string Title, Action Action)[] actions)
    {
        var bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4), RenderMode = ToolStripRenderMode.System };
        foreach (var (title, action) in actions)
        {
            var button = new ToolStripButton(title) { Margin = new Padding(4, 2, 4, 2), Padding = new Padding(4, 2, 4, 2) };
            button.Click += (_, _) => { try { action(); } catch (Exception ex) { Notice(ex.Message); } };
            bar.Items.Add(button);
        }
        return bar;
    }

    private static Panel Section(Control body, ToolStrip bar)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(body);
        panel.Controls.Add(bar);
        return panel;
    }

    private static SplitContainer Split(int distance) => new()
    {
        Dock = DockStyle.Fill, Width = 950, SplitterDistance = distance, FixedPanel = FixedPanel.Panel1
    };

    private Control BuildEquipmentView()
    {
        var split = Split(295);
        split.Panel1.Controls.Add(Section(_equipment, Bar(("Add equipment", AddEquipment), ("Delete", DeleteEquipment))));
        split.Panel2.Controls.Add(_equipmentEditor);
        return split;
    }

    private Control BuildRigView()
    {
        var split = Split(225);
        split.Panel1.Controls.Add(Section(_rigs, Bar(("Add rig", AddRig), ("Delete", DeleteRig))));

        _components.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Equipment", ReadOnly = true, FillWeight = 155 });
        _components.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Role", DataSource = Enum.GetValues<Role>(), FillWeight = 100, FlatStyle = FlatStyle.Flat });
        _components.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Weight (kg)", ReadOnly = true, FillWeight = 72 });
        _components.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Payload", FillWeight = 60 });
        _components.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Backfocus", FillWeight = 70 });
        _components.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Notes", FillWeight = 120 });

        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        details.Controls.Add(_rigEditor, 0, 0);
        details.Controls.Add(_metrics, 0, 1);
        details.Controls.Add(Section(_components, Bar(("Assign equipment", Assign), ("Remove", RemoveAssignment),
            ("Move up", () => MoveAssignment(-1)), ("Move down", () => MoveAssignment(1)))), 0, 2);
        split.Panel2.Controls.Add(details);
        return split;
    }

    private sealed class RigFields(Rig rig)
    {
        [DisplayName("Rig name")] public string Name { get => rig.Name; set => rig.Name = value; }
        [DisplayName("Sensor binning")] public int Binning { get => rig.Binning; set => rig.Binning = value; }
        [DisplayName("Required backfocus (mm)")] public double Backfocus { get => rig.RequiredBackfocusMm; set => rig.RequiredBackfocusMm = value; }
        public string Notes { get => rig.Notes; set => rig.Notes = value; }
    }

    private void RefreshLists(Equipment? item = null, Rig? rig = null)
    {
        _refreshing = true;
        try
        {
            _equipment.Items.Clear();
            _equipment.Items.AddRange(_inventory.Equipment.OrderBy(x => x.ToString(), StringComparer.CurrentCultureIgnoreCase).ToArray());
            _rigs.Items.Clear();
            _rigs.Items.AddRange(_inventory.Rigs.ToArray());
            _equipment.SelectedItem = item ?? (_equipment.Items.Count > 0 ? _equipment.Items[0] : null);
            _rigs.SelectedItem = rig ?? _inventory.Rigs.FirstOrDefault();
        }
        finally { _refreshing = false; }
        ShowEquipmentFields();
        RefreshRig();
    }

    private void RefreshRig(Assignment? selected = null)
    {
        _refreshing = true;
        try
        {
            var rig = SelectedRig;
            _rigEditor.SelectedObject = rig == null ? null : new RigFields(rig);
            _components.Rows.Clear();
            if (rig != null)
                foreach (var (assignment, item) in _inventory.Resolve(rig))
                {
                    var index = _components.Rows.Add(item.ToString(), assignment.Role,
                        item.WeightKg > 0 ? item.WeightKg.ToString("0.###") : "", assignment.CountInPayload,
                        assignment.CountInBackfocus, assignment.Notes);
                    _components.Rows[index].Tag = assignment;
                    if (assignment == selected) _components.CurrentCell = _components.Rows[index].Cells[0];
                }
            UpdateMetrics();
        }
        finally { _refreshing = false; }
    }

    private void UpdateMetrics()
    {
        if (SelectedRig is not Rig rig) { _metrics.Text = "Add a rig to get started."; return; }
        var v = RigMetrics.Calculate(_inventory, rig);
        string Number(double? d, string unit) => d is double value ? $"{value:0.##} {unit}".TrimEnd() : "—";
        _metrics.Lines =
        [
            $"Focal length   {Number(v.FocalLength, "mm")}     ·     Focal ratio   {Number(v.FocalRatio, "")}     ·     Image scale   {Number(v.ImageScale, "arcsec/px")}",
            $"Field of view   {Number(v.WidthDegrees, "°")} × {Number(v.HeightDegrees, "°")}",
            $"Recorded payload   {Number(v.Payload, "kg")}     ·     Rated   {Number(v.RatedPayload, "kg")}     ·     Imaging target   {Number(v.ImagingTarget, "kg")}",
            $"Headroom to target or rating   {Number(v.Headroom, "kg")}" + (v.MissingWeights > 0 ? $"     ·     Missing weights   {v.MissingWeights}" : ""),
            $"Backfocus target   {Number(v.RequiredBackfocus, "mm")}     ·     Recorded spacing   {Number(v.Spacing, "mm")}     ·     Remaining   {Number(v.Remaining, "mm")}"
        ];
    }

    private string? Prompt(string label)
    {
        using var dialog = new Form { Text = label, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(380, 105),
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var input = new TextBox { Left = 14, Top = 18, Width = 350, PlaceholderText = label };
        var ok = new Button { Text = "Add", Left = 194, Top = 60, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 286, Top = 60, DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange([input, ok, cancel]);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(input.Text) ? input.Text.Trim() : null;
    }

    private void AddEquipment()
    {
        var type = SelectEquipmentType();
        if (type == null) return;
        var item = new Equipment { Category = type.Value };
        if (!EnterEquipmentDetails(item)) return;
        _inventory.Equipment.Add(item);
        Changed();
        RefreshLists(item, SelectedRig);
    }

    internal void VerifyAddEquipmentFlow(bool usePreset)
    {
        const string model = "Verification mount";
        int stage = 0;
        int initialCount = _inventory.Equipment.Count;
        Exception? failure = null;
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.Cast<Form>().LastOrDefault(form => form != this);
            if (dialog == null) return;
            try
            {
                if (stage == 0)
                {
                    if (dialog.Text != "Add Equipment — Choose Type" || dialog.Controls.OfType<TextBox>().Any())
                        throw new InvalidDataException("The first equipment window must only ask for the type.");
                    var picker = dialog.Controls.OfType<ComboBox>().Single();
                    picker.SelectedItem = Role.Mount;
                    stage++;
                    dialog.Controls.OfType<Button>().Single(button => button.Text == "Next").PerformClick();
                }
                else if (stage == 1)
                {
                    var editor = dialog.Controls.OfType<PropertyGrid>().Single();
                    var fields = TypeDescriptor.GetProperties(editor.SelectedObject!);
                    if (dialog.Text != "Add Equipment — Mount" || fields[nameof(Equipment.RatedPayloadKg)] == null ||
                        fields[nameof(Equipment.PixelSizeUm)] != null)
                        throw new InvalidDataException("The mount window shows the wrong fields.");
                    if (usePreset)
                    {
                        var presets = dialog.Controls.OfType<ComboBox>().Single();
                        presets.SelectedItem = presets.Items.OfType<Equipment>().Single(preset => preset.Model == "Paramount MX+");
                        dialog.Controls.OfType<Button>().Single(button => button.Text == "Use preset").PerformClick();
                        if (dialog.Controls.OfType<TextBox>().Single().Text != "Paramount MX+")
                            throw new InvalidDataException("The preset did not fill the model.");
                    }
                    dialog.Controls.OfType<TextBox>().Single().Text = model;
                    stage++;
                    dialog.Controls.OfType<Button>().Single(button => button.Text == "Add").PerformClick();
                }
                else throw new InvalidDataException("The Add button did not close the equipment window.");
            }
            catch (Exception error)
            {
                failure = error;
                dialog.DialogResult = DialogResult.Cancel;
                dialog.Close();
            }
        };
        timer.Start();
        AddEquipment();
        timer.Stop();
        if (failure != null) throw failure;
        if (stage != 2 || _inventory.Equipment.Count != initialCount + 1 ||
            _inventory.Equipment[^1] is not { Model: model, Category: Role.Mount } added ||
            added.RatedPayloadKg != (usePreset ? 45 : 0))
            throw new InvalidDataException("The equipment flow did not add the selected mount.");
    }

    private Role? SelectEquipmentType()
    {
        using var dialog = new Form { Text = "Add Equipment — Choose Type", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(430, 117), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, Font = Font };
        var typeLabel = new Label { Text = "Equipment type", Left = 14, Top = 12, AutoSize = true };
        var type = new ComboBox { Left = 14, Top = 34, Width = 400, DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true, AccessibleName = "Equipment type" };
        type.Format += (_, e) => { if (e.ListItem is Role role) e.Value = EquipmentFields.TypeLabel(role); };
        type.Items.AddRange(Enum.GetValues<Role>().Cast<object>().ToArray());
        type.SelectedIndex = -1;
        var next = new Button { Text = "Next", Left = 242, Top = 72, Width = 80, Height = 30,
            DialogResult = DialogResult.OK, Enabled = false };
        var cancel = new Button { Text = "Cancel", Left = 334, Top = 72, Width = 80, Height = 30,
            DialogResult = DialogResult.Cancel };
        type.SelectedIndexChanged += (_, _) => next.Enabled = type.SelectedItem is Role;
        dialog.Controls.AddRange([typeLabel, type, next, cancel]);
        dialog.AcceptButton = next;
        dialog.CancelButton = cancel;
        dialog.ActiveControl = type;
        return dialog.ShowDialog(this) == DialogResult.OK && type.SelectedItem is Role selected ? selected : null;
    }

    private bool EnterEquipmentDetails(Equipment item)
    {
        var presets = EquipmentCatalog.For(item.Category);
        var presetSpace = presets.Count == 0 ? 0 : 58;
        using var dialog = new Form { Text = $"Add Equipment — {EquipmentFields.TypeLabel(item.Category)}",
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(510, 490 + presetSpace),
            MinimumSize = new Size(430, 410), MaximizeBox = false, MinimizeBox = false, Font = Font };
        var modelLabel = new Label { Text = "Model (required)", Left = 14, Top = 12 + presetSpace, AutoSize = true };
        var model = new TextBox { Left = 14, Top = 34 + presetSpace, Width = 482, AccessibleName = "Model",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        using var fields = new PropertyGrid { Left = 14, Top = 72 + presetSpace, Width = 482, Height = 356,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ToolbarVisible = false, HelpVisible = false, SelectedObject = new EquipmentFields(item, adding: true) };
        var add = new Button { Text = "Add", Left = 324, Top = 446 + presetSpace, Width = 80, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Enabled = false };
        var cancel = new Button { Text = "Cancel", Left = 416, Top = 446 + presetSpace, Width = 80, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel, CausesValidation = false };
        if (presets.Count > 0)
        {
            var label = new Label { Text = "Equipment preset (optional)", Left = 14, Top = 12, AutoSize = true };
            var picker = new ComboBox { Left = 14, Top = 34, Width = 371, DropDownStyle = ComboBoxStyle.DropDownList,
                AutoCompleteSource = AutoCompleteSource.ListItems, AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AccessibleName = "Equipment preset", Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            picker.Items.AddRange(presets.Cast<object>().ToArray());
            var apply = new Button { Text = "Use preset", Left = 396, Top = 32, Width = 100, Height = 30, Enabled = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right };
            picker.SelectedIndexChanged += (_, _) => apply.Enabled = picker.SelectedItem is Equipment;
            apply.Click += (_, _) =>
            {
                if (picker.SelectedItem is not Equipment preset) return;
                EquipmentCatalog.Apply(preset, item);
                model.Text = item.Model;
                fields.SelectedObject = new EquipmentFields(item, adding: true);
            };
            dialog.Controls.AddRange([label, picker, apply]);
        }
        model.TextChanged += (_, _) => add.Enabled = !string.IsNullOrWhiteSpace(model.Text);
        fields.PropertyValueChanged += (_, e) =>
        {
            if (e.ChangedItem?.PropertyDescriptor?.Name == nameof(Equipment.GuideDeviceType))
                dialog.BeginInvoke(() => fields.SelectedObject = new EquipmentFields(item, adding: true));
        };
        add.Click += (_, _) =>
        {
            if (!dialog.ValidateChildren()) return;
            if (!ValidEquipment(item))
            {
                MessageBox.Show(dialog, "Enter finite, non-negative specifications. Focal length multiplier must be positive.",
                    dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            item.Model = model.Text.Trim();
            dialog.DialogResult = DialogResult.OK;
        };
        dialog.Controls.AddRange([modelLabel, model, fields, add, cancel]);
        dialog.AcceptButton = add;
        dialog.CancelButton = cancel;
        dialog.ActiveControl = model;
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    private void AddRig()
    {
        if (Prompt("Rig name") is not string name) return;
        if (_inventory.Rigs.Any(rig => string.Equals(rig.Name, name, StringComparison.OrdinalIgnoreCase))) { Notice("This rig name is already in use."); return; }
        var rig = new Rig { Name = name };
        _inventory.Rigs.Add(rig);
        Changed();
        RefreshLists(_equipment.SelectedItem as Equipment, rig);
    }

    private bool Confirm(string message) => MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private void DeleteRig()
    {
        if (SelectedRig is not Rig rig || !Confirm($"Delete the rig {rig.Name}? Its equipment stays in inventory.")) return;
        _inventory.Rigs.Remove(rig);
        Changed();
        RefreshLists(_equipment.SelectedItem as Equipment);
    }

    private void DeleteEquipment()
    {
        if (_equipment.SelectedItem is not Equipment item) return;
        if (_inventory.Rigs.Any(rig => rig.Items.Any(x => x.EquipmentId == item.Id)))
        { Notice("Remove this item from its rigs before deleting it from inventory."); return; }
        if (!Confirm($"Delete {item} from inventory?")) return;
        _inventory.Equipment.Remove(item);
        Changed();
        RefreshLists(rig: SelectedRig);
    }

    private void Assign()
    {
        _components.EndEdit();
        if (SelectedRig is not Rig rig) { Notice("Add or select a rig first."); return; }
        if (_inventory.Equipment.Count == 0) { Notice("Add items on the Equipment tab first."); return; }
        using var dialog = new Form { Text = "Assign equipment", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(425, 108), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var picker = new ComboBox { Left = 14, Top = 14, Width = 395, DropDownStyle = ComboBoxStyle.DropDownList,
            DataSource = _inventory.Equipment.OrderBy(item => item.ToString()).ToArray() };
        var ok = new Button { Text = "Assign", Left = 235, Top = 57, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 330, Top = 57, DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange([picker, ok, cancel]);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK || picker.SelectedItem is not Equipment item) return;
        if (rig.Items.Any(x => x.EquipmentId == item.Id)) { Notice("This inventory item is already assigned to this rig."); return; }
        var assignment = Assignment.For(item);
        rig.Items.Add(assignment);
        Changed();
        RefreshRig(assignment);
    }

    private void RemoveAssignment()
    {
        _components.EndEdit();
        if (SelectedRig is not Rig rig || _components.CurrentRow?.Tag is not Assignment assignment) return;
        rig.Items.Remove(assignment);
        Changed();
        RefreshRig();
    }

    private void MoveAssignment(int offset)
    {
        _components.EndEdit();
        if (SelectedRig is not Rig rig || _components.CurrentRow?.Tag is not Assignment assignment) return;
        var i = rig.Items.IndexOf(assignment);
        var target = i + offset;
        if (i < 0 || target < 0 || target >= rig.Items.Count) return;
        (rig.Items[i], rig.Items[target]) = (rig.Items[target], rig.Items[i]);
        Changed();
        RefreshRig(assignment);
    }

    private void Import()
    {
        _components.EndEdit();
        using var dialog = new OpenFileDialog { Title = "Import AstroArmory backup", Filter = "SQLite backup (*.db)|*.db|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var imported = BackupImport.Read(dialog.FileName);
        if (!Confirm($"Add {imported.Equipment.Count} equipment items and {imported.Rigs.Count} rigs to this inventory? Importing again adds another copy.")) return;
        foreach (var rig in imported.Rigs)
        {
            var original = rig.Name;
            for (int i = 2; _inventory.Rigs.Any(r => string.Equals(r.Name, rig.Name, StringComparison.OrdinalIgnoreCase)); i++)
                rig.Name = $"{original} ({i})";
            _inventory.Rigs.Add(rig);
        }
        _inventory.Equipment.AddRange(imported.Equipment);
        Changed();
        RefreshLists(imported.Equipment.FirstOrDefault(), imported.Rigs.FirstOrDefault());
        _status.Text = "Imported equipment and rigs. Click Save to keep the changes.";
    }

    private void Export()
    {
        _components.EndEdit();
        using var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = "AstroRig Inventory.xlsx", DefaultExt = "xlsx", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ExcelExport.Write(dialog.FileName, _inventory);
        UpdateFileStatus($"Exported {dialog.FileName}");
    }

    private bool ConfirmNavigation(string action)
    {
        _components.EndEdit();
        if (!_dirty) return true;
        var result = MessageBox.Show(this, $"Save your changes before {action}?", Text,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return result == DialogResult.No || result == DialogResult.Yes && TrySave(_path);
    }

    private static bool IsReservedFile(string path) =>
        string.Equals(Path.GetFullPath(path), LastPathFile, StringComparison.OrdinalIgnoreCase);

    private void OpenInventory()
    {
        using var dialog = new OpenFileDialog { Title = "Open inventory", Filter = "AstroRig inventory (*.json)|*.json",
            InitialDirectory = Path.GetDirectoryName(_path), CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!ConfirmNavigation("opening another inventory")) return;
        var path = Path.GetFullPath(dialog.FileName);
        if (!File.Exists(path)) { Notice("The selected inventory is no longer available."); return; }
        var loaded = Inventory.Load(path);
        _inventory = loaded;
        _path = path;
        _dirty = false;
        RefreshLists();
        UpdateFileStatus(RememberInventory() ?? "Opened");
    }

    private void SaveAs()
    {
        using var dialog = new SaveFileDialog { Title = "Save inventory as", Filter = "AstroRig inventory (*.json)|*.json",
            InitialDirectory = Path.GetDirectoryName(_path), FileName = Path.GetFileName(_path),
            DefaultExt = "json", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) TrySave(Path.GetFullPath(dialog.FileName));
    }

    private string? RememberInventory()
    {
        try
        {
            Directory.CreateDirectory(AppDirectory);
            File.WriteAllText(LastPathFile, _path);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "Inventory is ready. The app could not remember its location for next time.";
        }
    }

    private void UpdateFileStatus(string? message = null)
    {
        Text = $"{Path.GetFileName(_path)} — AstroRig {VersionLabel}";
        _status.Text = message == null ? _path : $"{message}  •  {_path}";
        _status.ToolTipText = _path;
    }

    private void Changed() { _dirty = true; UpdateFileStatus("Unsaved changes"); }
    private void Notice(string message) => MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    private void Save() => TrySave(_path);
    private bool TrySave(string path)
    {
        _components.EndEdit();
        try
        {
            if (IsReservedFile(path)) throw new IOException("Choose a different inventory filename.");
            _inventory.Save(path);
            _path = path;
            _dirty = false;
            UpdateFileStatus(RememberInventory() ?? "Saved");
            return true;
        }
        catch (Exception error) { Notice($"Could not save your inventory. {error.Message}"); return false; }
    }
}

