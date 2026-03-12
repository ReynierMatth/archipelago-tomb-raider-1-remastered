."""
Multi-game item definitions for TR Remastered Archipelago World.
Loads items from all available game data files (tr1r_data.json, tr2r_data.json, tr3r_data.json).

Generic items (weapons, ammo, medipacks) are prefixed with game key (TR1/TR2/TR3)
to avoid name collisions. Key items already have unique level-prefixed names.
Events use level names which are unique across games.

Traps are defined per-game as AP-only items (not in game data).
"""

from typing import Dict, List, NamedTuple, Optional

from BaseClasses import ItemClassification

from .game_data import GAME_CONFIGS, GameData, get_available_games, load_game


class TRItemData(NamedTuple):
    ap_id: Optional[int]
    classification: ItemClassification
    category: str
    game: str  # "tr1", "tr2", "tr3"


_CLASSIFICATION_MAP = {
    "progression": ItemClassification.progression,
    "useful": ItemClassification.useful,
    "filler": ItemClassification.filler,
}

_GENERIC_CATEGORIES = {"weapon", "ammo", "small_medipack", "large_medipack", "pickup"}

TRAP_NAMES = ["Damage Trap", "Ammo Drain", "Small Drain"]


def _prefix(game_key: str, name: str) -> str:
    """Prefix an item name with the game key (e.g. 'TR1 - Shotgun')."""
    return f"{game_key.upper()} - {name}"


def _build_game_items(game: GameData) -> Dict[str, TRItemData]:
    """Build item dict for one game from its JSON data."""
    items: Dict[str, TRItemData] = {}
    config = game.config
    key = config.key

    # Items from itemDefinitions
    for item_def in game.raw["itemDefinitions"].values():
        raw_name = item_def["name"]
        ap_id = item_def["id"]
        category = item_def["category"]
        classification = _CLASSIFICATION_MAP.get(
            item_def["apClassification"], ItemClassification.filler
        )

        # Prefix generic items to avoid cross-game name collisions
        if category in _GENERIC_CATEGORIES:
            name = _prefix(key, raw_name)
        else:
            name = raw_name

        items[name] = TRItemData(ap_id, classification, category, key)

    # Traps (AP-only, not in game data)
    for i, trap_name in enumerate(TRAP_NAMES):
        name = _prefix(key, trap_name)
        items[name] = TRItemData(
            config.trap_base + i + 1, ItemClassification.trap, "trap", key
        )

    # Level completion events
    for i, level in enumerate(game.levels):
        name = f"Level Complete - {level['name']}"
        items[name] = TRItemData(
            config.level_complete_base + i,
            ItemClassification.progression,
            "event",
            key,
        )

    return items


def _build_all_items() -> Dict[str, TRItemData]:
    """Build items from all available game data files."""
    all_items: Dict[str, TRItemData] = {}
    for game_key in get_available_games():
        game = load_game(game_key)
        if game is not None:
            all_items.update(_build_game_items(game))
    return all_items


# Pre-built at module load (class-level AP registration needs this)
ALL_ITEMS: Dict[str, TRItemData] = _build_all_items()


def get_items_for_game(game_key: str) -> Dict[str, TRItemData]:
    """Return only items belonging to a specific game."""
    return {n: d for n, d in ALL_ITEMS.items() if d.game == game_key}


def get_items_for_games(game_keys: List[str]) -> Dict[str, TRItemData]:
    """Return items for a set of enabled games."""
    keys = set(game_keys)
    return {n: d for n, d in ALL_ITEMS.items() if d.game in keys}


def get_items_by_category(items: Dict[str, TRItemData], category: str) -> Dict[str, TRItemData]:
    """Filter items dict by category."""
    return {n: d for n, d in items.items() if d.category == category}


# Reverse lookup: AP ID -> item name
_id_to_name: Dict[int, str] = {
    d.ap_id: n for n, d in ALL_ITEMS.items() if d.ap_id is not None
}


def get_item_name_from_id(item_id: int) -> Optional[str]:
    return _id_to_name.get(item_id)
