// Extensions/AVA_AsciiModeOff.cs
using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

[Description("Disables ASCII LED control mode.")]
public class AVA_AsciiModeOff : Transform<object, object>
{
    public override IObservable<object> Process(IObservable<object> source)
    {
        return source.Select(input =>
        {
            if (AVA_AsciiMode.Active)
            {
                AVA_AsciiMode.Active = false;
                Console.WriteLine("ASCII mode OFF");
            }
            return input;
        });
    }
}