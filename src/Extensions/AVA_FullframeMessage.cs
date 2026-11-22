// Extensions/AVA_FullframeMessage.cs
using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

[Description("Wraps a per-LED RGB byte[] into a full-frame packet. Input: NumLeds * 3 bytes (R,G,B per LED). Only active in FullFrame mode.")]
public class AVA_FullframeMessage : Transform<byte[], byte[]>
{
    [Description("If true, stages the pattern without displaying. Send T to trigger.")]
    public bool Staged { get; set; }

    public AVA_FullframeMessage()
    {
        Staged = false;
    }

    public override IObservable<byte[]> Process(IObservable<byte[]> source)
    {
        return source.SelectMany(frame =>
        {
            if (AVA_Mode.Current != AVA_ModeState.FullFrame)
            {
                return Observable.Empty<byte[]>();
            }

            // frame is NumLeds * 3 bytes of per-LED RGB data
            int numLeds = frame.Length / 3;
            byte[] packet = new byte[1 + frame.Length];

            // Header: 0xFC for staged, 0xFF for immediate
            packet[0] = Staged ? (byte)0xFC : (byte)0xFF;

            // Copy the RGB data after the header
            Array.Copy(frame, 0, packet, 1, frame.Length);

            int count = 0;
            for (int i = 0; i < numLeds; i++)
            {
                int offset = i * 3;
                if (frame[offset] != 0 || frame[offset + 1] != 0 || frame[offset + 2] != 0)
                {
                    count++;
                }
            }
            Console.WriteLine("FullFrame {0}: {1} LEDs on ({2} total, {3} bytes)",
                Staged ? "staged" : "sent", count, numLeds, packet.Length);

            return Observable.Return(packet);
        });
    }
}
