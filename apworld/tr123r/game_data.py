"""
Multi-game data loader. Loads tr1r_data.json, tr2r_data.json, tr3r_data.json
based on which games are enabled. All downstream modules (items, locations,
regions, rules) consume this unified data structure.

Each game has its own ID ranges to avoid collisions:
  TR1: items 770000+, locations 780000+, secrets 790000+, level_complete 795000+
  TR2: items 870000+, locations 880000+, secrets 890000+, level_complete 895000+
  TR3: items 970000+, locations 980000+, secrets 990000+, level_complete 995000+
"""

import json
import pkgutil
from dataclasses import dataclass, field
from typing import Dict, List, Optional


@dataclass
class GameConfig:
    """Per-game configuration and ID ranges."""
    key: str              # "tr1", "tr2", "tr3"
    display_name: str     # "Tomb Raider 1 Remastered"
    data_file: str        # "data/tr1r_data.json"
    item_base: int        # 770000
    location_base: int    # 780000
    secret_base: int      # 790000
    level_complete_base: int  # 795000
    trap_base: int        # 769000


GAME_CONFIGS: Dict[str, GameConfig] = {
    "tr1": GameConfig(
        key="tr1",
        display_name="Tomb Raider 1",
        data_file="data/tr1r_data.json",
        item_base=770_000,
        location_base=780_000,
        secret_base=790_000,
        level_complete_base=795_000,
        trap_base=769_000,
    ),
    "tr2": GameConfig(
        key="tr2",
        display_name="Tomb Raider 2",
        data_file="data/tr2r_data.json",
        item_base=870_000,
        location_base=880_000,
        secret_base=890_000,
        level_complete_base=895_000,
        trap_base=869_000,
    ),
    "tr3": GameConfig(
        key="tr3",
        display_name="Tomb Raider 3",
        data_file="data/tr3r_data.json",
        item_base=970_000,
        location_base=980_000,
        secret_base=990_000,
        level_complete_base=995_000,
        trap_base=969_000,
    ),
}


@dataclass
class GameData:
    """Loaded data for one game."""
    config: GameConfig
    raw: dict
    levels: list = field(default_factory=list)


_loaded_games: Dict[str, GameData] = {}


def load_game(key: str) -> Optional[GameData]:
    """Load a game's data JSON. Returns None if file doesn't exist."""
    if key in _loaded_games:
        return _loaded_games[key]

    config = GAME_CONFIGS.get(key)
    if config is None:
        return None

    try:
        raw = pkgutil.get_data(__package__, config.data_file)
        if raw is None:
            return None
        data = json.loads(raw.decode("utf-8"))
        game_data = GameData(config=config, raw=data, levels=data.get("levels", []))
        _loaded_games[key] = game_data
        return game_data
    except (FileNotFoundError, json.JSONDecodeError):
        return None


def get_available_games() -> List[str]:
    """Return keys of games whose data files exist."""
    available = []
    for key in GAME_CONFIGS:
        try:
            raw = pkgutil.get_data(__package__, GAME_CONFIGS[key].data_file)
            if raw is not None:
                available.append(key)
        except FileNotFoundError:
            pass
    return available
