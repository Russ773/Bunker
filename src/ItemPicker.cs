using System.Drawing;

namespace Bunker;

/// <summary>
/// A text box that searches the Rust item list as you type. Matches drop down with each item's picture, name and shortname
/// (so typing "wood" shows "Wood" with its icon). Text holds the chosen shortname, which is what the server commands need.
/// </summary>
public class ItemPicker : ModernField
{
    private const int Rows = 7;
    private const int RowHeight = 44;

    private readonly Core _core;
    private readonly PictureBox _preview = new() { Width = 40, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Input };
    private readonly ListBox _list = new()
    {
        BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = RowHeight, IntegralHeight = false, Cursor = Cursors.Hand,
    };
    private readonly ToolStripDropDown _drop = new() { AutoClose = false, DropShadowEnabled = false, Padding = Padding.Empty, BackColor = Theme.Input };
    private bool _quiet;

    /// <summary>Raised when an item is picked from the list.</summary>
    public event Action<ItemInfo>? ItemChosen;

    public ItemPicker(Core core, string placeholder = "Search items, e.g. wood or rifle")
    {
        _core = core;
        Box.PlaceholderText = placeholder;
        AddTrailing(_preview);

        var host = new ToolStripControlHost(_list) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false };
        _drop.Items.Add(host);
        _list.DrawItem += DrawRow;
        _list.MouseClick += (_, e) =>
        {
            var index = _list.IndexFromPoint(e.Location);
            if (index >= 0 && _list.Items[index] is ItemInfo item) Choose(item);
        };
        _list.MouseMove += (_, e) =>
        {
            var index = _list.IndexFromPoint(e.Location);
            if (index >= 0 && index != _list.SelectedIndex) _list.SelectedIndex = index;
        };

        Box.TextChanged += (_, _) => { if (!_quiet) Refresh2(); };
        Box.KeyDown += OnKeyDown;
        Box.Leave += (_, _) => _drop.Close();
        _core.Items.IconLoaded += OnIconLoaded;
        Disposed += (_, _) => { _core.Items.IconLoaded -= OnIconLoaded; _drop.Dispose(); };
        UpdatePreview();
#if DEBUG
        // Test hook (debug builds only): type a search into the box, wait for the pictures and save a picture of the result list.
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_ITEMPICK") is { Length: > 0 } shot && Environment.GetEnvironmentVariable("BUNKER_START_PAGE") == "widgets:give")
            HandleCreated += async (_, _) =>
            {
                await Task.Delay(9000);
                _forceOpen = true;
                Text = Environment.GetEnvironmentVariable("BUNKER_TEST_ITEMQUERY") ?? "wood";
                await Task.Delay(6000);
                using var bmp = new Bitmap(_list.Width, _list.Height);
                _list.DrawToBitmap(bmp, new Rectangle(0, 0, _list.Width, _list.Height));
                bmp.Save(shot);
            };
#endif
    }

    /// <summary>The shortname typed or chosen.</summary>
    public string Shortname => (Text ?? "").Trim();

    private void OnIconLoaded(string _)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => { _list.Invalidate(); UpdatePreview(); }); } catch (InvalidOperationException) { }
    }

    private bool _forceOpen;

    private void Refresh2()
    {
        UpdatePreview();
        var matches = _core.Items.Search(Shortname, Rows);
        if (matches.Count == 0 || (!Box.Focused && !_forceOpen)) { _drop.Close(); return; }

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var m in matches) _list.Items.Add(m);
        _list.SelectedIndex = 0;
        _list.EndUpdate();

        var size = new Size(Width, matches.Count * RowHeight + 2);
        _list.Size = size;
        ((ToolStripControlHost)_drop.Items[0]).Size = size;
        _drop.Size = size;
        if (!_drop.Visible) _drop.Show(this, new Point(0, Height));
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_drop.Visible)
        {
            if (e.KeyCode == Keys.Down) { Refresh2(); e.Handled = true; }
            return;
        }
        switch (e.KeyCode)
        {
            case Keys.Down: _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1); e.Handled = e.SuppressKeyPress = true; break;
            case Keys.Up: _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1); e.Handled = e.SuppressKeyPress = true; break;
            case Keys.Enter or Keys.Tab when _list.SelectedItem is ItemInfo item: Choose(item); e.Handled = e.SuppressKeyPress = true; break;
            case Keys.Escape: _drop.Close(); e.Handled = e.SuppressKeyPress = true; break;
        }
    }

    private void Choose(ItemInfo item)
    {
        _quiet = true;
        Text = item.Shortname;
        _quiet = false;
        Box.SelectionStart = Box.TextLength;
        _drop.Close();
        UpdatePreview();
        ItemChosen?.Invoke(item);
    }

    /// <summary>Shows the picture of the item whose shortname is typed in the box, or nothing if it is not (yet) a known item.</summary>
    private void UpdatePreview()
    {
        var item = _core.Items.Find(Shortname);
        _preview.Image = item is null ? null : _core.Items.IconIfReady(item.Shortname);
        Tips.Set(_preview, item is null ? null : $"{item.Name}  ({item.Category})");
    }

    private void DrawRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _list.Items[e.Index] is not ItemInfo item) return;
        var g = e.Graphics;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? Theme.Selection : Theme.Input)) g.FillRectangle(back, e.Bounds);

        var icon = _core.Items.IconIfReady(item.Shortname);
        var box = new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 6, 32, 32);
        if (icon is not null) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(icon, box); }
        else using (var ph = new SolidBrush(Theme.GridLine)) g.FillRectangle(ph, box);

        var flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, item.Name, Theme.Semibold, new Rectangle(e.Bounds.X + 50, e.Bounds.Y + 5, e.Bounds.Width - 150, 20), Theme.Text, flags);
        TextRenderer.DrawText(g, item.Shortname, Theme.Small, new Rectangle(e.Bounds.X + 50, e.Bounds.Y + 24, e.Bounds.Width - 150, 16), Theme.Muted, flags);
        TextRenderer.DrawText(g, item.Category, Theme.Small, new Rectangle(e.Bounds.Right - 96, e.Bounds.Y, 88, e.Bounds.Height), Theme.Dim,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}
