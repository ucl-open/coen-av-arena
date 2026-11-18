// AVA_TriggerZone.cs
// Detects when a tracked centroid enters a predefined trigger zone.
// Outputs true ONCE on the frame of entry (rising edge), not while dwelling.
//
// Supports circle and rectangle shapes. Coordinates are in the same units
// as the input centroid (typically pixels from camera tracking).
//
// Workflow wiring:
//   Centroid (Point2f) → AVA_TriggerZoneDetector → Condition (value == true) → trigger next trial
//
// @hamid

using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;
using OpenCV.Net;

public enum AVA_ZoneShape
{
    Circle,
    Rectangle
}

[Description("Defines a trigger zone and detects when a tracked centroid enters it. Outputs true on entry (rising edge).")]
public class AVA_TriggerZone : Transform<Point2f, bool>
{
    private AVA_ZoneShape _shape;
    private float _centerX;
    private float _centerY;
    private float _radius;
    private float _width;
    private float _height;
    private bool _requireTrialPhase;

    [Description("Shape of the trigger zone.")]
    public AVA_ZoneShape Shape
    {
        get { return _shape; }
        set { _shape = value; }
    }

    [Description("X coordinate of the zone center (pixels).")]
    public float CenterX
    {
        get { return _centerX; }
        set { _centerX = value; }
    }

    [Description("Y coordinate of the zone center (pixels).")]
    public float CenterY
    {
        get { return _centerY; }
        set { _centerY = value; }
    }

    [Description("Radius of the zone (pixels). Used when Shape is Circle.")]
    public float Radius
    {
        get { return _radius; }
        set { _radius = value; }
    }

    [Description("Width of the zone (pixels). Used when Shape is Rectangle.")]
    public float Width
    {
        get { return _width; }
        set { _width = value; }
    }

    [Description("Height of the zone (pixels). Used when Shape is Rectangle.")]
    public float Height
    {
        get { return _height; }
        set { _height = value; }
    }

    [Description("If true, only fires during ITI or WaitForTrigger phases. Set false for testing without trials.")]
    public bool RequireTrialPhase
    {
        get { return _requireTrialPhase; }
        set { _requireTrialPhase = value; }
    }

    public AVA_TriggerZone()
    {
        _shape = AVA_ZoneShape.Circle;
        _centerX = 0;
        _centerY = 0;
        _radius = 50;
        _width = 100;
        _height = 100;
        _requireTrialPhase = true;
    }

    public override IObservable<bool> Process(IObservable<Point2f> source)
    {
        return Observable.Defer(() =>
        {
            bool wasInZone = false;

            float radiusSq = _radius * _radius;
            float halfW = _width / 2f;
            float halfH = _height / 2f;
            float cx = _centerX;
            float cy = _centerY;
            AVA_ZoneShape shape = _shape;

            Console.WriteLine(String.Format(
                "TriggerZone: {0} at ({1}, {2}), {3}",
                shape,
                cx, cy,
                shape == AVA_ZoneShape.Circle
                    ? String.Format("radius={0}", _radius)
                    : String.Format("width={0}, height={1}", _width, _height)));

            return source.Select(centroid =>
            {
                // Check trial phase if required
                if (_requireTrialPhase)
                {
                    AVA_TrialPhase phase = AVA_TrialState.Phase;
                    if (phase != AVA_TrialPhase.ITI &&
                        phase != AVA_TrialPhase.WaitForTrigger)
                    {
                        wasInZone = false;
                        return false;
                    }
                }

                // Check if centroid is inside the zone
                bool isInZone = false;

                if (shape == AVA_ZoneShape.Circle)
                {
                    float dx = centroid.X - cx;
                    float dy = centroid.Y - cy;
                    isInZone = (dx * dx + dy * dy) <= radiusSq;
                }
                else
                {
                    isInZone = centroid.X >= (cx - halfW) &&
                               centroid.X <= (cx + halfW) &&
                               centroid.Y >= (cy - halfH) &&
                               centroid.Y <= (cy + halfH);
                }

                // Rising edge: true only on the frame of entry
                bool entered = isInZone && !wasInZone;
                wasInZone = isInZone;

                if (entered)
                {
                    Console.WriteLine(String.Format(
                        "TriggerZone: Centroid entered zone at ({0:F1}, {1:F1})",
                        centroid.X, centroid.Y));
                }

                return entered;
            });
        });
    }

    // Accept a string input like "320,240" for testing without a camera
    public IObservable<bool> Process(IObservable<string> source)
    {
        return Process(source.Select(s =>
        {
            string[] parts = s.Split(',');
            float x = float.Parse(parts[0].Trim());
            float y = float.Parse(parts[1].Trim());
            return new Point2f(x, y);
        }));
    }
}
