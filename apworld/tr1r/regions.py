"""
Region definitions for Tomb Raider 1 Remastered Archipelago World.

Hierarchy:
  Menu -> Peru Hub -> [Caves, Vilcabamba, Lost Valley, Qualopec]
       -> Greece Hub -> [Folly, Colosseum, Midas, Cistern, Tihocan]
       -> Egypt Hub -> [Khamoon, Obelisk, Sanctuary]
       -> Atlantis Hub -> [Mines, Atlantis, Pyramid]

Each level is a region. Sub-regions within levels (based on key item gates)
will be added in Phase 4 using routes.json data.
"""

from typing import TYPE_CHECKING, Dict, List, Set

from BaseClasses import Entrance, Region

from .locations import TR1RLocationData, location_table

if TYPE_CHECKING:
    from . import TR1RWorld


def create_regions(world: "TR1RWorld") -> None:
    """Create all regions and connect them."""
    multiworld = world.multiworld
    player = world.player

    # Create all regions
    regions: Dict[str, Region] = {}

    region_names = [
        "Menu",
        # Hub regions
        "Peru Hub",
        "Greece Hub",
        "Egypt Hub",
        "Atlantis Hub",
        # Level regions
        "Caves",
        "City of Vilcabamba",
        "Lost Valley",
        "Tomb of Qualopec",
        "St. Francis' Folly",
        "Colosseum",
        "Palace Midas",
        "The Cistern",
        "Tomb of Tihocan",
        "City of Khamoon",
        "Obelisk of Khamoon",
        "Sanctuary of the Scion",
        "Natla's Mines",
        "Atlantis",
        "The Great Pyramid",
    ]

    for name in region_names:
        region = Region(name, player, multiworld)
        regions[name] = region
        multiworld.regions.append(region)

    # Place locations in their level regions
    for loc_name, loc_data in location_table.items():
        level_name = loc_data.level
        if level_name in regions:
            from BaseClasses import Location
            location = Location(player, loc_name, loc_data.ap_id, regions[level_name])
            regions[level_name].locations.append(location)

    # -- Connect regions --

    # Menu -> Peru Hub (always accessible)
    _connect(regions, "Menu", "Peru Hub")

    # Peru Hub -> individual Peru levels (sequential)
    _connect(regions, "Peru Hub", "Caves")
    _connect(regions, "Caves", "City of Vilcabamba",
             lambda state: state.has("Level Complete - Caves", player))
    _connect(regions, "City of Vilcabamba", "Lost Valley",
             lambda state: state.has("Level Complete - City of Vilcabamba", player))
    _connect(regions, "Lost Valley", "Tomb of Qualopec",
             lambda state: state.has("Level Complete - Lost Valley", player))

    # Peru -> Greece Hub (requires completing Qualopec)
    _connect(regions, "Tomb of Qualopec", "Greece Hub",
             lambda state: state.has("Level Complete - Tomb of Qualopec", player))

    # Greece Hub -> individual Greece levels (sequential)
    _connect(regions, "Greece Hub", "St. Francis' Folly")
    _connect(regions, "St. Francis' Folly", "Colosseum",
             lambda state: state.has("Level Complete - St. Francis' Folly", player))
    _connect(regions, "Colosseum", "Palace Midas",
             lambda state: state.has("Level Complete - Colosseum", player))
    _connect(regions, "Palace Midas", "The Cistern",
             lambda state: state.has("Level Complete - Palace Midas", player))
    _connect(regions, "The Cistern", "Tomb of Tihocan",
             lambda state: state.has("Level Complete - The Cistern", player))

    # Greece -> Egypt Hub (requires completing Tihocan)
    _connect(regions, "Tomb of Tihocan", "Egypt Hub",
             lambda state: state.has("Level Complete - Tomb of Tihocan", player))

    # Egypt Hub -> individual Egypt levels (sequential)
    _connect(regions, "Egypt Hub", "City of Khamoon")
    _connect(regions, "City of Khamoon", "Obelisk of Khamoon",
             lambda state: state.has("Level Complete - City of Khamoon", player))
    _connect(regions, "Obelisk of Khamoon", "Sanctuary of the Scion",
             lambda state: state.has("Level Complete - Obelisk of Khamoon", player))

    # Egypt -> Atlantis Hub (requires completing Sanctuary)
    _connect(regions, "Sanctuary of the Scion", "Atlantis Hub",
             lambda state: state.has("Level Complete - Sanctuary of the Scion", player))

    # Atlantis Hub -> individual Atlantis levels (sequential)
    _connect(regions, "Atlantis Hub", "Natla's Mines")
    _connect(regions, "Natla's Mines", "Atlantis",
             lambda state: state.has("Level Complete - Natla's Mines", player))
    _connect(regions, "Atlantis", "The Great Pyramid",
             lambda state: state.has("Level Complete - Atlantis", player))


def _connect(regions: Dict[str, Region], source: str, target: str,
             rule=None) -> Entrance:
    """Helper to connect two regions with an optional access rule."""
    entrance = regions[source].connect(regions[target])
    if rule is not None:
        entrance.access_rule = rule
    return entrance
