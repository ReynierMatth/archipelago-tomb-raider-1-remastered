"""
Multi-game location definitions for TR Remastered Archipelago World.
All data loaded from game JSON files via game_data.py.

ID Schema (per game, must match client LocationMapper.cs):
  TR1: pickups 780000+, secrets 790000+, level_complete 795000+
  TR2: pickups 880000+, secrets 890000+, level_complete 895000+
  TR3: pickups 980000+, secrets 990000+, level_complete 995000+
"""

from typing import Dict, List, NamedTuple, Optional

from .game_data import GameData, get_available_games, load_game


class TRLocationData(NamedTuple):
    ap_id: Optional[int]
    region: str
    level: str
    category: str  # "pickup", "key_item", "secret", "level_complete"
    game: str  # "tr1", "tr2", "tr3"


# Friendly names for pickup types (shared across games)
_PICKUP_TYPE_NAMES = {
    # TR1
    "SmallMed_S_P": "Small Medipack",
    "LargeMed_S_P": "Large Medipack",
    "Shotgun_S_P": "Shotgun",
    "Magnums_S_P": "Magnums",
    "Uzis_S_P": "Uzis",
    "ShotgunAmmo_S_P": "Shotgun Shells",
    "MagnumAmmo_S_P": "Magnum Clips",
    "UziAmmo_S_P": "Uzi Clips",
    # TR2
    "Automags_S_P": "Automags",
    "Harpoon_S_P": "Harpoon Gun",
    "M16_S_P": "M16",
    "GrenadeLauncher_S_P": "Grenade Launcher",
    "AutoAmmo_S_P": "Auto Clips",
    "HarpoonAmmo_S_P": "Harpoons",
    "M16Ammo_S_P": "M16 Clips",
    "Grenades_S_P": "Grenades",
    "Flares_S_P": "Flares",
    "Uzi_S_P": "Uzis",
    # TR3
    "SmallMed_P": "Small Medipack",
    "LargeMed_P": "Large Medipack",
    "Shotgun_P": "Shotgun",
    "Deagle_P": "Desert Eagle",
    "Uzis_P": "Uzis",
    "Harpoon_P": "Harpoon Gun",
    "MP5_P": "MP5",
    "RocketLauncher_P": "Rocket Launcher",
    "GrenadeLauncher_P": "Grenade Launcher",
    "ShotgunAmmo_P": "Shotgun Shells",
    "DeagleAmmo_P": "Desert Eagle Clips",
    "UziAmmo_P": "Uzi Clips",
    "Harpoons_P": "Harpoons",
    "MP5Ammo_P": "MP5 Clips",
    "Rockets_P": "Rockets",
    "Grenades_P": "Grenades",
    "Flares_P": "Flares",
}


def _build_game_locations(game: GameData) -> Dict[str, TRLocationData]:
    """Build location definitions for one game."""
    locations: Dict[str, TRLocationData] = {}
    config = game.config

    for level_idx, level in enumerate(game.levels):
        level_name = level["name"]
        region = level["region"]

        # Standard pickup locations
        type_counters: Dict[str, int] = {}
        for pickup in level["pickups"]:
            entity_idx = pickup["entityIndex"]
            ap_id = config.location_base + level_idx * 1000 + entity_idx
            pickup_type = pickup["type"]

            type_counters[pickup_type] = type_counters.get(pickup_type, 0) + 1
            type_name = _PICKUP_TYPE_NAMES.get(
                pickup_type,
                pickup_type.replace("_S_P", "").replace("_P", ""),
            )
            count = type_counters[pickup_type]
            loc_name = f"{level_name} - {type_name} {count}"

            locations[loc_name] = TRLocationData(
                ap_id=ap_id, region=region, level=level_name,
                category="pickup", game=config.key,
            )

        # Key item locations
        for key_item in level["keyItems"]:
            entity_idx = key_item["entityIndex"]
            ap_id = config.location_base + level_idx * 1000 + entity_idx
            loc_name = key_item["name"]

            locations[loc_name] = TRLocationData(
                ap_id=ap_id, region=region, level=level_name,
                category="key_item", game=config.key,
            )

        # Secret locations
        for secret in level["secrets"]:
            secret_idx = secret["index"]
            ap_id = config.secret_base + level_idx * 10 + secret_idx
            loc_name = f"{level_name} - Secret {secret_idx + 1}"

            locations[loc_name] = TRLocationData(
                ap_id=ap_id, region=region, level=level_name,
                category="secret", game=config.key,
            )

        # Level completion event
        ap_id = config.level_complete_base + level_idx
        loc_name = f"{level_name} - Complete"
        locations[loc_name] = TRLocationData(
            ap_id=ap_id, region=region, level=level_name,
            category="level_complete", game=config.key,
        )

    return locations


def _build_all_locations() -> Dict[str, TRLocationData]:
    """Build locations from all available game data files."""
    all_locs: Dict[str, TRLocationData] = {}
    for game_key in get_available_games():
        game = load_game(game_key)
        if game is not None:
            all_locs.update(_build_game_locations(game))
    return all_locs


# Pre-built at module load
location_table: Dict[str, TRLocationData] = _build_all_locations()

# Reverse lookup
location_id_to_name: Dict[int, str] = {
    d.ap_id: n for n, d in location_table.items() if d.ap_id is not None
}


def get_locations_for_games(game_keys: List[str]) -> Dict[str, TRLocationData]:
    """Return locations for a set of enabled games."""
    keys = set(game_keys)
    return {n: d for n, d in location_table.items() if d.game in keys}


def get_levels_for_game(game_key: str) -> List[dict]:
    """Return the level list for a game."""
    game = load_game(game_key)
    if game is None:
        return []
    return game.levels


def get_secrets_per_level(game_key: str) -> List[int]:
    """Return secret counts per level for a game."""
    game = load_game(game_key)
    if game is None:
        return []
    return [len(level["secrets"]) for level in game.levels]
