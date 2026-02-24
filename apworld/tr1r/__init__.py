"""
Archipelago World definition for Tomb Raider 1 Remastered.
"""

from typing import Any, Dict, List, Set

from BaseClasses import ItemClassification, MultiWorld, Tutorial
from worlds.AutoWorld import WebWorld, World

from .items import (
    AMMO,
    EVENTS,
    KEY_ITEMS,
    MEDIPACKS,
    TRAPS,
    TR1RItemData,
    WEAPONS,
    get_all_items,
)
from .locations import LEVELS, SECRETS_PER_LEVEL, location_id_to_name, location_table
from .options import TR1ROptions
from .regions import create_regions
from .rules import set_rules


class TR1RWeb(WebWorld):
    theme = "dirt"
    tutorials = [
        Tutorial(
            "Tomb Raider 1 Remastered Multiworld Setup Guide",
            "A guide to setting up TR1 Remastered for Archipelago multiworld.",
            "English",
            "setup_en.md",
            "setup/en",
            ["TRRando Community"],
        )
    ]


class TR1RWorld(World):
    """
    Tomb Raider 1 Remastered (2024) is an action-adventure game where Lara Croft
    explores ancient ruins across Peru, Greece, Egypt, and Atlantis.

    In this Archipelago integration, pickups and key items are shuffled into the
    multiworld pool. Key items from other players' worlds may appear in your
    Tomb Raider levels, and your keys/artifacts may end up in their games.
    """

    game = "Tomb Raider 1 Remastered"
    web = TR1RWeb()
    options_dataclass = TR1ROptions
    options: TR1ROptions

    # Item/location name <-> ID mappings
    item_name_to_id: Dict[str, int] = {
        name: data.ap_id for name, data in get_all_items().items()
        if data.ap_id is not None
    }
    location_name_to_id: Dict[str, int] = {
        name: data.ap_id for name, data in location_table.items()
        if data.ap_id is not None
    }

    def create_regions(self) -> None:
        create_regions(self)

    def create_items(self) -> None:
        item_pool: List[str] = []
        all_items = get_all_items()
        locations_count = len([
            loc for loc in location_table.values()
            if loc.category != "level_complete"
        ])

        # Add all key items (always in pool)
        for name, data in KEY_ITEMS.items():
            item_pool.append(name)

        # Add weapons
        for name in WEAPONS:
            item_pool.append(name)

        # Calculate filler needed
        filler_needed = locations_count - len(item_pool)

        # Calculate traps
        trap_pct = self.options.trap_percentage.value
        trap_count = int(filler_needed * trap_pct / 100) if trap_pct > 0 else 0
        filler_count = filler_needed - trap_count

        # Add filler items (ammo + medipacks)
        filler_items = list(AMMO.keys()) + list(MEDIPACKS.keys())
        for i in range(filler_count):
            item_pool.append(filler_items[i % len(filler_items)])

        # Add traps
        trap_names = list(TRAPS.keys())
        for i in range(trap_count):
            item_pool.append(trap_names[i % len(trap_names)])

        # Create the actual item objects
        for item_name in item_pool:
            if item_name in all_items:
                data = all_items[item_name]
                self.multiworld.itempool.append(
                    self.create_item(item_name)
                )

        # Create level completion events (not in item pool)
        for loc_name, loc_data in location_table.items():
            if loc_data.category == "level_complete":
                event_item_name = f"Level Complete - {loc_data.level}"
                if event_item_name in EVENTS:
                    event_location = self.multiworld.get_location(loc_name, self.player)
                    event_location.place_locked_item(
                        self.create_event(event_item_name)
                    )

    def create_item(self, name: str):
        all_items = get_all_items()
        data = all_items[name]
        from BaseClasses import Item
        return Item(name, data.classification, data.ap_id, self.player)

    def create_event(self, name: str):
        from BaseClasses import Item
        data = EVENTS[name]
        return Item(name, data.classification, data.ap_id, self.player)

    def set_rules(self) -> None:
        set_rules(self)

    def get_filler_item_name(self) -> str:
        return "Small Medipack"

    def fill_slot_data(self) -> Dict[str, Any]:
        """Data sent to the client for this player's slot."""
        return {
            "goal": self.options.goal.value,
            "levels_for_goal": self.options.levels_for_goal.value,
            "secrets_mode": self.options.secrets_mode.value,
            "death_link": self.options.death_link.value,
            "starting_weapons": self.options.starting_weapons.value,
            "level_sequence": [level[1] for level in LEVELS],
        }

    def set_completion_rules(self) -> None:
        """Set the completion condition based on the selected goal."""
        from worlds.generic.Rules import set_rule

        goal = self.options.goal.value
        player = self.player

        if goal == 0:  # final_boss
            self.multiworld.completion_condition[player] = \
                lambda state: state.has("Level Complete - The Great Pyramid", player)
        elif goal == 1:  # all_secrets
            self.multiworld.completion_condition[player] = \
                lambda state: all(
                    state.can_reach(f"{level[0]} - Secret {s + 1}", "Location", player)
                    for i, level in enumerate(LEVELS)
                    for s in range(SECRETS_PER_LEVEL[i])
                )
        elif goal == 2:  # n_levels
            required = self.options.levels_for_goal.value
            self.multiworld.completion_condition[player] = \
                lambda state, req=required: sum(
                    1 for level in LEVELS
                    if state.has(f"Level Complete - {level[0]}", player)
                ) >= req

    def generate_basic(self) -> None:
        self.set_completion_rules()
