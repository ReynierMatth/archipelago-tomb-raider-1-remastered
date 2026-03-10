"""
Multi-game region definitions for TR Remastered Archipelago World.

Each game defines its own hub structure. Levels connect sequentially
within each hub region. All enabled games are accessible in parallel
from the Menu region.

TR1: Peru -> Greece -> Egypt -> Atlantis (linear)
TR2: Great Wall -> Italy -> Offshore -> Tibet -> China (linear)
TR3: India -> {South Pacific, London, Nevada} (choice) -> Antarctica
"""

from typing import TYPE_CHECKING, Callable, Dict, List, Optional, Set

from BaseClasses import Entrance, Location, Region

from .game_data import load_game
from .locations import TRLocationData, location_table

if TYPE_CHECKING:
    from . import TR1RWorld


# Per-game region hub ordering.
# Each entry: (hub_name, [level_display_names in order])
# Built dynamically from JSON region field + levelSequence order.

def _build_hub_structure(game_key: str) -> List[tuple]:
    """Build ordered list of (hub_name, [level_names]) from game data."""
    game = load_game(game_key)
    if game is None:
        return []

    hubs: Dict[str, List[str]] = {}
    hub_order: List[str] = []

    for level in game.levels:
        region = level["region"]
        if region not in hubs:
            hubs[region] = []
            hub_order.append(region)
        hubs[region].append(level["name"])

    return [(hub, hubs[hub]) for hub in hub_order]


def create_regions(world: "TR1RWorld", enabled_games: List[str]) -> None:
    """Create all regions for enabled games and connect them."""
    multiworld = world.multiworld
    player = world.player

    regions: Dict[str, Region] = {}

    # Menu region (always exists)
    menu = Region("Menu", player, multiworld)
    regions["Menu"] = menu
    multiworld.regions.append(menu)

    # Collect location data for enabled games only
    enabled_set = set(enabled_games)
    game_locations = {
        n: d for n, d in location_table.items() if d.game in enabled_set
    }

    # Build regions per game, all accessible in parallel from Menu
    for game_key in enabled_games:
        hub_structure = _build_hub_structure(game_key)
        if not hub_structure:
            continue

        first_hub_name = None

        for hub_name, level_names in hub_structure:
            # Create hub region
            hub_region_name = f"{game_key.upper()} - {hub_name} Hub"
            if hub_region_name not in regions:
                hub_region = Region(hub_region_name, player, multiworld)
                regions[hub_region_name] = hub_region
                multiworld.regions.append(hub_region)

            if first_hub_name is None:
                first_hub_name = hub_region_name

            # Create level regions and place locations
            prev_level: Optional[str] = None
            for level_name in level_names:
                if level_name not in regions:
                    level_region = Region(level_name, player, multiworld)
                    regions[level_name] = level_region
                    multiworld.regions.append(level_region)

                    # Place locations in level region
                    for loc_name, loc_data in game_locations.items():
                        if loc_data.level == level_name:
                            location = Location(
                                player, loc_name, loc_data.ap_id,
                                regions[level_name],
                            )
                            regions[level_name].locations.append(location)

                # Connect within hub: sequential level progression
                if prev_level is None:
                    # First level in hub: connect from hub
                    _connect(regions, hub_region_name, level_name)
                else:
                    # Subsequent levels: require previous level completion
                    _prev = prev_level  # capture for lambda
                    _connect(
                        regions, prev_level, level_name,
                        lambda state, p=player, lv=_prev: state.has(
                            f"Level Complete - {lv}", p
                        ),
                    )
                prev_level = level_name

        # Connect hubs within this game
        _connect_game_hubs(regions, game_key, hub_structure, player)

        # Connect first hub of this game directly from Menu (parallel access)
        if first_hub_name is not None:
            _connect(regions, "Menu", first_hub_name)


def _connect_game_hubs(
    regions: Dict[str, Region],
    game_key: str,
    hub_structure: List[tuple],
    player: int,
) -> None:
    """Connect hub regions within a game. TR3 has special branching."""
    if game_key == "tr3":
        _connect_tr3_hubs(regions, hub_structure, player)
    else:
        # Linear: each hub's last level connects to next hub
        for i in range(len(hub_structure) - 1):
            current_levels = hub_structure[i][1]
            next_hub_name = f"{game_key.upper()} - {hub_structure[i + 1][0]} Hub"
            if current_levels:
                last_level = current_levels[-1]
                _connect(
                    regions, last_level, next_hub_name,
                    lambda state, p=player, lv=last_level: state.has(
                        f"Level Complete - {lv}", p
                    ),
                )


def _connect_tr3_hubs(
    regions: Dict[str, Region],
    hub_structure: List[tuple],
    player: int,
) -> None:
    """TR3 special branching: after India, choose South Pacific/London/Nevada
    in any order. Antarctica requires all three completed."""
    hub_map = {name: levels for name, levels in hub_structure}

    # India (first hub) -> three middle hubs (accessible in any order)
    india_levels = hub_map.get("India", [])
    middle_hubs = ["South Pacific", "London", "Nevada"]

    if india_levels:
        last_india = india_levels[-1]
        for hub_name in middle_hubs:
            hub_region_name = f"TR3 - {hub_name} Hub"
            if hub_region_name in regions:
                _connect(
                    regions, last_india, hub_region_name,
                    lambda state, p=player, lv=last_india: state.has(
                        f"Level Complete - {lv}", p
                    ),
                )

    # Antarctica requires all three middle hubs' last levels completed
    antarctica_hub = "TR3 - Antarctica Hub"
    if antarctica_hub in regions:
        last_levels = []
        for hub_name in middle_hubs:
            hub_levels = hub_map.get(hub_name, [])
            if hub_levels:
                last_levels.append(hub_levels[-1])

        if last_levels:
            # Find a source region to connect from — use any middle hub's last level
            # We need to connect from each middle hub's last level to Antarctica
            for src_level in last_levels:
                _connect(
                    regions, src_level, antarctica_hub,
                    lambda state, p=player, lvs=last_levels: all(
                        state.has(f"Level Complete - {lv}", p) for lv in lvs
                    ),
                )

    # Connect remaining sequential hubs (India -> middle is handled above)
    # Middle hubs internal connections are already handled by the level-sequential logic


def _connect(
    regions: Dict[str, Region],
    source: str,
    target: str,
    rule: Optional[Callable] = None,
) -> Optional[Entrance]:
    """Helper to connect two regions with an optional access rule."""
    if source not in regions or target not in regions:
        return None
    entrance = regions[source].connect(regions[target])
    if rule is not None:
        entrance.access_rule = rule
    return entrance
