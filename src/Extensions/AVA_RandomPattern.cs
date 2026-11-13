// Extensions/AVA_RandomPattern.cs
using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

[Description("Generates a random LED pattern of 0s and 1s on each trigger.")]
public class AVA_RandomPattern : Transform<object, byte[]>
{
    private Random _rng = new Random();

    [Description("Total number of LEDs.")]
    public int NumLeds { get; set; }

    [Description("Probability of each LED being on (0.0 to 1.0).")]
    public double OnProbability { get; set; }

    public AVA_RandomPattern()
    {
        NumLeds = 16;
        OnProbability = 0.3;
    }

    public override IObservable<byte[]> Process(IObservable<object> source)
    {
        return source.Select(_ =>
        {
            byte[] pattern = new byte[NumLeds];
            for (int i = 0; i < NumLeds; i++)
            {
                pattern[i] = _rng.NextDouble() < OnProbability
                    ? (byte)1 : (byte)0;
            }

            // Log which LEDs are on
            string ledList = "";
            for (int i = 0; i < NumLeds; i++)
            {
                if (pattern[i] == 1)
                {
                    if (ledList.Length > 0) ledList += ",";
                    ledList += i.ToString();
                }
            }
            Console.WriteLine("Pattern: [{0}]", ledList);

            return pattern;
        });
    }
}