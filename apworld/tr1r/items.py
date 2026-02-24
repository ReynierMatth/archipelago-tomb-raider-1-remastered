"""
Item definitions for Tomb Raider 1 Remastered Archipelago World.
Key items, weapons, ammo, and medipacks loaded from tr1r_data.json.
Traps and events are AP-only (not in game data).

ID Schema:
  - Key items/weapons/ammo/meds: from itemDefinitions in tr1r_data.json
  - Traps: 769000 + trap_index
  - Events: 795000 + level_index (matches level completion location IDs)
"""

import json
import pkgutil
from typing import Dict, NamedTuple, Optional

from BaseClasses import ItemClassification


class TR1RItemData(NamedTuple):
    ap_id: Optional[int]
    classification: ItemClassification
    category: str
    count: int = 1  # Default number to place in the pool


_CLASSIFICATION_MAP = {
    "progression": ItemClassification.progression,
    "useful": ItemClassification.useful,
    "filler": ItemClassification.filler,
}


_cached_data: Optional[dict] = None


def _load_data() -> dict:
    """Load exported game data from tr1r_data.json (ZIP-safe, cached)."""
    global _cached_data
    if _cached_data is None:
        raw = pkgutil.get_data(__package__, "data/tr1r_data.json")
        _cached_data = json.loads(raw.decode("utf-8"))
    return _cached_data


def _build_items_from_data():
    """Build item dictionaries from exported game data."""
    data = _load_data()
    key_items: Dict[str, TR1RItemData] = {}
    weapons: Dict[str, TR1RItemData] = {}
    ammo: Dict[str, TR1RItemData] = {}
    medipacks: Dict[str, TR1RItemData] = {}

    for item_def in data["itemDefinitions"].values():
        name = item_def["name"]
        ap_id = item_def["id"]
        category = item_def["category"]
        classification = _CLASSIFICATION_MAP.get(
            item_def["apClassification"], ItemClassification.filler
        )
        item_data = TR1RItemData(ap_id, classification, category)

        if category == "key_item":
            key_items[name] = item_data
        elif category == "weapon":
            weapons[name] = item_data
        elif category == "ammo":
            ammo[name] = item_data
        elif category in ("small_medipack", "large_medipack"):
            medipacks[name] = item_data

    return key_items, weapons, ammo, medipacks


# Load items from game data
KEY_ITEMS, WEAPONS, AMMO, MEDIPACKS = _build_items_from_data()

# Base ID (for reference, actual IDs come from JSON)
BASE_ID = 770_000

# -- Traps (AP-only, not in game data) --
TRAP_BASE_ID = 769_000
TRAPS: Dict[str, TR1RItemData] = {
    "Damage Trap": TR1RItemData(TRAP_BASE_ID + 1, ItemClassification.trap, "trap"),
    "Ammo Drain":  TR1RItemData(TRAP_BASE_ID + 2, ItemClassification.trap, "trap"),
    "Small Drain": TR1RItemData(TRAP_BASE_ID + 3, ItemClassification.trap, "trap"),
}

# -- Level Completion Events (locked items, real AP IDs matching location IDs) --
LEVEL_COMPLETE_BASE_ID = 795_000
_game_data = _load_data()
EVENTS: Dict[str, TR1RItemData] = {
    f"Level Complete - {level['name']}": TR1RItemData(
        LEVEL_COMPLETE_BASE_ID + i, ItemClassification.progression, "event"
    )
    for i, level in enumerate(_game_data["levels"])
}


def get_all_items() -> Dict[str, TR1RItemData]:
    """Returns all item definitions merged into a single dict (includes events)."""
    all_items: Dict[str, TR1RItemData] = {}
    all_items.update(KEY_ITEMS)
    all_items.update(WEAPONS)
    all_items.update(AMMO)
    all_items.update(MEDIPACKS)
    all_items.update(TRAPS)
    all_items.update(EVENTS)
    return all_items


# Reverse lookup: AP ID -> item name
_id_to_name: Dict[int, str] = {}
for _name, _data in get_all_items().items():
    if _data.ap_id is not None:
        _id_to_name[_data.ap_id] = _name


def get_item_name_from_id(item_id: int) -> Optional[str]:
    return _id_to_name.get(item_id)
