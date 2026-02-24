"""
Access rules for Tomb Raider 1 Remastered Archipelago World.

Rules define which items are required to reach specific locations within levels.
Key items gate progression within levels (e.g., need Silver Key to open a door).
Level completion gates progression between levels (handled in regions.py).

Phase 4 will add detailed intra-level rules based on routes.json sub-regions.
"""

from typing import TYPE_CHECKING

from BaseClasses import CollectionState

if TYPE_CHECKING:
    from . import TR1RWorld


def set_rules(world: "TR1RWorld") -> None:
    """Set access rules for all locations."""
    player = world.player
    multiworld = world.multiworld

    # -- Intra-level key item rules --
    # These ensure key items are required to reach certain locations within levels.
    # For now, we use a simplified model where key items gate the level completion.
    # Phase 4 will add sub-region rules based on routes.json for finer granularity.

    # Vilcabamba: need Silver Key and Gold Idol to complete
    _set_rule(multiworld, player, "City of Vilcabamba - Complete",
              lambda state: state.has("Vilcabamba Silver Key", player)
              and state.has("Vilcabamba Gold Idol", player))

    # Lost Valley: need all 3 cogs to complete
    _set_rule(multiworld, player, "Lost Valley - Complete",
              lambda state: state.has("Lost Valley Cog (Above Pool)", player)
              and state.has("Lost Valley Cog (Bridge)", player)
              and state.has("Lost Valley Cog (Temple)", player))

    # Folly: need all 4 keys to complete
    _set_rule(multiworld, player, "St. Francis' Folly - Complete",
              lambda state: state.has("Folly Neptune Key", player)
              and state.has("Folly Atlas Key", player)
              and state.has("Folly Damocles Key", player)
              and state.has("Folly Thor Key", player))

    # Colosseum: need Rusty Key to complete
    _set_rule(multiworld, player, "Colosseum - Complete",
              lambda state: state.has("Colosseum Rusty Key", player))

    # Palace Midas: need all 3 lead bars
    _set_rule(multiworld, player, "Palace Midas - Complete",
              lambda state: state.has("Midas Lead Bar (Fire Room)", player)
              and state.has("Midas Lead Bar (Spike Room)", player)
              and state.has("Midas Lead Bar (Temple Roof)", player))

    # Cistern: need Gold Key, Silver Keys, and Rusty Keys
    _set_rule(multiworld, player, "The Cistern - Complete",
              lambda state: state.has("Cistern Gold Key", player)
              and state.has("Cistern Silver Key (Behind Door)", player)
              and state.has("Cistern Silver Key (Between Doors)", player)
              and state.has("Cistern Rusty Key (Main Room)", player))

    # Tihocan: need Gold Keys and Rusty Keys
    _set_rule(multiworld, player, "Tomb of Tihocan - Complete",
              lambda state: state.has("Tihocan Gold Key (Flip Map)", player)
              and state.has("Tihocan Rusty Key (Boulders)", player))

    # Khamoon: need Sapphire Keys
    _set_rule(multiworld, player, "City of Khamoon - Complete",
              lambda state: state.has("Khamoon Sapphire Key (End)", player)
              and state.has("Khamoon Sapphire Key (Start)", player))

    # Obelisk: need Sapphire Keys and all 4 puzzle items
    _set_rule(multiworld, player, "Obelisk of Khamoon - Complete",
              lambda state: state.has("Obelisk Sapphire Key (End)", player)
              and state.has("Obelisk Sapphire Key (Start)", player)
              and state.has("Obelisk Eye of Horus", player)
              and state.has("Obelisk Scarab", player)
              and state.has("Obelisk Seal of Anubis", player)
              and state.has("Obelisk Ankh", player))

    # Sanctuary: need Gold Key, Ankhs, and Scarab
    _set_rule(multiworld, player, "Sanctuary of the Scion - Complete",
              lambda state: state.has("Sanctuary Gold Key", player)
              and state.has("Sanctuary Ankh (After Key)", player)
              and state.has("Sanctuary Ankh (Behind Sphinx)", player)
              and state.has("Sanctuary Scarab", player))

    # Mines: need Rusty Key, Fuses, and Pyramid Key
    _set_rule(multiworld, player, "Natla's Mines - Complete",
              lambda state: state.has("Mines Rusty Key", player)
              and state.has("Mines Fuse (Boulder)", player)
              and state.has("Mines Fuse (Conveyor)", player)
              and state.has("Mines Fuse (Cowboy)", player)
              and state.has("Mines Pyramid Key", player))


def _set_rule(multiworld, player: int, location_name: str, rule) -> None:
    """Set an access rule on a location, if it exists."""
    location = multiworld.get_location(location_name, player)
    if location is not None:
        location.access_rule = rule
