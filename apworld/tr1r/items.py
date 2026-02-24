"""
Item definitions for Tomb Raider 1 Remastered Archipelago World.

ID Schema (base 770000):
  - Key items:    770000 + alias_enum_value (e.g., 770000 + 11183 = 781183)
  - Weapons:      770000 + type_value
  - Ammo/Meds:    770000 + type_value
  - Traps:        769000 + trap_index
"""

from typing import Dict, NamedTuple, Optional

from BaseClasses import ItemClassification


class TR1RItemData(NamedTuple):
    ap_id: Optional[int]
    classification: ItemClassification
    category: str
    count: int = 1  # Default number to place in the pool


# Base ID for all TR1R items
BASE_ID = 770_000

# -- Key Items (Progression) --
# These use the TR1Type alias enum values as offsets
KEY_ITEMS: Dict[str, TR1RItemData] = {
    # Peru - Vilcabamba
    "Vilcabamba Silver Key":               TR1RItemData(BASE_ID + 11183, ItemClassification.progression, "key_item"),
    "Vilcabamba Gold Idol":                TR1RItemData(BASE_ID + 11143, ItemClassification.progression, "key_item"),
    # Peru - Lost Valley
    "Lost Valley Cog (Above Pool)":        TR1RItemData(BASE_ID + 12177, ItemClassification.progression, "key_item"),
    "Lost Valley Cog (Bridge)":            TR1RItemData(BASE_ID + 12242, ItemClassification.progression, "key_item"),
    "Lost Valley Cog (Temple)":            TR1RItemData(BASE_ID + 12241, ItemClassification.progression, "key_item"),
    # Greece - St. Francis' Folly
    "Folly Neptune Key":                   TR1RItemData(BASE_ID + 14315, ItemClassification.progression, "key_item"),
    "Folly Atlas Key":                     TR1RItemData(BASE_ID + 14299, ItemClassification.progression, "key_item"),
    "Folly Damocles Key":                  TR1RItemData(BASE_ID + 14290, ItemClassification.progression, "key_item"),
    "Folly Thor Key":                      TR1RItemData(BASE_ID + 14280, ItemClassification.progression, "key_item"),
    # Greece - Colosseum
    "Colosseum Rusty Key":                 TR1RItemData(BASE_ID + 15217, ItemClassification.progression, "key_item"),
    # Greece - Palace Midas
    "Midas Lead Bar (Fire Room)":          TR1RItemData(BASE_ID + 16178, ItemClassification.progression, "key_item"),
    "Midas Lead Bar (Spike Room)":         TR1RItemData(BASE_ID + 16157, ItemClassification.progression, "key_item"),
    "Midas Lead Bar (Temple Roof)":        TR1RItemData(BASE_ID + 16166, ItemClassification.progression, "key_item"),
    # Greece - The Cistern
    "Cistern Gold Key":                    TR1RItemData(BASE_ID + 17245, ItemClassification.progression, "key_item"),
    "Cistern Silver Key (Behind Door)":    TR1RItemData(BASE_ID + 17208, ItemClassification.progression, "key_item"),
    "Cistern Silver Key (Between Doors)":  TR1RItemData(BASE_ID + 17231, ItemClassification.progression, "key_item"),
    "Cistern Rusty Key (Main Room)":       TR1RItemData(BASE_ID + 17295, ItemClassification.progression, "key_item"),
    "Cistern Rusty Key (Near Pierre)":     TR1RItemData(BASE_ID + 17143, ItemClassification.progression, "key_item"),
    # Greece - Tomb of Tihocan
    "Tihocan Gold Key (Flip Map)":         TR1RItemData(BASE_ID + 18133, ItemClassification.progression, "key_item"),
    "Tihocan Gold Key (Pierre)":           TR1RItemData(BASE_ID + 18389, ItemClassification.progression, "key_item"),
    "Tihocan Rusty Key (Boulders)":        TR1RItemData(BASE_ID + 18277, ItemClassification.progression, "key_item"),
    "Tihocan Rusty Key (Clang Clang)":     TR1RItemData(BASE_ID + 18267, ItemClassification.progression, "key_item"),
    "Tihocan Scion":                       TR1RItemData(BASE_ID + 18444, ItemClassification.progression, "key_item"),
    # Egypt - City of Khamoon
    "Khamoon Sapphire Key (End)":          TR1RItemData(BASE_ID + 19193, ItemClassification.progression, "key_item"),
    "Khamoon Sapphire Key (Start)":        TR1RItemData(BASE_ID + 19217, ItemClassification.progression, "key_item"),
    # Egypt - Obelisk of Khamoon
    "Obelisk Sapphire Key (End)":          TR1RItemData(BASE_ID + 20213, ItemClassification.progression, "key_item"),
    "Obelisk Sapphire Key (Start)":        TR1RItemData(BASE_ID + 20308, ItemClassification.progression, "key_item"),
    "Obelisk Eye of Horus":               TR1RItemData(BASE_ID + 20160, ItemClassification.progression, "key_item"),
    "Obelisk Scarab":                      TR1RItemData(BASE_ID + 20151, ItemClassification.progression, "key_item"),
    "Obelisk Seal of Anubis":             TR1RItemData(BASE_ID + 20152, ItemClassification.progression, "key_item"),
    "Obelisk Ankh":                        TR1RItemData(BASE_ID + 20163, ItemClassification.progression, "key_item"),
    # Egypt - Sanctuary of the Scion
    "Sanctuary Gold Key":                  TR1RItemData(BASE_ID + 21191, ItemClassification.progression, "key_item"),
    "Sanctuary Ankh (After Key)":          TR1RItemData(BASE_ID + 21196, ItemClassification.progression, "key_item"),
    "Sanctuary Ankh (Behind Sphinx)":      TR1RItemData(BASE_ID + 21100, ItemClassification.progression, "key_item"),
    "Sanctuary Scarab":                    TR1RItemData(BASE_ID + 21202, ItemClassification.progression, "key_item"),
    # Atlantis - Natla's Mines
    "Mines Rusty Key":                     TR1RItemData(BASE_ID + 22137, ItemClassification.progression, "key_item"),
    "Mines Fuse (Boulder)":               TR1RItemData(BASE_ID + 22160, ItemClassification.progression, "key_item"),
    "Mines Fuse (Conveyor)":              TR1RItemData(BASE_ID + 22183, ItemClassification.progression, "key_item"),
    "Mines Fuse (Cowboy)":                TR1RItemData(BASE_ID + 22148, ItemClassification.progression, "key_item"),
    "Mines Fuse (Cowboy Alt)":            TR1RItemData(BASE_ID + 22146, ItemClassification.progression, "key_item"),
    "Mines Pyramid Key":                   TR1RItemData(BASE_ID + 22216, ItemClassification.progression, "key_item"),
}

