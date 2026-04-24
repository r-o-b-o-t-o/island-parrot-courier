namespace IslandParrotCourier.Services;

public interface IArchipelagoService
{
    /// <summary>Connects a single slot session. Safe to call when already connected.</summary>
    Task ConnectAsync(int gameId, string host, int port, string slotName);
    Task DisconnectAsync(int gameId);
    bool IsConnected(int gameId, string slotName);
    List<HintInfo> GetHints(int gameId, string slotName);
    /// <summary>Sends a hint request for the given item name and returns the matching hints.</summary>
    Task<List<HintInfo>> HintItemAsync(int gameId, string slotName, string itemName);
    List<PlayerProgress> GetProgress(int gameId);
}

public class HintInfo
{
    public string ItemName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string FindingSlot { get; set; } = "";
    public string FindingPlayerName { get; set; } = "";
    public string ReceivingSlot { get; set; } = "";
    public string ReceivingPlayerName { get; set; } = "";
    public bool Found { get; set; }
}

public class PlayerProgress
{
    public string Slot { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public long LocationsChecked { get; set; }
    public long TotalLocations { get; set; }
    public double Percentage => TotalLocations == 0 ? 0 : Math.Round((double)LocationsChecked / TotalLocations * 100, 2);
}
