using Bunker;
using Xunit;

public class ServerImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bunker-import-" + Guid.NewGuid().ToString("N"));

    public ServerImportTests() => Directory.CreateDirectory(Path.Combine(_dir, "userCfg"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, "userCfg", name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Reads_name_address_and_rcon_port_from_a_RustAdmin_folder()
    {
        Write("cfg_My PvE Server.ini", "[General]\npassword=\"c2VjcmV0\"\nServerVersion=ExperimentalWeb\nip=203.0.113.9\nport=27101\nserverPort=27100\n\n[RustAdminAccount]\nip=ignored\n");
        var servers = ServerImporters.ReadRustAdmin(_dir);
        var s = Assert.Single(servers);
        Assert.Equal("My PvE Server", s.Name);
        Assert.Equal("203.0.113.9", s.Host);
        Assert.Equal(27101, s.Port);
        Assert.Equal("", s.Note);
    }

    [Fact]
    public void Accepts_the_userCfg_folder_or_a_single_file()
    {
        var file = Write("cfg_A.ini", "[General]\nip=localhost\nport=28507\n");
        Assert.Single(ServerImporters.ReadRustAdmin(Path.Combine(_dir, "userCfg")));
        Assert.Single(ServerImporters.ReadRustAdmin(file));
    }

    [Theory]
    [InlineData("[General]\nip=\nport=28016\n")]
    [InlineData("[General]\nip=host\nport=0\n")]
    [InlineData("[General]\nip=host\nport=99999\n")]
    [InlineData("[General]\nip=bad host!\nport=28016\n")]
    [InlineData("nothing useful here")]
    public void Skips_files_without_a_usable_address(string text)
    {
        Write("cfg_Bad.ini", text);
        Assert.Empty(ServerImporters.ReadRustAdmin(_dir));
    }

    [Fact]
    public void Warns_when_the_server_does_not_use_WebRCON()
    {
        Write("cfg_Old.ini", "[General]\nServerVersion=Legacy\nip=host.example.com\nport=28016\n");
        Assert.Contains("WebRCON", Assert.Single(ServerImporters.ReadRustAdmin(_dir)).Note);
    }

    [Fact]
    public void A_missing_folder_gives_an_empty_list() =>
        Assert.Empty(ServerImporters.ReadRustAdmin(Path.Combine(_dir, "nope")));
}
