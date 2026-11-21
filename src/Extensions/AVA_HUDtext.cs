// AVA_HUDtext.cs
// Renders dynamic text onto a black canvas for use as an AVA_HUD overlay layer.
// Accepts a string or AVA_TrialPhase observable and produces an IplImage each
// time the input changes.  CombineLatest in the HUD re-composites every camera
// frame using the most recent text canvas, so the display stays in sync.
//
// Wiring example:
//   AVA_TrialStateSource → AVA_HUDtext ──┐
//   Camera (BGR) ───────────────────────────├→ CombineLatest → AVA_HUD
//   Other overlays ─────────────────────────┘
//
// @hamid

using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;
using OpenCV.Net;

[Description("Renders a dynamic string onto a black canvas for HUD overlay. Accepts a string or AVA_TrialPhase input.")]
public class AVA_HUDtext : Transform<string, IplImage>
{
    private int _width;
    private int _height;
    private int _originX;
    private int _originY;
    private double _fontScale;
    private int _fontThickness;
    private int _colorR;
    private int _colorG;
    private int _colorB;
    private string _prefix;
    private FontFace _fontFace;

    public AVA_HUDtext()
    {
        _width = 1440;
        _height = 1080;
        _originX = 10;
        _originY = 30;
        _fontScale = 1.0;
        _fontThickness = 2;
        _colorR = 255;
        _colorG = 255;
        _colorB = 255;
        _prefix = "";
        _fontFace = FontFace.HersheySimplex;
    }

    [Description("Canvas width in pixels. Should match camera width.")]
    public int Width
    {
        get { return _width; }
        set { _width = value; }
    }

    [Description("Canvas height in pixels. Should match camera height.")]
    public int Height
    {
        get { return _height; }
        set { _height = value; }
    }

    [Description("X position of text origin (left edge).")]
    public int OriginX
    {
        get { return _originX; }
        set { _originX = value; }
    }

    [Description("Y position of text baseline. OpenCV draws text upward from this point.")]
    public int OriginY
    {
        get { return _originY; }
        set { _originY = value; }
    }

    [Description("Font scale multiplier (1.0 = default size).")]
    public double FontScale
    {
        get { return _fontScale; }
        set { _fontScale = value; }
    }

    [Description("Font stroke thickness in pixels.")]
    public int FontThickness
    {
        get { return _fontThickness; }
        set { _fontThickness = value; }
    }

    [Description("Text color - Red (0-255).")]
    public int ColorR
    {
        get { return _colorR; }
        set { _colorR = value; }
    }

    [Description("Text color - Green (0-255).")]
    public int ColorG
    {
        get { return _colorG; }
        set { _colorG = value; }
    }

    [Description("Text color - Blue (0-255).")]
    public int ColorB
    {
        get { return _colorB; }
        set { _colorB = value; }
    }

    [Description("Static text prepended to the input (e.g. 'Phase: ').")]
    public string Prefix
    {
        get { return _prefix; }
        set { _prefix = value; }
    }

    [Description("OpenCV font face.")]
    public FontFace FontFace
    {
        get { return _fontFace; }
        set { _fontFace = value; }
    }

    // ── Core rendering ──

    private IplImage RenderText(string text)
    {
        IplImage canvas = new IplImage(new Size(_width, _height), IplDepth.U8, 3);
        CV.Xor(canvas, canvas, canvas);

        string displayText = text ?? "";
        if (!String.IsNullOrEmpty(_prefix))
        {
            displayText = _prefix + displayText;
        }

        Scalar color = new Scalar(_colorB, _colorG, _colorR, 255);
        Font font = new Font(_fontFace, _fontScale, _fontScale, 0, _fontThickness, LineFlags.Connected8);
        CV.PutText(canvas, displayText, new Point(_originX, _originY), font, color);

        return canvas;
    }

    // ── String input ──
    public override IObservable<IplImage> Process(IObservable<string> source)
    {
        return source.Select(text => RenderText(text));
    }

    // ── AVA_TrialPhase input (direct wiring from AVA_TrialStateSource) ──
    public IObservable<IplImage> Process(IObservable<AVA_TrialPhase> source)
    {
        return source.Select(phase => RenderText(phase.ToString()));
    }
}
