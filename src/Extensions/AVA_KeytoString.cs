// Extensions/AVA_KeyToString.cs
using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

[Description("Converts a keyboard event to a key name string.")]
public class AVA_KeyToString : Transform<object, string>
{
    public override IObservable<string> Process(IObservable<object> source)
    {
        return source.Select(input =>
        {
            string keyName = input.ToString();

            if (keyName.Length == 2 && keyName[0] == 'D'
                && char.IsDigit(keyName[1]))
            {
                return keyName[1].ToString();
            }

            if (keyName.StartsWith("NumPad")
                && keyName.Length == 7
                && char.IsDigit(keyName[6]))
            {
                return keyName[6].ToString();
            }

            return keyName;
        });
    }
}