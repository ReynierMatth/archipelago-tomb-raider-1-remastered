"""
Location definitions for Tomb Raider 1 Remastered Archipelago World.

ID Schema:
  - Pickup locations:    780000 + level_index * 1000 + entity_index
  - Secret locations:    790000 + level_index * 10 + secret_index
  - Level completion:    795000 + level_index
"""

from typing import Dict, List, NamedTuple, Optional

from BaseClasses import ItemClassification


class TR1RLocationData(NamedTuple):
    ap_id: Optional[int]
    region: str
    level: str
    category: str  # "pickup", "key_item", "secret", "level_complete"


# Base IDs
PICKUP_BASE_ID = 780_000
SECRET_BASE_ID = 790_000
LEVEL_COMPLETE_BASE_ID = 795_000

# Level index mapping (0-based)
LEVELS = [
    ("Caves",                     "LEVEL1.PHD",  "Peru"),
    ("City of Vilcabamba",        "LEVEL2.PHD",  "Peru"),
    ("Lost Valley",               "LEVEL3A.PHD", "Peru"),
    ("Tomb of Qualopec",          "LEVEL3B.PHD", "Peru"),
    ("St. Francis' Folly",        "LEVEL4.PHD",  "Greece"),
    ("Colosseum",                 "LEVEL5.PHD",  "Greece"),
    ("Palace Midas",              "LEVEL6.PHD",  "Greece"),
    ("The Cistern",               "LEVEL7A.PHD", "Greece"),
    ("Tomb of Tihocan",           "LEVEL7B.PHD", "Greece"),
    ("City of Khamoon",           "LEVEL8A.PHD", "Egypt"),
    ("Obelisk of Khamoon",        "LEVEL8B.PHD", "Egypt"),
    ("Sanctuary of the Scion",    "LEVEL8C.PHD", "Egypt"),
    ("Natla's Mines",             "LEVEL10A.PHD","Atlantis"),
    ("Atlantis",                  "LEVEL10B.PHD","Atlantis"),
    ("The Great Pyramid",         "LEVEL10C.PHD","Atlantis"),
]

# Number of secrets per level (3 each for all 15 levels = 45 total)
SECRETS_PER_LEVEL = 3


def build_locations() -> Dict[str, TR1RLocationData]:
    """
    Build all location definitions.

    For now, key item locations are built from the known alias data.
    Pickup locations will be populated from tr1r_data.json or level files.
    """
    locations: Dict[str, TR1RLocationData] = {}

    for level_idx, (level_name, level_file, region) in enumerate(LEVELS):
        # -- Secret locations (3 per level) --
        for secret_idx in range(SECRETS_PER_LEVEL):
            secret_id = SECRET_BASE_ID + level_idx * 10 + secret_idx
            loc_name = f"{level_name} - Secret {secret_idx + 1}"
            locations[loc_name] = TR1RLocationData(
                ap_id=secret_id,
                region=region,
                level=level_name,
                category="secret",
            )

        # -- Level completion event --
        complete_id = LEVEL_COMPLETE_BASE_ID + level_idx
        loc_name = f"{level_name} - Complete"
        locations[loc_name] = TR1RLocationData(
            ap_id=complete_id,
            region=region,
            level=level_name,
            category="level_complete",
        )

    # -- Key item pickup locations (one per known key item alias) --
    _add_key_item_locations(locations)

    return locations


