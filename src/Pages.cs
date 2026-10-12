using System.Drawing;

namespace Bunker;

public static class Pages
{
    public static Control Dashboard(Core core)
    {
        var body = new Panel { BackColor = Theme.Bg };

        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        var players = new PlayersView(core);
        var playersCard = Card.Titled("Players", players, players.HeaderActions); playersCard.Dock = DockStyle.Fill;
        row.Controls.Add(playersCard, 0, 0);
        var console = new ConsoleView(core);
        var consoleCard = Card.Titled("Console", console, console.HeaderActions); consoleCard.Dock = DockStyle.Fill;
        row.Controls.Add(consoleCard, 1, 0);

        var stats = Card.Titled("Server", new ServerInfoView(core, 6));
        stats.Dock = DockStyle.Fill;
        var statsHost = new Panel { Dock = DockStyle.Top, Height = 156, BackColor = Theme.Bg, Padding = new Padding(6, 0, 6, 0) };
        statsHost.Controls.Add(stats);

        body.Controls.Add(row);
        body.Controls.Add(statsHost);
        return new Page("Dashboard", "A live overview of your server", body);
    }

    public static Control Console(Core core)
    {
        var v = new ConsoleView(core);
        return new Page("Console", "Everything the server prints, and a box to run commands",
            WrapCard(Card.Titled("Console", v, v.HeaderActions)));
    }

    public static Control Players(Core core)
    {
        var v = new PlayersView(core);
        return new Page("Players", "Who is on the server right now. Right-click a player to copy their ID, kick or ban",
            WrapCard(Card.Titled("Players", v, v.HeaderActions)));
    }

    private static Control WrapCard(Card card)
    {
        var p = new Panel { BackColor = Theme.Bg, Padding = new Padding(6) };
        card.Dock = DockStyle.Fill;
        p.Controls.Add(card);
        return p;
    }
}
