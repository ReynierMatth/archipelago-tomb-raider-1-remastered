using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using Newtonsoft.Json.Linq;

namespace TRArchipelagoClient.Core;

/// <summary>
/// Wraps the Archipelago.MultiClient.Net session for TR1R.
/// Handles connection, item receiving, and location sending.
/// </summary>
public class APSession
{
    private ArchipelagoSession? _session;
    private readonly Queue<ItemInfo> _receivedItems = new();
    private readonly HashSet<long> _checkedLocations = new();

    public SlotData? SlotData { get; private set; }
    public bool IsConnected => _session?.Socket?.Connected ?? false;

    public event Action<ItemInfo>? OnItemReceived;
    public event Action<string>? OnDeathLinkReceived;

    public async Task<bool> ConnectAsync(string server, string slotName, string password = "")
    {
        _session = ArchipelagoSessionFactory.CreateSession(server);

        // Subscribe to item received events
        _session.Items.ItemReceived += (helper) =>
        {
            var item = helper.DequeueItem();
            _receivedItems.Enqueue(item);
            OnItemReceived?.Invoke(item);
        };

        // Connect then login (two-step async)
        await _session.ConnectAsync();

        var result = await _session.LoginAsync(
            "Tomb Raider 1 Remastered",
            slotName,
            ItemsHandlingFlags.AllItems,
            tags: new[] { "TR1R" },
            password: password,
            requestSlotData: true
        );

        if (!result.Successful)
        {
            var failure = (LoginFailure)result;
            var errors = string.Join(", ", failure.Errors);
            throw new Exception($"AP login failed: {errors}");
        }

        // Parse slot data from the login result
        var loginSuccess = (LoginSuccessful)result;
        if (loginSuccess.SlotData != null)
        {
            SlotData = SlotData.FromDictionary(loginSuccess.SlotData);
        }

        return true;
    }

    public void SendLocationCheck(long locationId)
    {
        if (!_checkedLocations.Contains(locationId))
        {
            _checkedLocations.Add(locationId);
            _session?.Locations.CompleteLocationChecks(locationId);
        }
    }

    public void SendLocationChecks(IEnumerable<long> locationIds)
    {
        var newChecks = locationIds.Where(id => !_checkedLocations.Contains(id)).ToArray();
        foreach (var id in newChecks)
        {
            _checkedLocations.Add(id);
        }
        if (newChecks.Length > 0)
        {
            _session?.Locations.CompleteLocationChecks(newChecks);
        }
    }

    public void SendGoalComplete()
    {
        var packet = new StatusUpdatePacket
        {
            Status = ArchipelagoClientState.ClientGoal
        };
        _session?.Socket.SendPacket(packet);
    }

    public void SendDeathLink(string cause = "Lara died")
    {
        _session?.Socket.SendPacket(new BouncePacket
        {
            Tags = new List<string> { "DeathLink" },
            Data = new Dictionary<string, JToken>
            {
                ["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
                ["cause"] = cause,
                ["source"] = _session.Players.GetPlayerName(_session.ConnectionInfo.Slot),
            }
        });
    }

    public bool TryDequeueReceivedItem(out ItemInfo? item)
    {
        if (_receivedItems.Count > 0)
        {
            item = _receivedItems.Dequeue();
            return true;
        }
        item = default;
        return false;
    }

    public string GetItemName(long itemId)
    {
        return _session?.Items.GetItemName(itemId) ?? $"Unknown Item ({itemId})";
    }

    public string GetPlayerName(int slot)
    {
        return _session?.Players.GetPlayerName(slot) ?? $"Player {slot}";
    }

    public string GetLocationName(long locationId)
    {
        return _session?.Locations.GetLocationNameFromId(locationId) ?? $"Unknown Location ({locationId})";
    }

    public void Disconnect()
    {
        _session?.Socket?.DisconnectAsync();
    }
}
