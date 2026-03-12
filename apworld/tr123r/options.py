from dataclasses import dataclass

from Options import (
    Choice,
    DeathLink,
    DefaultOnToggle,
    PerGameCommonOptions,
    Range,
    Toggle,
)


class IncludeTR1(DefaultOnToggle):
    """Include Tomb Raider 1 levels in the multiworld."""
    display_name = "Include Tomb Raider 1"


class IncludeTR2(Toggle):
    """Include Tomb Raider 2 levels in the multiworld.
    Requires tr2r_data.json in the data folder."""
    display_name = "Include Tomb Raider 2"


class IncludeTR3(Toggle):
    """Include Tomb Raider 3 levels in the multiworld.
    Requires tr3r_data.json in the data folder."""
    display_name = "Include Tomb Raider 3"


class Goal(Choice):
    """
    Determines the win condition for this game.

    All Levels: Complete every level of all enabled games.
    N Levels: Complete a configurable number of levels.
    """
    display_name = "Goal"
    option_all_levels = 0
    option_n_levels = 1
    default = 0


class LevelsForGoal(Range):
    """
    If Goal is set to 'N Levels', how many levels must be completed to win.
    """
    display_name = "Levels Required for Goal"
    range_start = 1
    range_end = 60
    default = 15


class SecretsMode(Choice):
    """
    How secrets are handled in the randomization.

    Excluded: Secrets are not part of the multiworld.
    Useful: Secrets are 'useful' classification items.
    Progression: Secrets may be required for progression.
    """
    display_name = "Secrets Mode"
    option_excluded = 0
    option_useful = 1
    option_progression = 2
    default = 1


class TrapPercentage(Range):
    """
    Percentage of filler items that are replaced with traps.
    """
    display_name = "Trap Percentage"
    range_start = 0
    range_end = 50
    default = 10


class StartingWeapons(Choice):
    """
    Which weapons Lara starts with.

    Pistols: Standard pistols only.
    Random: A random weapon set.
    All: All weapons from the start.
    """
    display_name = "Starting Weapons"
    option_pistols = 0
    option_random = 1
    option_all = 2
    default = 0


@dataclass
class TR1ROptions(PerGameCommonOptions):
    include_tr1: IncludeTR1
    include_tr2: IncludeTR2
    include_tr3: IncludeTR3
    goal: Goal
    levels_for_goal: LevelsForGoal
    secrets_mode: SecretsMode
    trap_percentage: TrapPercentage
    starting_weapons: StartingWeapons
    death_link: DeathLink
