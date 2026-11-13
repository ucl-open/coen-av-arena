// Extensions/AVA_AsciiModeToggle.cs
using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

[Description("Toggles ASCII control mode on/off.")]
public class AVA_AsciiModeToggle : Transform<object, object>
{
    private bool _initialized = false;

    public override IObservable<object> Process(IObservable<object> source)
    {
        return Observable.Defer(() =>
        {
            AVA_AsciiMode.Reset();
            _initialized = true;

            return source.Select(input =>
            {
                AVA_AsciiMode.Active = !AVA_AsciiMode.Active;
                Console.WriteLine("ASCII mode {0}",
                    AVA_AsciiMode.Active ? "ON" : "OFF");
                return input;
            });
        });
    }
}