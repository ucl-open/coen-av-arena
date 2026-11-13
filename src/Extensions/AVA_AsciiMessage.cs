// Extensions/AVA_AsciiMessage.cs
using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

[Description("Type an LED number + submit to light it. X to turn all off. Only active in ASCII mode.")]
public class AVA_AsciiMessage : Transform<string, string>
{
    private string _buffer = "";

    [Description("LED color as r,g,b.")]
    public string LedColor { get; set; }

    public AVA_AsciiMessage()
    {
        LedColor = "255,255,255";
    }

    public override IObservable<string> Process(IObservable<string> source)
    {
        return source.SelectMany(key =>
        {
            if (AVA_Mode.Current != AVA_ModeState.ASCII)
            {
                return Observable.Empty<string>();
            }

            if (key == "X" || key == "x")
            {
                _buffer = "";
                Console.WriteLine("All LEDs off");
                return Observable.Return("X");
            }

            if (key == "Return" || key == "Space")
            {
                if (_buffer.Length > 0)
                {
                    int id;
                    if (int.TryParse(_buffer, out id))
                    {
                        int[] rgb = ParseInts(LedColor);
                        string cmd = string.Format("L,{0},{1},{2},{3}",
                            id, rgb[0], rgb[1], rgb[2]);
                        Console.WriteLine(cmd);
                        _buffer = "";
                        return Observable.Return(cmd);
                    }

                    Console.WriteLine("Invalid LED number: '{0}'", _buffer);
                    _buffer = "";
                }
                return Observable.Empty<string>();
            }

            if (key.Length == 1 && char.IsDigit(key[0]))
            {
                _buffer += key;
                Console.WriteLine("Typing: {0}", _buffer);
                return Observable.Empty<string>();
            }

            if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1]))
            {
                _buffer += key[1];
                Console.WriteLine("Typing: {0}", _buffer);
                return Observable.Empty<string>();
            }

            if (key.StartsWith("NumPad") && key.Length == 7
                && char.IsDigit(key[6]))
            {
                _buffer += key[6];
                Console.WriteLine("Typing: {0}", _buffer);
                return Observable.Empty<string>();
            }

            if (key == "Back" && _buffer.Length > 0)
            {
                _buffer = _buffer.Substring(0, _buffer.Length - 1);
                Console.WriteLine("Typing: {0}", _buffer);
                return Observable.Empty<string>();
            }

            return Observable.Empty<string>();
        });
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