"""
Data-driven access rules for TR Remastered Archipelago World.

Two layers of rules:
  1. Level completion: requires ALL key items for that level
  2. Per-pickup gating: pickups behind locked doors require specific key items

Pickup gating uses requiredKeyItems from route analysis (populated by exporter).
"""

import re
from collections import defaultdict
from typing import TYPE_CHECKING, Dict, List, Set

from .game_data import GameData, load_game
from .locations import _PICKUP_TYPE_NAMES

if TYPE_CHECKING:
    from . import TR1RWorld


def set_rules(world: "TR1RWorld", enabled_games: List[str]) -> None:
    """Set access rules for all enabled games."""
    player = world.player
    multiworld = world.multiworld

    for game_key in enabled_games:
        game = load_game(game_key)
        if game is None:
            continue

        # Level completion rules (require all key items for the level)
        has_item_name = any(
            ki.get("itemName")
            for level in game.levels
            for ki in level.get("keyItems", [])
        )

        if has_item_name:
            _set_rules_direct(multiworld, player, game)
        else:
            _set_rules_prefix_match(multiworld, player, game)

        # Per-pickup gating rules (pickups behind locked doors)
        _set_pickup_gating_rules(multiworld, player, game, game_key)


def _set_rules_direct(multiworld, player: int, game: GameData) -> None:
    """Use the itemName field directly from key items."""
    for level in game.levels:
        key_items = level.get("keyItems", [])
        if not key_items:
            continue
        required = list({ki["itemName"] for ki in key_items if ki.get("itemName")})
        if required:
            _set_completion_rule(multiworld, player, level["name"], required)


def _set_rules_prefix_match(multiworld, player: int, game: GameData) -> None:
    """Fallback: match key items to definitions by level prefix in JSON keys.

    keyDependencies keys share a level prefix with itemDefinitions keys.
    E.g. keyDep "vilcabamba_Puzzle1" and itemDef "vilcabamba_P1_GoldIdol"
    both start with "vilcabamba", indicating they belong to the same level.
    """
    raw = game.raw
    key_deps = raw.get("keyDependencies", {})
    item_defs = raw.get("itemDefinitions", {})

    # Step 1: level_file -> set of prefixes (from keyDependencies)
    level_to_prefixes: Dict[str, Set[str]] = {}
    for alias, dep in key_deps.items():
        prefix = alias.split("_")[0].lower()
        level_file = dep.get("level", "")
        level_to_prefixes.setdefault(level_file, set()).add(prefix)

    # Step 2: prefix -> [item_def_names] (from itemDefinitions, key_items only)
    prefix_to_items: Dict[str, List[str]] = {}
    for key, idef in item_defs.items():
        if idef.get("category") != "key_item":
            continue
        prefix = key.split("_")[0].lower()
        prefix_to_items.setdefault(prefix, []).append(idef["name"])

    # Step 3: for each level with key items, require all matching definitions
    for level in game.levels:
        if not level.get("keyItems"):
            continue
        level_file = level["file"]
        prefixes = level_to_prefixes.get(level_file, set())
        required: List[str] = []
        for prefix in prefixes:
            required.extend(prefix_to_items.get(prefix, []))

        if required:
            _set_completion_rule(multiworld, player, level["name"], required)


def _alias_type_to_abbrev(alias_type: str) -> str:
    """Convert keyItem alias type to itemDefinition key abbreviation.

    Key1 -> K1, Puzzle1 -> P1, LeadBar -> LeadBar, etc.
    """
    m = re.match(r"^Key(\d+)$", alias_type)
    if m:
        return f"K{m.group(1)}"
    m = re.match(r"^Puzzle(\d+)$", alias_type)
    if m:
        return f"P{m.group(1)}"
    return alias_type


