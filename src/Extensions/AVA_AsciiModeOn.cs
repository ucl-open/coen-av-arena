// Extensions/AVA_AsciiModeOn.cs
using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

[Description("Enables ASCII LED control mode.")]
public class AVA_AsciiModeOn : Transform<object, object>
{
    public override IObservable<object> Process(IObservable<object> source)
    {
        return source.Select(input =>
        {
            if (!AVA_AsciiMode.Active)
            {
                AVA_AsciiMode.Active = true;
                Console.WriteLine("ASCII mode ON");
            }
            return input;
        });
    }
}