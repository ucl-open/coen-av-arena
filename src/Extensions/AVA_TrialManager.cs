// AVA_TrialManager.cs
// Loads trial patterns from a CSV, expands them according to per-type ratios and blocks,
// pseudo-randomizes with a max-consecutive constraint, and emits one pattern per trigger.
// Replaces AVA_RandomPattern in the workflow.
//
// Output: byte[] of 0s and 1s, same format AVA_BitmaskMessage expects.
//
// Workflow wiring:
//   trigger → AVA_TrialManager → AVA_BitmaskMessage (Staged: true) → SerialWrite
//
// @hamid

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reactive.Linq;
using Bonsai;

// Simple container for one trial definition
public class AVA_TrialDef
{
    private string _trialType;
    private string _rewardTile;
    private byte[] _pattern;

    public string TrialType
    {
        get { return _trialType; }
        set { _trialType = value; }
    }

    public string RewardTile
    {
        get { return _rewardTile; }
        set { _rewardTile = value; }
    }

    public byte[] Pattern
    {
        get { return _pattern; }
        set { _pattern = value; }
    }

    public AVA_TrialDef(string trialType, string rewardTile, byte[] pattern)
    {
        _trialType = trialType;
        _rewardTile = rewardTile;
        _pattern = pattern;
    }

    public AVA_TrialDef Clone()
    {
        byte[] copy = new byte[_pattern.Length];
        Array.Copy(_pattern, copy, _pattern.Length);
        return new AVA_TrialDef(_trialType, _rewardTile, copy);
    }
}

[Description("Loads trial patterns from CSV, pseudo-randomizes, and serves one pattern per trigger input.")]
public class AVA_TrialManager : Transform<object, byte[]>
{
    private string _csvPath;
    private string _generatedCsvPath;
    private string _trialsPerType;
    private int _numBlocks;
    private int _maxConsecutive;
    private int _numLeds;

    [Description("Path to the trials CSV file (relative to Bonsai working directory).")]
    public string CsvPath
    {
        get { return _csvPath; }
        set { _csvPath = value; }
    }

    [Description("Path to write the generated trial sequence CSV on startup. Leave empty to skip.")]
    public string GeneratedCsvPath
    {
        get { return _generatedCsvPath; }
        set { _generatedCsvPath = value; }
    }

    [Description("Trials per type per block, comma-separated matching CSV row order. E.g. '10,10'.")]
    public string TrialsPerType
    {
        get { return _trialsPerType; }
        set { _trialsPerType = value; }
    }

    [Description("Number of blocks in the session.")]
    public int NumBlocks
    {
        get { return _numBlocks; }
        set { _numBlocks = value; }
    }

    [Description("Maximum consecutive trials of the same type allowed.")]
    public int MaxConsecutive
    {
        get { return _maxConsecutive; }
        set { _maxConsecutive = value; }
    }

    [Description("Total number of LEDs in the arena (must match CSV columns).")]
    public int NumLeds
    {
        get { return _numLeds; }
        set { _numLeds = value; }
    }

    public AVA_TrialManager()
    {
        _csvPath = "trials.csv";
        _generatedCsvPath = "generated_trials.csv";
        _trialsPerType = "10,10";
        _numBlocks = 5;
        _maxConsecutive = 4;
        _numLeds = 400;
    }

    public override IObservable<byte[]> Process(IObservable<object> source)
    {
        return Observable.Defer(() =>
        {
            // ── Load trial definitions from CSV ──
            List<AVA_TrialDef> trialDefs = LoadCsv(_csvPath, _numLeds);

            if (trialDefs.Count == 0)
            {
                Console.WriteLine("TrialManager: ERROR - No trial types loaded from CSV");
                return source.Select(_ => new byte[0]);
            }

            // ── Parse per-type ratios ──
            int[] ratios = ParseRatios(_trialsPerType);

            if (ratios.Length != trialDefs.Count)
            {
                Console.WriteLine(String.Format(
                    "TrialManager: ERROR - TrialsPerType has {0} values but CSV has {1} trial types",
                    ratios.Length, trialDefs.Count));
                return source.Select(_ => new byte[0]);
            }

            // ── Build the full expanded sequence ──
            List<AVA_TrialDef> sequence = BuildSequence(trialDefs, ratios, _numBlocks);

            // ── Pseudo-randomize ──
            sequence = PseudoRandomize(sequence, _maxConsecutive);

            // ── Export generated sequence to CSV ──
            if (!String.IsNullOrEmpty(_generatedCsvPath))
            {
                ExportSequenceCsv(sequence, _generatedCsvPath, _numLeds);
            }

            // ── Initialize trial state ──
            AVA_TrialState.Reset(sequence.Count);

            Console.WriteLine(String.Format(
                "TrialManager: Ready - {0} trial types, {1} total trials, max {2} consecutive",
                trialDefs.Count, sequence.Count, _maxConsecutive));

            for (int t = 0; t < trialDefs.Count; t++)
            {
                int count = 0;
                for (int s = 0; s < sequence.Count; s++)
                {
                    if (sequence[s].TrialType == trialDefs[t].TrialType) count++;
                }
                Console.WriteLine(String.Format(
                    "  {0}: {1} trials", trialDefs[t].TrialType, count));
            }

            // ── Serve one trial per trigger ──
            int index = 0;
            bool complete = false;

            return source.SelectMany(_ =>
            {
                if (index >= sequence.Count)
                {
                    if (!complete)
                    {
                        AVA_TrialState.Phase = AVA_TrialPhase.Complete;
                        Console.WriteLine("TrialManager: All trials complete");
                        complete = true;
                    }
                    return Observable.Empty<byte[]>();
                }

                AVA_TrialDef trial = sequence[index];
                AVA_TrialState.SetTrial(index + 1, trial.TrialType, trial.RewardTile);
                AVA_TrialState.Phase = AVA_TrialPhase.Staging;

                Console.WriteLine(String.Format(
                    "TrialManager: Trial {0}/{1} - {2} (reward: {3})",
                    index + 1, sequence.Count, trial.TrialType, trial.RewardTile));

                index++;
                return Observable.Return(trial.Pattern);
            });
        });
    }