def _add_key_item_locations(locations: Dict[str, TR1RLocationData]) -> None:
    """Add locations for each key item pickup spot."""
    # These correspond to the places in each level where key items sit.
    # Format: (location_name, level_index, entity_index_placeholder, level_name, region)
    key_item_locations = [
        # Vilcabamba (level_idx=1)
        ("City of Vilcabamba - Silver Key",               1, 183, "City of Vilcabamba", "Peru"),
        ("City of Vilcabamba - Gold Idol",                1, 143, "City of Vilcabamba", "Peru"),
        # Lost Valley (level_idx=2)
        ("Lost Valley - Cog (Above Pool)",                2, 177, "Lost Valley", "Peru"),
        ("Lost Valley - Cog (Bridge)",                    2, 242, "Lost Valley", "Peru"),
        ("Lost Valley - Cog (Temple)",                    2, 241, "Lost Valley", "Peru"),
        # Folly (level_idx=4)
        ("St. Francis' Folly - Neptune Key",              4, 315, "St. Francis' Folly", "Greece"),
        ("St. Francis' Folly - Atlas Key",                4, 299, "St. Francis' Folly", "Greece"),
        ("St. Francis' Folly - Damocles Key",             4, 290, "St. Francis' Folly", "Greece"),
        ("St. Francis' Folly - Thor Key",                 4, 280, "St. Francis' Folly", "Greece"),
        # Colosseum (level_idx=5)
        ("Colosseum - Rusty Key",                         5, 217, "Colosseum", "Greece"),
        # Midas (level_idx=6)
        ("Palace Midas - Lead Bar (Fire Room)",           6, 178, "Palace Midas", "Greece"),
        ("Palace Midas - Lead Bar (Spike Room)",          6, 157, "Palace Midas", "Greece"),
        ("Palace Midas - Lead Bar (Temple Roof)",         6, 166, "Palace Midas", "Greece"),
        # Cistern (level_idx=7)
        ("The Cistern - Gold Key",                        7, 245, "The Cistern", "Greece"),
        ("The Cistern - Silver Key (Behind Door)",        7, 208, "The Cistern", "Greece"),
        ("The Cistern - Silver Key (Between Doors)",      7, 231, "The Cistern", "Greece"),
        ("The Cistern - Rusty Key (Main Room)",           7, 295, "The Cistern", "Greece"),
        ("The Cistern - Rusty Key (Near Pierre)",         7, 143, "The Cistern", "Greece"),
        # Tihocan (level_idx=8)
        ("Tomb of Tihocan - Gold Key (Flip Map)",         8, 133, "Tomb of Tihocan", "Greece"),
        ("Tomb of Tihocan - Gold Key (Pierre)",           8, 389, "Tomb of Tihocan", "Greece"),
        ("Tomb of Tihocan - Rusty Key (Boulders)",        8, 277, "Tomb of Tihocan", "Greece"),
        ("Tomb of Tihocan - Rusty Key (Clang Clang)",     8, 267, "Tomb of Tihocan", "Greece"),
        ("Tomb of Tihocan - Scion",                       8, 444, "Tomb of Tihocan", "Greece"),
        # Khamoon (level_idx=9)
        ("City of Khamoon - Sapphire Key (End)",          9, 193, "City of Khamoon", "Egypt"),
        ("City of Khamoon - Sapphire Key (Start)",        9, 217, "City of Khamoon", "Egypt"),
        # Obelisk (level_idx=10)
        ("Obelisk of Khamoon - Sapphire Key (End)",       10, 213, "Obelisk of Khamoon", "Egypt"),
        ("Obelisk of Khamoon - Sapphire Key (Start)",     10, 308, "Obelisk of Khamoon", "Egypt"),
        ("Obelisk of Khamoon - Eye of Horus",             10, 160, "Obelisk of Khamoon", "Egypt"),
        ("Obelisk of Khamoon - Scarab",                   10, 151, "Obelisk of Khamoon", "Egypt"),
        ("Obelisk of Khamoon - Seal of Anubis",           10, 152, "Obelisk of Khamoon", "Egypt"),
        ("Obelisk of Khamoon - Ankh",                     10, 163, "Obelisk of Khamoon", "Egypt"),
        # Sanctuary (level_idx=11)
        ("Sanctuary of the Scion - Gold Key",             11, 191, "Sanctuary of the Scion", "Egypt"),
        ("Sanctuary of the Scion - Ankh (After Key)",     11, 196, "Sanctuary of the Scion", "Egypt"),
        ("Sanctuary of the Scion - Ankh (Behind Sphinx)", 11, 100, "Sanctuary of the Scion", "Egypt"),
        ("Sanctuary of the Scion - Scarab",               11, 202, "Sanctuary of the Scion", "Egypt"),
        # Mines (level_idx=12)
        ("Natla's Mines - Rusty Key",                     12, 137, "Natla's Mines", "Atlantis"),
        ("Natla's Mines - Fuse (Boulder)",                12, 160, "Natla's Mines", "Atlantis"),
        ("Natla's Mines - Fuse (Conveyor)",               12, 183, "Natla's Mines", "Atlantis"),
        ("Natla's Mines - Fuse (Cowboy)",                 12, 148, "Natla's Mines", "Atlantis"),
        ("Natla's Mines - Fuse (Cowboy Alt)",             12, 146, "Natla's Mines", "Atlantis"),
        ("Natla's Mines - Pyramid Key",                   12, 216, "Natla's Mines", "Atlantis"),
    ]

    for loc_name, level_idx, entity_idx, level_name, region in key_item_locations:
        ap_id = PICKUP_BASE_ID + level_idx * 1000 + entity_idx
        locations[loc_name] = TR1RLocationData(
            ap_id=ap_id,
            region=region,
            level=level_name,
            category="key_item",
        )


# Pre-built location table
location_table: Dict[str, TR1RLocationData] = build_locations()

# Reverse lookup: AP ID -> location name
location_id_to_name: Dict[int, str] = {
    data.ap_id: name for name, data in location_table.items() if data.ap_id is not None
}
