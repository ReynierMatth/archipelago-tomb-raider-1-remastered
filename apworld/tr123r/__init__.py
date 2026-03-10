"""
Archipelago World definition for Tomb Raider Remastered (TR1/TR2/TR3).
"""

from typing import Any, Dict, List, Set

from BaseClasses import Item, ItemClassification, MultiWorld, Tutorial
from worlds.AutoWorld import WebWorld, World

from .game_data import get_available_games, load_game
from .items import (
    ALL_ITEMS,
    TRItemData,
    get_items_by_category,
    get_items_for_games,
)
from .locations import (
    get_levels_for_game,
    get_locations_for_games,
    get_secrets_per_level,
    location_table,
)
from .options import TR1ROptions
from .regions import create_regions
from .rules import set_rules


class TR1RWeb(WebWorld):
    theme = "dirt"
    tutorials = [
        Tutorial(
            "Tomb Raider Remastered Multiworld Setup Guide",
            "A guide to setting up TR Remastered for Archipelago multiworld.",
            "English",
            "setup_en.md",
            "setup/en",
            ["TRRando Community"],
        )
    ]


class TR1RWorld(World):
    """
    Tomb Raider I-III Remastered (2024) is an action-adventure game where Lara Croft
    explores ancient ruins across multiple continents.

    In this Archipelago integration, pickups and key items are shuffled into the
    multiworld pool. Key items from other players' worlds may appear in your
    Tomb Raider levels, and your keys/artifacts may end up in their games.
    """

    game = "Tomb Raider Remastered"
    web = TR1RWeb()
    options_dataclass = TR1ROptions
    options: TR1ROptions

    # Item/location name <-> ID mappings (all games, class-level)
    item_name_to_id: Dict[str, int] = {
        name: data.ap_id for name, data in ALL_ITEMS.items()
        if data.ap_id is not None
    }
    location_name_to_id: Dict[str, int] = {
        name: data.ap_id for name, data in location_table.items()
        if data.ap_id is not None
    }

    def _get_enabled_games(self) -> List[str]:
        """Return list of enabled game keys based on options and available data."""
        available = set(get_available_games())
        enabled = []
        if self.options.include_tr1.value and "tr1" in available:
            enabled.append("tr1")
        if self.options.include_tr2.value and "tr2" in available:
            enabled.append("tr2")
        if self.options.include_tr3.value and "tr3" in available:
            enabled.append("tr3")
        # Fallback: at least TR1 if nothing enabled
        if not enabled and "tr1" in available:
            enabled.append("tr1")
        return enabled

    def create_regions(self) -> None:
        create_regions(self, self._get_enabled_games())

    def create_items(self) -> None:
        enabled = self._get_enabled_games()
        game_items = get_items_for_games(enabled)
        game_locations = get_locations_for_games(enabled)

        item_pool: List[str] = []

        locations_count = len([
            loc for loc in game_locations.values()
            if loc.category != "level_complete"
        ])

        # Add all key items (always in pool)
        key_items = get_items_by_category(game_items, "key_item")
        for name in key_items:
            item_pool.append(name)

        # Add weapons
        weapons = get_items_by_category(game_items, "weapon")
        for name in weapons:
            item_pool.append(name)

        # Calculate filler needed
        filler_needed = locations_count - len(item_pool)

        # Calculate traps
        trap_pct = self.options.trap_percentage.value
        trap_count = int(filler_needed * trap_pct / 100) if trap_pct > 0 else 0
        filler_count = filler_needed - trap_count

        # Filler items (ammo + medipacks from enabled games)
        ammo = get_items_by_category(game_items, "ammo")
        small_med = get_items_by_category(game_items, "small_medipack")
        large_med = get_items_by_category(game_items, "large_medipack")
        filler_items = list(ammo.keys()) + list(small_med.keys()) + list(large_med.keys())
        if filler_items:
            for i in range(filler_count):
                item_pool.append(filler_items[i % len(filler_items)])

        # Traps from enabled games
        traps = get_items_by_category(game_items, "trap")
        trap_names = list(traps.keys())
        if trap_names:
            for i in range(trap_count):
                item_pool.append(trap_names[i % len(trap_names)])

        # Create actual item objects
        for item_name in item_pool:
            if item_name in game_items:
                self.multiworld.itempool.append(self.create_item(item_name))

        # Create level completion events (not in item pool)
        for loc_name, loc_data in game_locations.items():
            if loc_data.category == "level_complete":
                event_item_name = f"Level Complete - {loc_data.level}"
                if event_item_name in game_items:
                    event_location = self.multiworld.get_location(loc_name, self.player)
                    event_location.place_locked_item(
                        self.create_event(event_item_name)
                    )

    def create_item(self, name: str):
        data = ALL_ITEMS[name]
        return Item(name, data.classification, data.ap_id, self.player)

    def create_event(self, name: str):
        data = ALL_ITEMS[name]
        return Item(name, data.classification, data.ap_id, self.player)

    def set_rules(self) -> None:
        set_rules(self, self._get_enabled_games())

    def get_filler_item_name(self) -> str:
        enabled = self._get_enabled_games()
        # Return first available game's small medipack
        first_game = enabled[0] if enabled else "tr1"
        return f"{first_game.upper()} - Small Medipack"

    def fill_slot_data(self) -> Dict[str, Any]:
        """Data sent to the client for this player's slot."""
        enabled = self._get_enabled_games()

        # Build per-game level sequences and secret counts
        all_levels = []
        all_secrets = []
        level_sequences = {}
        for game_key in enabled:
            game = load_game(game_key)
            if game is None:
                continue
            levels = game.levels
            level_sequences[game_key] = [level["file"] for level in levels]
            all_levels.extend([(level["name"], level["file"]) for level in levels])
            all_secrets.extend(len(level["secrets"]) for level in levels)

        return {
            "goal": self.options.goal.value,
            "levels_for_goal": self.options.levels_for_goal.value,
            "secrets_mode": self.options.secrets_mode.value,
            "death_link": self.options.death_link.value,
            "starting_weapons": self.options.starting_weapons.value,
            "enabled_games": enabled,
            "total_secrets": sum(all_secrets),
            "level_sequences": level_sequences,
        }

    def set_completion_rules(self) -> None:
        """Set the completion condition based on the selected goal."""
        goal = self.options.goal.value
        player = self.player
        enabled = self._get_enabled_games()

        if goal == 0:  # final_boss - complete last level of last enabled game
            last_game = enabled[-1] if enabled else "tr1"
            game = load_game(last_game)
            if game and game.levels:
                last_level_name = game.levels[-1]["name"]
                self.multiworld.completion_condition[player] = \
                    lambda state, lv=last_level_name: state.has(
                        f"Level Complete - {lv}", player
                    )

        elif goal == 1:  # all_secrets
            all_secret_locs = []
            for game_key in enabled:
                game = load_game(game_key)
                if game is None:
                    continue
                for i, level in enumerate(game.levels):
                    for s in range(len(level["secrets"])):
                        all_secret_locs.append(f"{level['name']} - Secret {s + 1}")

            self.multiworld.completion_condition[player] = \
                lambda state, locs=all_secret_locs: all(
                    state.can_reach(loc, "Location", player) for loc in locs
                )

        elif goal == 2:  # n_levels
            required = self.options.levels_for_goal.value
            all_level_names = []
            for game_key in enabled:
                game = load_game(game_key)
                if game is None:
                    continue
                all_level_names.extend(level["name"] for level in game.levels)

            self.multiworld.completion_condition[player] = \
                lambda state, req=required, lvs=all_level_names: sum(
                    1 for lv in lvs if state.has(f"Level Complete - {lv}", player)
                ) >= req

    def generate_basic(self) -> None:
        self.set_completion_rules()
