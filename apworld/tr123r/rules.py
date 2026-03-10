"""
Data-driven access rules for TR Remastered Archipelago World.

For each level, all its key items are required to complete the level.
Uses two strategies:
  1. If keyItems have "itemName" field (new exporter format): direct mapping
  2. Fallback: match key items to item definitions by level prefix in the JSON keys
"""

from typing import TYPE_CHECKING, Dict, List, Set

from .game_data import GameData, load_game

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

        # Check if new "itemName" field is available
        has_item_name = any(
            ki.get("itemName")
            for level in game.levels
            for ki in level.get("keyItems", [])
        )

        if has_item_name:
            _set_rules_direct(multiworld, player, game)
        else:
            _set_rules_prefix_match(multiworld, player, game)


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
