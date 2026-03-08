using System.Collections.Concurrent;
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
    private readonly ConcurrentQueue<ItemInfo> _receivedItems = new();
    private readonly ConcurrentDictionary<long, byte> _checkedLocations = new();

    public SlotData? SlotData { get; private set; }
    public bool IsConnected => _session?.Socket?.Connected ?? false;

    /// <summary>The slot name used for the current connection.</summary>
    public string SlotName { get; private set; } = "";

    /// <summary>The seed hash for the current room, used to detect seed changes.</summary>
    public string Seed => _session?.RoomState?.Seed ?? "";

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

        SlotName = slotName;

        var result = await _session.LoginAsync(
            "Tomb Raider 1 Remastered",
            slotName,
            ItemsHandlingFlags.AllItems,
            tags: new[] { "TR1R", "DeathLink" },
            password: password,
            requestSlotData: true
        );

        if (!result.Successful)
        {
            var failure = (LoginFailure)result;
            var errors = string.Join(", ", failure.Errors);
            throw new Exception($"AP login failed: {errors}");
        }

        // Subscribe to bounce packets (DeathLink)
        _session.Socket.PacketReceived += OnPacketReceived;

        // Parse slot data from the login result
        var loginSuccess = (LoginSuccessful)result;
        if (loginSuccess.SlotData != null)
        {
            SlotData = SlotData.FromDictionary(loginSuccess.SlotData);
        }

        // Pre-populate checked locations from the server so we know which
        // locations were already sent in previous sessions. This prevents
        // duplicate sentinel removals when entity flags change on save reload.
        foreach (long locId in _session.Locations.AllLocationsChecked)
        {
            _checkedLocations.TryAdd(locId, 0);
        }

        return true;
    }

    /// <summary>Returns all location IDs that have been checked (sent to AP).</summary>
    public ICollection<long> GetCheckedLocations() => _checkedLocations.Keys;

    /// <summary>Returns true if this was a NEW check (not already sent).</summary>
    public bool SendLocationCheck(long locationId)
    {
        if (_checkedLocations.TryAdd(locationId, 0))
        {
            _session?.Locations.CompleteLocationChecks(locationId);
            return true;
        }
        return false;
    }

    public void SendLocationChecks(IEnumerable<long> locationIds)
    {
        var newChecks = locationIds.Where(id => _checkedLocations.TryAdd(id, 0)).ToArray();
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
        if (_receivedItems.TryDequeue(out var result))
        {
            item = result;
            return true;
        }
        item = default;
        return false;
    }

    private void OnPacketReceived(ArchipelagoPacketBase packet)
    {
        if (packet is BouncePacket bounce && bounce.Tags.Contains("DeathLink"))
        {
            string source = bounce.Data.TryGetValue("source", out var s) ? s.ToString() : "Unknown";
            string cause = bounce.Data.TryGetValue("cause", out var c) ? c.ToString() : "died";
            OnDeathLinkReceived?.Invoke($"{source}: {cause}");
        }
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
        if (_session != null)
        {
            _session.Socket.PacketReceived -= OnPacketReceived;
        }
        _session?.Socket?.DisconnectAsync();
    }
}
