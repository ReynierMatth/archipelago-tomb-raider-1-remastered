from dataclasses import dataclass

from Options import (
    Choice,
    DeathLink,
    DefaultOnToggle,
    PerGameCommonOptions,
    Range,
    Toggle,
)


class Goal(Choice):
    """
    Determines the win condition for this game.

    Final Boss: Complete The Great Pyramid (final level).
    All Secrets: Collect all 45 secrets across the game.
    N Levels: Complete a configurable number of levels.
    """
    display_name = "Goal"
    option_final_boss = 0
    option_all_secrets = 1
    option_n_levels = 2
    default = 0


class LevelsForGoal(Range):
    """
    If Goal is set to 'N Levels', how many levels must be completed to win.
    """
    display_name = "Levels Required for Goal"
    range_start = 1
    range_end = 15
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
    goal: Goal
    levels_for_goal: LevelsForGoal
    secrets_mode: SecretsMode
    trap_percentage: TrapPercentage
    starting_weapons: StartingWeapons
    death_link: DeathLink