    // ── CSV Loading ──

    private static List<AVA_TrialDef> LoadCsv(string path, int numLeds)
    {
        List<AVA_TrialDef> defs = new List<AVA_TrialDef>();

        if (!File.Exists(path))
        {
            Console.WriteLine(String.Format("TrialManager: ERROR - CSV not found: {0}", path));
            return defs;
        }

        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2)
        {
            Console.WriteLine("TrialManager: ERROR - CSV has no data rows");
            return defs;
        }

        // Parse header to find column indices
        string[] headers = SplitCsvLine(lines[0]);

        int colTrialType = -1;
        int colRewardTile = -1;
        int[] colLeds = new int[numLeds];
        for (int i = 0; i < numLeds; i++) colLeds[i] = -1;

        for (int c = 0; c < headers.Length; c++)
        {
            string h = headers[c].Trim();
            if (h == "trial_type") colTrialType = c;
            else if (h == "reward_tile") colRewardTile = c;
            else if (h.StartsWith("led_"))
            {
                int ledId;
                if (int.TryParse(h.Substring(4), out ledId))
                {
                    if (ledId >= 0 && ledId < numLeds) colLeds[ledId] = c;
                }
            }
        }

        if (colTrialType < 0)
        {
            Console.WriteLine("TrialManager: ERROR - CSV missing 'trial_type' column");
            return defs;
        }

        // Parse data rows
        for (int row = 1; row < lines.Length; row++)
        {
            if (String.IsNullOrEmpty(lines[row].Trim())) continue;

            string[] cols = SplitCsvLine(lines[row]);
            string trialType = cols[colTrialType].Trim();
            string rewardTile = colRewardTile >= 0 ? cols[colRewardTile].Trim() : "";

            byte[] pattern = new byte[numLeds];
            for (int i = 0; i < numLeds; i++)
            {
                if (colLeds[i] >= 0 && colLeds[i] < cols.Length)
                {
                    pattern[i] = cols[colLeds[i]].Trim() == "1" ? (byte)1 : (byte)0;
                }
            }

            defs.Add(new AVA_TrialDef(trialType, rewardTile, pattern));
        }

