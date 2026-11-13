// Extensions/AVA_Mode.cs
using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

public enum AVA_ModeState
{
    Off,
    ASCII,
    Bitmask
}

public static class AVA_Mode
{
    public static AVA_ModeState Current = AVA_ModeState.Off;

    public static void Reset()
    {
        Current = AVA_ModeState.Off;
    }
}

[Description("Toggles a specific mode on/off. If another mode is active, switches to this one.")]
public class AVA_ModeToggle : Transform<object, object>
{
    [Description("Which mode this toggle controls: ASCII or Bitmask.")]
    public AVA_ModeState Mode { get; set; }

    public AVA_ModeToggle()
    {
        Mode = AVA_ModeState.ASCII;
    }

    public override IObservable<object> Process(IObservable<object> source)
    {
        return Observable.Defer(() =>
        {
            AVA_Mode.Reset();

            return source.Select(input =>
            {
                if (AVA_Mode.Current == Mode)
                {
                    AVA_Mode.Current = AVA_ModeState.Off;
                    Console.WriteLine("Mode: OFF");
                }
                else
                {
                    AVA_Mode.Current = Mode;
                    Console.WriteLine("Mode: {0}", Mode);
                }
                return input;
            });
        });
    }
}