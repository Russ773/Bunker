using System.Drawing;
using System.Text.RegularExpressions;

namespace Bunker;

/// <summary>Give an item to one online player (inventory.giveto) or to everyone (inventory.giveall).</summary>
public class GiveItemsView : Panel
{
    internal static readonly Regex SafeItem = new(@"^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled);

    /// <summary>Common item shortnames for auto-complete. Any valid Rust shortname can still be typed.</summary>
    internal static readonly string[] Items =
    {
        "wood", "stones", "metal.ore", "metal.fragments", "metal.refined", "sulfur.ore", "sulfur", "charcoal", "gunpowder",
        "lowgradefuel", "cloth", "leather", "fat.animal", "scrap", "gears", "metalpipe", "metalspring", "rope", "sewingkit",
        "tarp", "techparts", "roadsigns", "sheetmetal", "semibody", "riflebody", "smgbody", "targetingcomputer", "cctvcamera",
        "ammo.rifle", "ammo.pistol", "ammo.shotgun", "ammo.shotgun.slug", "ammo.rifle.explosive", "ammo.rifle.hv",
        "ammo.pistol.hv", "ammo.rocket.basic", "ammo.rocket.hv", "arrow.wooden", "arrow.hv",
        "rifle.ak", "rifle.lr300", "rifle.bolt", "rifle.semiauto", "rifle.l96", "rifle.m39", "smg.thompson", "smg.mp5", "smg.2",
        "pistol.python", "pistol.m92", "pistol.semiauto", "pistol.revolver", "shotgun.pump", "shotgun.double", "shotgun.waterpipe",
        "shotgun.spas12", "lmg.m249", "rocket.launcher", "bow.hunting", "crossbow",
        "pickaxe", "hatchet", "axe.salvaged", "icepick.salvaged", "chainsaw", "jackhammer",
        "syringe.medical", "bandage", "largemedkit", "antiradpills",
    };

    private readonly Core _core;
    private readonly PlayerPicker _picker;
    private readonly ModernField _item = new();
    private readonly ModernField _amount = new();
    private readonly Button _giveOne = Theme.PrimaryButton("Give to player");
    private readonly Button _giveAll = Theme.GhostButton("Give to everyone");
    private readonly Label _status = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, MaximumSize = new Size(420, 0), Margin = new Padding(0, 10, 0, 0) };

    public GiveItemsView(Core core)
    {
        _core = core;
        _picker = new PlayerPicker(core, "Select a player");
        BackColor = Theme.Panel;
        AutoScroll = true;
        DarkScroll.Apply(this);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = Theme.Panel,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows, Padding = new Padding(0, 4, 0, 0),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));


        _item.Dock = DockStyle.Top;
        _item.Box.PlaceholderText = "Item shortname, e.g. wood or rifle.ak";
        _item.Box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _item.Box.AutoCompleteSource = AutoCompleteSource.CustomSource;
        var source = new AutoCompleteStringCollection();
        source.AddRange(Items);
        _item.Box.AutoCompleteCustomSource = source;

        _amount.Dock = DockStyle.Top;
        _amount.Text = "100";
        _amount.Box.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(0, 12, 0, 0), BackColor = Theme.Panel, WrapContents = false };
        _giveOne.Height = 36;
        _giveAll.Height = 36;
        _giveAll.Margin = new Padding(8, 0, 0, 0);
        buttons.Controls.Add(_giveOne);
        buttons.Controls.Add(_giveAll);

        // Item and amount share one row to keep the widget short enough for a half-height slot.
        var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, AutoSize = true, BackColor = Theme.Panel, Margin = new Padding(0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        row.Controls.Add(Caption("Item"), 0, 0);
        row.Controls.Add(Caption("Amount"), 1, 0);
        _item.Margin = new Padding(0, 0, 8, 0);
        _amount.Margin = new Padding(0);
        _item.Dock = _amount.Dock = DockStyle.Top;
        row.Controls.Add(_item, 0, 1);
        row.Controls.Add(_amount, 1, 1);

        foreach (var c in new Control[] { _picker.Button, row, buttons, _status })
        {
            c.Dock = DockStyle.Top;
            table.Controls.Add(c);
        }
        Controls.Add(table);

        _giveOne.Click += async (_, _) => await GiveToPlayerAsync();
        _giveAll.Click += async (_, _) => await GiveToAllAsync();
        _core.StateChanged += ApplyEnabled;
        ApplyEnabled();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _core.StateChanged -= ApplyEnabled;
        base.Dispose(disposing);
    }

    private static Label Caption(string text) =>
        new() { Text = text, ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, Margin = new Padding(0, 10, 0, 4) };

    private void ApplyEnabled()
    {
        var on = _core.State == ConnState.Connected;
        _giveOne.Enabled = _giveAll.Enabled = _picker.Button.Enabled = on;
        if (!on) Say("Connect to a server to give items.", Theme.Muted);
        else if (_status.Text.StartsWith("Connect to")) Say("", Theme.Muted);
    }

    private void Say(string text, Color color) { _status.Text = text; _status.ForeColor = color; }

    private bool TryReadItem(out string item, out int amount)
    {
        item = _item.Text.Trim();
        amount = 0;
        if (!SafeItem.IsMatch(item)) { Say("Enter an item shortname such as wood or rifle.ak (letters, numbers, dots and dashes only).", Theme.Err); return false; }
        if (!int.TryParse(_amount.Text.Trim(), out amount) || amount is < 1 or > 100000) { Say("The amount must be a number from 1 to 100,000.", Theme.Err); return false; }
        return true;
    }

    private async Task GiveToPlayerAsync()
    {
        if (_picker.SteamId is null) { Say("Select a player first.", Theme.Err); return; }
        if (!TryReadItem(out var item, out var amount)) return;
        await _core.SendCommandAsync($"inventory.giveto \"{_picker.SteamId}\" \"{item}\" {amount}");
        Say($"Sent: {amount} x {item} to {_picker.PlayerName}. Check the console for the server's reply.", Theme.Ok);
    }

    private async Task GiveToAllAsync()
    {
        if (!TryReadItem(out var item, out var amount)) return;
        var everyone = _core.Monitor.Players.Count;
        var answer = MessageBox.Show(FindForm(),
            $"Give {amount} x {item} to EVERY player on the server ({everyone} online right now)?",
            "Give to everyone", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;
        await _core.SendCommandAsync($"inventory.giveall \"{item}\" {amount}");
        Say($"Sent: {amount} x {item} to everyone. Check the console for the server's reply.", Theme.Ok);
    }
}
