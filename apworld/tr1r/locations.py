"""
Location definitions for Tomb Raider 1 Remastered Archipelago World.
All data loaded from tr1r_data.json (exported by TRDataExporter).

ID Schema (must match client LocationMapper.cs):
  - Pickup/Key item locations: 780000 + level_index * 1000 + entity_index
  - Secret locations:          790000 + level_index * 10 + secret_index
  - Level completion:          795000 + level_index
"""

import json
import pkgutil
from typing import Dict, List, NamedTuple, Optional


class TR1RLocationData(NamedTuple):
    ap_id: Optional[int]
    region: str
    level: str
    category: str  # "pickup", "key_item", "secret", "level_complete"


# Base IDs (must match client LocationMapper.cs)
PICKUP_BASE_ID = 780_000
SECRET_BASE_ID = 790_000
LEVEL_COMPLETE_BASE_ID = 795_000

# Friendly names for pickup types
_PICKUP_TYPE_NAMES = {
    "SmallMed_S_P": "Small Medipack",
    "LargeMed_S_P": "Large Medipack",
    "Shotgun_S_P": "Shotgun",
    "Magnums_S_P": "Magnums",
    "Uzis_S_P": "Uzis",
    "ShotgunAmmo_S_P": "Shotgun Shells",
    "MagnumAmmo_S_P": "Magnum Clips",
    "UziAmmo_S_P": "Uzi Clips",
}


def _load_data() -> dict:
    """Load exported game data from tr1r_data.json (ZIP-safe)."""
    raw = pkgutil.get_data(__package__, "data/tr1r_data.json")
    return json.loads(raw.decode("utf-8"))


# Load once at module level
_game_data = _load_data()

# Level info list: [(name, file, region), ...]
LEVELS: List[tuple] = [
    (level["name"], level["file"], level["region"])
    for level in _game_data["levels"]
]

# Secret counts per level (from actual data, not hardcoded to 3)
SECRETS_PER_LEVEL: List[int] = [
    len(level["secrets"]) for level in _game_data["levels"]
]


def build_locations() -> Dict[str, TR1RLocationData]:
    """Build all location definitions from exported game data."""
    locations: Dict[str, TR1RLocationData] = {}

    for level_idx, level in enumerate(_game_data["levels"]):
        level_name = level["name"]
        region = level["region"]

        # -- Standard pickup locations --
        type_counters: Dict[str, int] = {}
        for pickup in level["pickups"]:
            entity_idx = pickup["entityIndex"]
            ap_id = PICKUP_BASE_ID + level_idx * 1000 + entity_idx
            pickup_type = pickup["type"]

            # Sequential numbering per type within the level
            type_counters[pickup_type] = type_counters.get(pickup_type, 0) + 1
            type_name = _PICKUP_TYPE_NAMES.get(pickup_type, pickup_type.replace("_S_P", ""))
            count = type_counters[pickup_type]
            loc_name = f"{level_name} - {type_name} {count}"

            locations[loc_name] = TR1RLocationData(
                ap_id=ap_id,
                region=region,
                level=level_name,
                category="pickup",
            )

        # -- Key item locations --
        for key_item in level["keyItems"]:
            entity_idx = key_item["entityIndex"]
            ap_id = PICKUP_BASE_ID + level_idx * 1000 + entity_idx
            loc_name = key_item["name"]  # e.g. "City of Vilcabamba - Silver Key"

            locations[loc_name] = TR1RLocationData(
                ap_id=ap_id,
                region=region,
                level=level_name,
                category="key_item",
            )

        # -- Secret locations (variable count per level) --
        for secret in level["secrets"]:
            secret_idx = secret["index"]
            ap_id = SECRET_BASE_ID + level_idx * 10 + secret_idx
            loc_name = f"{level_name} - Secret {secret_idx + 1}"

            locations[loc_name] = TR1RLocationData(
                ap_id=ap_id,
                region=region,
                level=level_name,
                category="secret",
            )

        # -- Level completion event --
        ap_id = LEVEL_COMPLETE_BASE_ID + level_idx
        loc_name = f"{level_name} - Complete"
        locations[loc_name] = TR1RLocationData(
            ap_id=ap_id,
            region=region,
            level=level_name,
            category="level_complete",
        )

    return locations


# Pre-built location table
location_table: Dict[str, TR1RLocationData] = build_locations()

# Reverse lookup: AP ID -> location name
location_id_to_name: Dict[int, str] = {
    data.ap_id: name for name, data in location_table.items() if data.ap_id is not None
}
