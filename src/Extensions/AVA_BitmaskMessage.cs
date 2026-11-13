// Extensions/AVA_BitmaskMessage.cs
using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

[Description("Converts a byte[] of 0s and 1s into a bitmask packet. Only active in Bitmask mode.")]
public class AVA_BitmaskMessage : Transform<byte[], byte[]>
{
    [Description("LED color as r,g,b.")]
    public string LedColor { get; set; }

    [Description("If true, stages the pattern without displaying. Send T to trigger.")]
    public bool Staged { get; set; }

    public AVA_BitmaskMessage()
    {
        LedColor = "255,255,255";
        Staged = false;
    }

    public override IObservable<byte[]> Process(IObservable<byte[]> source)
    {
        return source.SelectMany(pattern =>
        {
            if (AVA_Mode.Current != AVA_ModeState.Bitmask)
            {
                return Observable.Empty<byte[]>();
            }

            int[] rgb = ParseInts(LedColor);
            byte[] packet = BuildPacket(pattern,
                (byte)rgb[0], (byte)rgb[1], (byte)rgb[2], Staged);

            int count = 0;
            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] == 1) count++;
            }
            Console.WriteLine("Bitmask {0}: {1} LEDs on",
                Staged ? "staged" : "sent", count);

            return Observable.Return(packet);
        });
    }

    private static byte[] BuildPacket(byte[] pattern, byte r, byte g, byte b, bool staged)
    {
        int numLeds = pattern.Length;
        int maskBytes = (numLeds + 7) / 8;
        byte[] packet = new byte[1 + maskBytes + 3];

        // Header: 0xFD for staged, 0xFE for immediate
        packet[0] = staged ? (byte)0xFD : (byte)0xFE;

        for (int i = 0; i < numLeds; i++)
        {
            if (pattern[i] == 1)
            {
                packet[1 + i / 8] |= (byte)(1 << (i % 8));
            }
        }

        packet[1 + maskBytes] = r;
        packet[1 + maskBytes + 1] = g;
        packet[1 + maskBytes + 2] = b;

        return packet;
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