def _build_key_item_name_map(game: GameData) -> Dict[str, str]:
    """Build mapping from keyItem location names to itemDefinition item names.

    requiredKeyItems arrays use keyItem location names (e.g.,
    "City of Vilcabamba - Silver Key") but items in the pool use
    itemDefinition names (e.g., "Vilcabamba Silver Key").

    Matching uses keyItem aliases and itemDefinition key patterns:
      alias "Vilcabamba_Key1"  -> itemDef key "vilcabamba_K1_SilverKey"
      alias "Valley_Puzzle1_2" -> 2nd itemDef with key "valley_P1_*"

    When TR engine key slot numbers have gaps (e.g., Tihocan uses Key1 and
    Key3, skipping Key2), the exporter renumbers itemDef keys sequentially
    (K1, K2). A fallback handles this by mapping slot positions to the
    available sequential abbreviation numbers.
    """
    raw = game.raw
    item_defs = raw.get("itemDefinitions", {})

    # Group key_item definitions by (prefix, type_abbrev),
    # preserving JSON insertion order for duplicate matching.
    def_groups: Dict[tuple, List[str]] = defaultdict(list)
    for def_key, idef in item_defs.items():
        if idef.get("category") != "key_item":
            continue
        parts = def_key.split("_", 2)
        if len(parts) < 2:
            continue
        prefix = parts[0].lower()
        type_abbrev = parts[1]
        def_groups[(prefix, type_abbrev)].append(idef["name"])

    # Collect available abbreviation numbers per (prefix, base_letter)
    # e.g., tihocan K -> [1, 2]
    avail_nums: Dict[tuple, List[int]] = defaultdict(list)
    for prefix, abbrev in def_groups:
        m = re.match(r"^([A-Za-z]+?)(\d+)$", abbrev)
        if m:
            base_letter = m.group(1)
            num = int(m.group(2))
            if num not in avail_nums[(prefix, base_letter)]:
                avail_nums[(prefix, base_letter)].append(num)
    for key in avail_nums:
        avail_nums[key].sort()

    # Collect unique key slot numbers per (prefix, base_word) from keyItems
    # e.g., tihocan Key -> [1, 3]
    slot_nums: Dict[tuple, List[int]] = defaultdict(list)
    for level in game.levels:
        for key_item in level.get("keyItems", []):
            alias = key_item.get("alias", "")
            if not alias:
                continue
            parts = alias.split("_")
            prefix = parts[0].lower()
            if len(parts) >= 3 and parts[-1].isdigit():
                alias_type = "_".join(parts[1:-1])
            else:
                alias_type = "_".join(parts[1:])
            m = re.match(r"^(Key|Puzzle)(\d+)$", alias_type)
            if m:
                base_word = m.group(1)
                slot_num = int(m.group(2))
                if slot_num not in slot_nums[(prefix, base_word)]:
                    slot_nums[(prefix, base_word)].append(slot_num)
    for key in slot_nums:
        slot_nums[key].sort()

    def _resolve_abbrev(prefix: str, alias_type: str) -> str:
        """Resolve alias type to itemDef abbreviation, with renumber fallback."""
        direct = _alias_type_to_abbrev(alias_type)
        if (prefix, direct) in def_groups:
            return direct

        # Renumber: Key3 with slots [1,3] and avail [K1,K2] -> K2
        m = re.match(r"^(Key|Puzzle)(\d+)$", alias_type)
        if not m:
            return direct
        base_word = m.group(1)
        slot_num = int(m.group(2))
        base_letter = "K" if base_word == "Key" else "P"

        slots = slot_nums.get((prefix, base_word), [])
        avail = avail_nums.get((prefix, base_letter), [])
        if slot_num in slots and avail:
            idx = slots.index(slot_num)
            if idx < len(avail):
                return f"{base_letter}{avail[idx]}"
        return direct

    # For each keyItem, resolve its alias to an itemDefinition name.
    name_map: Dict[str, str] = {}

    for level in game.levels:
        for key_item in level.get("keyItems", []):
            loc_name = key_item["name"]
            alias = key_item.get("alias", "")
            if not alias:
                continue

            # Parse alias: "Prefix_Type" or "Prefix_Type_N" (N = duplicate index)
            parts = alias.split("_")
            prefix = parts[0].lower()

            if len(parts) >= 3 and parts[-1].isdigit():
                alias_type = "_".join(parts[1:-1])
                dup_idx = int(parts[-1]) - 1  # "_2" -> index 1
            else:
                alias_type = "_".join(parts[1:])
                dup_idx = 0  # first occurrence

            abbrev = _resolve_abbrev(prefix, alias_type)
            group = def_groups.get((prefix, abbrev), [])
            if dup_idx < len(group):
                name_map[loc_name] = group[dup_idx]

    return name_map


def _set_pickup_gating_rules(
    multiworld, player: int, game: GameData, game_key: str
) -> None:
    """Set access rules on individual pickup locations that are behind locked doors.

    Uses requiredKeyItems from route analysis: each pickup may list key item
    names that must be obtained before the pickup is reachable.
    Location names must match the format in locations.py exactly.

    requiredKeyItems uses keyItem location names (e.g., "City of Vilcabamba -
    Silver Key") but items in the pool use itemDefinition names (e.g.,
    "Vilcabamba Silver Key"). We translate through a name map built from
    keyItem aliases and itemDefinition key patterns.
    """
    key_name_map = _build_key_item_name_map(game)

    for level in game.levels:
        level_name = level["name"]

        # Replicate the same type counter logic as locations.py
        type_counters: Dict[str, int] = {}

        for pickup in level.get("pickups", []):
            pickup_type = pickup.get("type", "Unknown")
            type_counters[pickup_type] = type_counters.get(pickup_type, 0) + 1
            count = type_counters[pickup_type]

            required_keys = pickup.get("requiredKeyItems", [])
            if not required_keys:
                continue

            # Translate location-style names to actual item names
            translated = [key_name_map.get(k, k) for k in required_keys]

            # Build the same location name as locations.py
            type_name = _PICKUP_TYPE_NAMES.get(
                pickup_type,
                pickup_type.replace("_S_P", "").replace("_P", ""),
            )
            loc_name = f"{level_name} - {type_name} {count}"

            _set_rule(
                multiworld, player, loc_name,
                lambda state, names=translated, p=player: all(
                    state.has(n, p) for n in names
                ),
            )


def _set_completion_rule(
    multiworld, player: int, level_name: str, required_items: List[str]
) -> None:
    """Set a rule requiring all listed items for level completion."""
    loc_name = f"{level_name} - Complete"
    _set_rule(
        multiworld, player, loc_name,
        lambda state, names=required_items, p=player: all(
            state.has(n, p) for n in names
        ),
    )


def _set_rule(multiworld, player: int, location_name: str, rule) -> None:
    """Set an access rule on a location, if it exists."""
    location = multiworld.get_location(location_name, player)
    if location is not None:
        location.access_rule = rule