# -- Weapons (Useful) --
WEAPONS: Dict[str, TR1RItemData] = {
    "Shotgun":  TR1RItemData(BASE_ID + 85,  ItemClassification.useful, "weapon"),
    "Magnums":  TR1RItemData(BASE_ID + 86,  ItemClassification.useful, "weapon"),
    "Uzis":     TR1RItemData(BASE_ID + 87,  ItemClassification.useful, "weapon"),
}

# -- Ammo (Filler) --
AMMO: Dict[str, TR1RItemData] = {
    "Shotgun Shells": TR1RItemData(BASE_ID + 89,  ItemClassification.filler, "ammo"),
    "Magnum Clips":   TR1RItemData(BASE_ID + 90,  ItemClassification.filler, "ammo"),
    "Uzi Clips":      TR1RItemData(BASE_ID + 91,  ItemClassification.filler, "ammo"),
}

# -- Medipacks --
MEDIPACKS: Dict[str, TR1RItemData] = {
    "Small Medipack": TR1RItemData(BASE_ID + 93,  ItemClassification.filler,  "small_medipack"),
    "Large Medipack": TR1RItemData(BASE_ID + 94,  ItemClassification.useful,  "large_medipack"),
}

# -- Traps --
TRAP_BASE_ID = 769_000
TRAPS: Dict[str, TR1RItemData] = {
    "Damage Trap":    TR1RItemData(TRAP_BASE_ID + 1, ItemClassification.trap, "trap"),
    "Ammo Drain":     TR1RItemData(TRAP_BASE_ID + 2, ItemClassification.trap, "trap"),
    "Small Drain":    TR1RItemData(TRAP_BASE_ID + 3, ItemClassification.trap, "trap"),
}

# -- Level Completion Events (no physical item) --
EVENTS: Dict[str, TR1RItemData] = {
    "Level Complete - Caves":               TR1RItemData(None, ItemClassification.progression, "event"),
    "Level Complete - City of Vilcabamba":   TR1RItemData(None, ItemClassification.progression, "event"),
    "Level Complete - Lost Valley":          TR1RItemData(None, ItemClassification.progression, "event"),
    "Level Complete - Tomb of Qualopec":     TR1RItemData(None, ItemClassification.progression, "event"),
    "Level Complete - St. Francis' Folly":   TR1RItemData(None, ItemClassification.progression, "event"),
    "Level Complete - Colosseum":            TR1RItemData(None, ItemClassification.progression, "event"),
    "Level Complete - Palace Midas":         TR1RItemData(None, ItemClassification.progression, "event"),
    "Level Complete - The Cistern":          TR1RItemData(None, ItemClassification.progression, "event"),
    "Level Complete - Tomb of Tihocan":      TR1RItemData(None, ItemClassification.progression, "event"),
    "Level Complete - City of Khamoon":      TR1RItemData(None, ItemClassification.progression, "event"),
    "Level Complete - Obelisk of Khamoon":   TR1RItemData(None, ItemClassification.progression, "event"),
    "Level Complete - Sanctuary of the Scion": TR1RItemData(None, ItemClassification.progression, "event"),
    "Level Complete - Natla's Mines":        TR1RItemData(None, ItemClassification.progression, "event"),
    "Level Complete - Atlantis":             TR1RItemData(None, ItemClassification.progression, "event"),
    "Level Complete - The Great Pyramid":    TR1RItemData(None, ItemClassification.progression, "event"),
}


def get_all_items() -> Dict[str, TR1RItemData]:
    """Returns all item definitions merged into a single dict."""
    all_items: Dict[str, TR1RItemData] = {}
    all_items.update(KEY_ITEMS)
    all_items.update(WEAPONS)
    all_items.update(AMMO)
    all_items.update(MEDIPACKS)
    all_items.update(TRAPS)
    # Events are not physical items - they're handled separately
    return all_items


# Build a reverse lookup: AP ID -> item name
_id_to_name: Dict[int, str] = {}
for _name, _data in get_all_items().items():
    if _data.ap_id is not None:
        _id_to_name[_data.ap_id] = _name


def get_item_name_from_id(item_id: int) -> Optional[str]:
    return _id_to_name.get(item_id)
