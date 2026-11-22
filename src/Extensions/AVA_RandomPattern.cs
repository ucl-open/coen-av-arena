// Extensions/AVA_RandomPattern.cs
using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

public enum AVA_RandomPatternMode
{
    Bitmask,
    FullFrame
}

public enum AVA_FullFrameColorMode
{
    Random,
    BitmaskColor
}

[Description("Generates a random LED pattern. In Bitmask mode, outputs byte[] of 0s and 1s. In FullFrame mode, outputs byte[] of per-LED RGB triplets.")]
public class AVA_RandomPattern : Transform<object, byte[]>
{
    private Random _rng = new Random();

    [Description("Output format: Bitmask (0s and 1s) or FullFrame (per-LED RGB triplets).")]
    public AVA_RandomPatternMode Mode { get; set; }

    [Description("Total number of LEDs.")]
    public int NumLeds { get; set; }

    [Description("Probability of each LED being on (0.0 to 1.0).")]
    public double OnProbability { get; set; }

    [Description("LED color as r,g,b. Used for on-LEDs in Bitmask mode and in FullFrame mode when FullFrame Color is set to BitmaskColor.")]
    public string BitmaskColor { get; set; }

    [Description("FullFrame only: Random gives each on-LED a random color. BitmaskColor uses the Bitmask Color value.")]
    public AVA_FullFrameColorMode FullFrameColor { get; set; }

    public AVA_RandomPattern()
    {
        Mode = AVA_RandomPatternMode.Bitmask;
        NumLeds = 16;
        OnProbability = 0.3;
        BitmaskColor = "255,255,255";
        FullFrameColor = AVA_FullFrameColorMode.Random;
    }

    public override IObservable<byte[]> Process(IObservable<object> source)
    {
        return source.Select(_ =>
        {
            // Generate random on/off decisions
            bool[] onFlags = new bool[NumLeds];
            int count = 0;
            for (int i = 0; i < NumLeds; i++)
            {
                onFlags[i] = _rng.NextDouble() < OnProbability;
                if (onFlags[i]) count++;
            }

            byte[] output;
            if (Mode == AVA_RandomPatternMode.FullFrame)
            {
                output = BuildFullFrame(onFlags);
            }
            else
            {
                output = BuildBitmask(onFlags);
            }

            // Log which LEDs are on
            string ledList = "";
            for (int i = 0; i < NumLeds; i++)
            {
                if (onFlags[i])
                {
                    if (ledList.Length > 0) ledList += ",";
                    ledList += i.ToString();
                }
            }
            Console.WriteLine("RandomPattern ({0}): {1} LEDs on [{2}]",
                Mode, count, ledList);

            return output;
        });
    }

    private byte[] BuildBitmask(bool[] onFlags)
    {
        // Output: byte[] of 0s and 1s, one per LED
        byte[] pattern = new byte[NumLeds];
        for (int i = 0; i < NumLeds; i++)
        {
            pattern[i] = onFlags[i] ? (byte)1 : (byte)0;
        }
        return pattern;
    }

    private byte[] BuildFullFrame(bool[] onFlags)
    {
        // Output: byte[] of NumLeds * 3 RGB bytes (R,G,B per LED)
        int[] rgb = ParseInts(BitmaskColor);
        byte[] frame = new byte[NumLeds * 3];

        for (int i = 0; i < NumLeds; i++)
        {
            int offset = i * 3;
            if (onFlags[i])
            {
                if (FullFrameColor == AVA_FullFrameColorMode.Random)
                {
                    frame[offset] = (byte)_rng.Next(256);
                    frame[offset + 1] = (byte)_rng.Next(256);
                    frame[offset + 2] = (byte)_rng.Next(256);
                }
                else
                {
                    frame[offset] = (byte)rgb[0];
                    frame[offset + 1] = (byte)rgb[1];
                    frame[offset + 2] = (byte)rgb[2];
                }
            }
            // else bytes stay 0 (black)
        }
        return frame;
    }

    private int[] ParseInts(string csv)
    {
        string[] parts = csv.Split(',');
        int[] values = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            values[i] = int.Parse(parts[i].Trim());
        return values;
    }
}