        return defs;
    }

    private static string[] SplitCsvLine(string line)
    {
        // Simple CSV split (no quoted commas needed for this data)
        return line.Split(',');
    }


    // ── Sequence Building ──

    private static List<AVA_TrialDef> BuildSequence(
        List<AVA_TrialDef> trialDefs, int[] ratios, int numBlocks)
    {
        List<AVA_TrialDef> sequence = new List<AVA_TrialDef>();

        for (int block = 0; block < numBlocks; block++)
        {
            for (int t = 0; t < trialDefs.Count; t++)
            {
                for (int r = 0; r < ratios[t]; r++)
                {
                    sequence.Add(trialDefs[t].Clone());
                }
            }
        }

        return sequence;
    }


    // ── Pseudo-Randomization ──

    private static List<AVA_TrialDef> PseudoRandomize(
        List<AVA_TrialDef> sequence, int maxConsecutive)
    {
        Random rng = new Random();
        int n = sequence.Count;

        // Fisher-Yates shuffle first
        for (int i = n - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            AVA_TrialDef temp = sequence[i];
            sequence[i] = sequence[j];
            sequence[j] = temp;
        }

        // Repair pass: fix consecutive violations by swapping
        int maxAttempts = n * 10;
        int attempts = 0;

        while (attempts < maxAttempts)
        {
            int violationAt = FindViolation(sequence, maxConsecutive);
            if (violationAt < 0) break; // No violations found

            // Find a swap candidate: a later position with a different trial type
            // that won't create a new violation at its current position
            bool swapped = false;
            int searchStart = violationAt + 1;

            // Build a shuffled list of candidate indices to avoid bias
            List<int> candidates = new List<int>();
            for (int c = searchStart; c < n; c++) candidates.Add(c);
            for (int c = 0; c < searchStart; c++) candidates.Add(c);

            for (int ci = 0; ci < candidates.Count; ci++)
            {
                int c = candidates[ci];
                if (c == violationAt) continue;
                if (sequence[c].TrialType == sequence[violationAt].TrialType) continue;

                // Try the swap
                AVA_TrialDef temp = sequence[violationAt];
                sequence[violationAt] = sequence[c];
                sequence[c] = temp;

                // Check if this swap fixed things without creating new violations nearby
                if (FindViolationNear(sequence, maxConsecutive, violationAt) < 0 &&
                    FindViolationNear(sequence, maxConsecutive, c) < 0)
                {
                    swapped = true;
                    break;
                }

                // Undo the swap
                temp = sequence[violationAt];
                sequence[violationAt] = sequence[c];
                sequence[c] = temp;
            }

            if (!swapped)
            {
                // If no clean swap found, force a random swap to make progress
                for (int c = 0; c < n; c++)
                {
                    if (c != violationAt &&
                        sequence[c].TrialType != sequence[violationAt].TrialType)
                    {
                        AVA_TrialDef temp = sequence[violationAt];
                        sequence[violationAt] = sequence[c];
                        sequence[c] = temp;
                        break;
                    }
                }
            }

            attempts++;
        }

        // Final check
        int remaining = FindViolation(sequence, maxConsecutive);
        if (remaining >= 0)
        {
            Console.WriteLine(String.Format(
                "TrialManager: WARNING - Could not fully resolve consecutive constraint " +
                "after {0} attempts. Some runs of >{1} may remain.",
                maxAttempts, maxConsecutive));
        }
        else
        {
            Console.WriteLine("TrialManager: Pseudo-randomization OK - no consecutive violations");
        }

        return sequence;
    }

    // Returns the index of the first element in a consecutive run that violates the constraint,
    // or -1 if no violation exists.
    private static int FindViolation(List<AVA_TrialDef> seq, int maxConsecutive)
    {
        int runLength = 1;
        for (int i = 1; i < seq.Count; i++)
        {
            if (seq[i].TrialType == seq[i - 1].TrialType)
            {
                runLength++;
                if (runLength > maxConsecutive)
                {
                    return i; // Return the last element of the violating run
                }
            }
            else
            {
                runLength = 1;
            }
        }
        return -1;
    }

    // Check for violations in a window around a specific index
    private static int FindViolationNear(List<AVA_TrialDef> seq, int maxConsecutive, int center)
    {
        int start = Math.Max(0, center - maxConsecutive);
        int end = Math.Min(seq.Count - 1, center + maxConsecutive);

        int runLength = 1;
        for (int i = start + 1; i <= end; i++)
        {
            if (seq[i].TrialType == seq[i - 1].TrialType)
            {
                runLength++;
                if (runLength > maxConsecutive) return i;
            }
            else
            {
                runLength = 1;
            }
        }
        return -1;
    }


    // ── Ratio Parsing ──

    private static int[] ParseRatios(string csv)
    {
        string[] parts = csv.Split(',');
        int[] values = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            values[i] = int.Parse(parts[i].Trim());
        }
        return values;
    }


    // ── Sequence Export ──

    private static void ExportSequenceCsv(
        List<AVA_TrialDef> sequence, string path, int numLeds)
    {
        try
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            // Header row
            sb.Append("trial_number,trial_type,reward_tile");
            for (int i = 0; i < numLeds; i++)
            {
                sb.Append(",led_");
                sb.Append(i);
            }
            sb.AppendLine();

            // Data rows
            for (int t = 0; t < sequence.Count; t++)
            {
                AVA_TrialDef trial = sequence[t];
                sb.Append(t + 1);
                sb.Append(",");
                sb.Append(trial.TrialType);
                sb.Append(",");
                sb.Append(trial.RewardTile);

                for (int i = 0; i < numLeds; i++)
                {
                    sb.Append(",");
                    if (i < trial.Pattern.Length)
                    {
                        sb.Append(trial.Pattern[i]);
                    }
                    else
                    {
                        sb.Append(0);
                    }
                }
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString());
            Console.WriteLine(String.Format(
                "TrialManager: Exported {0} trials to {1}", sequence.Count, path));
        }
        catch (Exception ex)
        {
            Console.WriteLine(String.Format(
                "TrialManager: WARNING - Could not export sequence CSV: {0}", ex.Message));
        }
    }
}
