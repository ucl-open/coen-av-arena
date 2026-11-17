"""
AVA Trial Generator
===================
Reads an AVA task JSON config file and generates a CSV of trial definitions.
Each row in the CSV is one trial type, with a column per LED (0 or 1)
indicating which LEDs are on for that trial.

The CSV defines WHAT each trial looks like.
Bonsai handles HOW MANY times each trial runs and in WHAT ORDER (pseudo-randomization).

Usage:
    python generate_trials.py --config ava_config.json --output trials.csv

Author: @hamid
"""

import argparse
import json
import csv
import sys
from pathlib import Path


def load_config(config_path: str) -> dict:
    """Load and validate the AvaTask JSON config."""
    path = Path(config_path)
    if not path.exists():
        print(f"Error: Config file not found: {config_path}")
        sys.exit(1)

    with open(path, "r") as f:
        config = json.load(f)

    # Validate required sections
    required = ["ArenaLayout", "TrialTypes"]
    for section in required:
        if section not in config:
            print(f"Error: Missing required section '{section}' in config")
            sys.exit(1)

    return config


def build_tile_to_leds(arena_layout: dict) -> dict:
    """
    Build a lookup from tile ID to a list of LED indices.

    Args:
        arena_layout: The ArenaLayout section of the config.

    Returns:
        dict mapping tile name (str) to list of LED indices (list[int]).
    """
    tile_map = arena_layout["tileMap"]
    result = {}

    for tile_name, tile_info in tile_map.items():
        led_start = tile_info["ledStart"]
        led_end = tile_info["ledEnd"]
        result[tile_name] = list(range(led_start, led_end + 1))

    return result


def generate_trial_pattern(
    trial_type_name: str,
    trial_type_def: dict,
    tile_to_leds: dict,
    num_leds: int,
) -> dict:
    """
    Generate a single trial pattern as a dict with metadata and LED states.

    Args:
        trial_type_name: Name of this trial type.
        trial_type_def: Trial type definition from config.
        tile_to_leds: Mapping from tile names to LED indices.
        num_leds: Total number of LEDs in the arena.

    Returns:
        dict with 'trial_type', 'reward_tile', 'tiles', and 'led_0'...'led_N' keys.
    """
    # Start with all LEDs off
    led_states = [0] * num_leds

    # Turn on LEDs for each tile in this trial type
    tiles = trial_type_def["tiles"]
    for tile_name in tiles:
        if tile_name not in tile_to_leds:
            print(f"Warning: Tile '{tile_name}' in trial type "
                  f"'{trial_type_name}' not found in tileMap. Skipping.")
            continue

        for led_id in tile_to_leds[tile_name]:
            if led_id < num_leds:
                led_states[led_id] = 1

    # Build the row
    row = {
        "trial_type": trial_type_name,
        "reward_tile": trial_type_def.get("rewardTile", ""),
        "tiles": ";".join(tiles),
    }

    # Add LED columns
    for i in range(num_leds):
        row[f"led_{i}"] = led_states[i]

    return row


def generate_trials(config: dict) -> list:
    """
    Generate all trial patterns from the config.

    Args:
        config: The full AvaTask config dict.

    Returns:
        List of dicts, one per trial type, each with metadata and LED states.
    """
    arena_layout = config["ArenaLayout"]
    trial_types = config["TrialTypes"]
    num_leds = arena_layout["numLeds"]

    tile_to_leds = build_tile_to_leds(arena_layout)

    trials = []
    for trial_type_name, trial_type_def in trial_types.items():
        pattern = generate_trial_pattern(
            trial_type_name, trial_type_def, tile_to_leds, num_leds
        )
        trials.append(pattern)

    return trials


def write_csv(trials: list, output_path: str, num_leds: int):
    """
    Write trial patterns to a CSV file.

    Args:
        trials: List of trial pattern dicts.
        output_path: Path for the output CSV.
        num_leds: Total number of LEDs (determines number of led columns).
    """
    if not trials:
        print("Error: No trials generated.")
        sys.exit(1)

    # Build column headers
    fieldnames = ["trial_type", "reward_tile", "tiles"]
    fieldnames += [f"led_{i}" for i in range(num_leds)]

    path = Path(output_path)
    path.parent.mkdir(parents=True, exist_ok=True)

    with open(path, "w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(trials)

    print(f"Generated {len(trials)} trial type(s) -> {output_path}")


def print_summary(config: dict, trials: list):
    """Print a summary of the generated trials."""
    arena = config["ArenaLayout"]
    structure = config.get("TrialStructure", {})
    ratios = structure.get("trialTypeRatios", {})
    num_blocks = structure.get("numBlocks", 1)

    print("")
    print("=" * 60)
    print("AVA Trial Generation Summary")
    print("=" * 60)
    print(f"Arena: {arena['numLeds']} LEDs across "
          f"{arena['numTiles']} tiles "
          f"({arena['gridColumns']}x{arena['gridRows']} grid)")
    print(f"LEDs per tile: {arena['ledsPerTile']}")
    print("")

    print("Trial types:")
    total_per_block = 0
    for trial in trials:
        trial_type = trial["trial_type"]
        tiles = trial["tiles"]
        num_on = sum(1 for k, v in trial.items()
                     if k.startswith("led_") and v == 1)
        ratio = ratios.get(trial_type, "?")
        print(f"  {trial_type}")
        print(f"    Tiles: {tiles}")
        print(f"    LEDs on: {num_on}")
        print(f"    Reward tile: {trial['reward_tile']}")
        print(f"    Trials per block: {ratio}")
        if isinstance(ratio, int):
            total_per_block += ratio

    print("")
    print(f"Blocks: {num_blocks}")
    print(f"Trials per block: {total_per_block}")
    print(f"Total trials per session: {total_per_block * num_blocks}")

    pseudo = structure.get("pseudoRandom", {})
    max_consec = pseudo.get("maxConsecutiveSameType", "not set")
    print(f"Max consecutive same type: {max_consec}")

    iti = structure.get("iti", [0, 0])
    print(f"ITI: {iti[0]}s base + 0-{iti[1]}s jitter")
    print("=" * 60)
    print("")


def main():
    parser = argparse.ArgumentParser(
        description="AVA Trial Generator: creates trial CSV from JSON config"
    )
    parser.add_argument(
        "--config",
        type=str,
        required=True,
        help="Path to the AvaTask JSON config file",
    )
    parser.add_argument(
        "--output",
        type=str,
        default="trials.csv",
        help="Path for the output CSV file (default: trials.csv)",
    )
    parser.add_argument(
        "--quiet",
        action="store_true",
        help="Suppress summary output",
    )

    args = parser.parse_args()

    # Load config
    config = load_config(args.config)

    # Generate trial patterns
    trials = generate_trials(config)

    # Write CSV
    num_leds = config["ArenaLayout"]["numLeds"]
    write_csv(trials, args.output, num_leds)

    # Print summary
    if not args.quiet:
        print_summary(config, trials)


if __name__ == "__main__":
    main()
