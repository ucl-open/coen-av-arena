// AVA_HUD.cs
// General-purpose heads-up display compositing node.
// Accepts 2 to 7 layers via CombineLatest and composites them in order.
// Layer 1 (Item1) is the base (e.g., camera feed).
// Layers 2+ are overlays with BLACK backgrounds and bright drawings.
//
// Blend modes:
//   - Max:         takes the brighter pixel of base or overlay (clean, no darkening)
//   - Add:         adds pixel values (can saturate to white)
//   - Blend:       weighted average across entire image (base * Weight + overlay * (1 - Weight))
//   - Replace:     overlay replaces base wherever overlay is not black (sharp cutout)
//   - MaskedBlend: blends where overlay is not black (i.e. Replace & Blend)
//
// Workflow wiring:
//   Camera Image - ConvertColor (Grey2Bgr) ─────┐
//   CreateCanvas - Circle - DrawCanvas ─────────┤
//   CreateCanvas - Rectangle - DrawCanvas ──────├→ CombineLatest → AVA_HUD → Visualizer
//   CreateCanvas - AddText - DrawCanvas ────────┘
//
// @hamid

using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;
using OpenCV.Net;

public enum AVA_BlendMode
{
    Max,
    Add,
    Blend,
    Replace,
    MaskedBlend
}

[Description("Composites 2-7 image layers into one. First input is the base; others are overlays with black backgrounds.")]
public class AVA_HUD : Transform<Tuple<IplImage, IplImage>, IplImage>
{
    private AVA_BlendMode _mode;
    private double _weight;

    [Description("How each overlay is combined with the base image.")]
    public AVA_BlendMode Mode
    {
        get { return _mode; }
        set { _mode = value; }
    }

    [Description("Base image weight for Blend mode (0.0 to 1.0). Overlay weight is 1 - this value.")]
    public double Weight
    {
        get { return _weight; }
        set { _weight = value; }
    }

    public AVA_HUD()
    {
        _mode = AVA_BlendMode.Max;
        _weight = 0.7;
    }

    // ── Core compositing: applies one overlay onto a base ──

    private IplImage Composite(IplImage baseImg, IplImage overlay)
    {
        // Handle channel mismatch
        IplImage overlayMatched = overlay;
        bool disposeMatched = false;

        if (overlay.Channels != baseImg.Channels)
        {
            overlayMatched = new IplImage(baseImg.Size, baseImg.Depth, baseImg.Channels);
            disposeMatched = true;

            if (overlay.Channels == 1 && baseImg.Channels == 3)
            {
                CV.CvtColor(overlay, overlayMatched, ColorConversion.Gray2Bgr);
            }
            else if (overlay.Channels == 3 && baseImg.Channels == 1)
            {
                CV.CvtColor(overlay, overlayMatched, ColorConversion.Bgr2Gray);
            }
        }

        // Handle size mismatch
        if (overlayMatched.Width != baseImg.Width || overlayMatched.Height != baseImg.Height)
        {
            IplImage resized = new IplImage(baseImg.Size, baseImg.Depth, baseImg.Channels);
            CV.Resize(overlayMatched, resized, SubPixelInterpolation.Linear);
            if (disposeMatched)
            {
                overlayMatched.Close();
            }
            overlayMatched = resized;
            disposeMatched = true;
        }

        IplImage output = new IplImage(baseImg.Size, baseImg.Depth, baseImg.Channels);

        switch (_mode)
        {
            case AVA_BlendMode.Max:
                CV.Max(baseImg, overlayMatched, output);
                break;

            case AVA_BlendMode.Add:
                CV.Add(baseImg, overlayMatched, output, null);
                break;

            case AVA_BlendMode.Blend:
                CV.AddWeighted(baseImg, _weight, overlayMatched, 1.0 - _weight, 0, output);
                break;

            case AVA_BlendMode.Replace:
                CV.Copy(baseImg, output);
                {
                    IplImage mask = new IplImage(baseImg.Size, IplDepth.U8, 1);
                    if (overlayMatched.Channels == 1)
                    {
                        CV.Threshold(overlayMatched, mask, 1, 255, ThresholdTypes.Binary);
                    }
                    else
                    {
                        IplImage gray = new IplImage(baseImg.Size, IplDepth.U8, 1);
                        CV.CvtColor(overlayMatched, gray, ColorConversion.Bgr2Gray);
                        CV.Threshold(gray, mask, 1, 255, ThresholdTypes.Binary);
                        gray.Close();
                    }
                    CV.Copy(overlayMatched, output, mask);
                    mask.Close();
                }
                break;

            case AVA_BlendMode.MaskedBlend:
                // Blend only where overlay is not black; rest of base passes through
                CV.Copy(baseImg, output);
                {
                    // Build mask from non-black overlay pixels
                    IplImage mask = new IplImage(baseImg.Size, IplDepth.U8, 1);
                    if (overlayMatched.Channels == 1)
                    {
                        CV.Threshold(overlayMatched, mask, 1, 255, ThresholdTypes.Binary);
                    }
                    else
                    {
                        IplImage gray = new IplImage(baseImg.Size, IplDepth.U8, 1);
                        CV.CvtColor(overlayMatched, gray, ColorConversion.Bgr2Gray);
                        CV.Threshold(gray, mask, 1, 255, ThresholdTypes.Binary);
                        gray.Close();
                    }

                    // Blend the full images
                    IplImage blended = new IplImage(baseImg.Size, baseImg.Depth, baseImg.Channels);
                    CV.AddWeighted(baseImg, _weight, overlayMatched, 1.0 - _weight, 0, blended);

                    // Copy only the blended pixels where the mask is active
                    CV.Copy(blended, output, mask);

                    blended.Close();
                    mask.Close();
                }
                break;
        }

        if (disposeMatched)
        {
            overlayMatched.Close();
        }

        return output;
    }

    // ── 2 layers ──
    public override IObservable<IplImage> Process(IObservable<Tuple<IplImage, IplImage>> source)
    {
        return source.Select(t =>
        {
            return Composite(t.Item1, t.Item2);
        });
    }

    // ── 3 layers ──
    public IObservable<IplImage> Process(IObservable<Tuple<IplImage, IplImage, IplImage>> source)
    {
        return source.Select(t =>
        {
            IplImage result = Composite(t.Item1, t.Item2);
            IplImage result2 = Composite(result, t.Item3);
            result.Close();
            return result2;
        });
    }

    // ── 4 layers ──
    public IObservable<IplImage> Process(
        IObservable<Tuple<IplImage, IplImage, IplImage, IplImage>> source)
    {
        return source.Select(t =>
        {
            IplImage r1 = Composite(t.Item1, t.Item2);
            IplImage r2 = Composite(r1, t.Item3);
            r1.Close();
            IplImage r3 = Composite(r2, t.Item4);
            r2.Close();
            return r3;
        });
    }

    // ── 5 layers ──
    public IObservable<IplImage> Process(
        IObservable<Tuple<IplImage, IplImage, IplImage, IplImage, IplImage>> source)
    {
        return source.Select(t =>
        {
            IplImage r1 = Composite(t.Item1, t.Item2);
            IplImage r2 = Composite(r1, t.Item3);
            r1.Close();
            IplImage r3 = Composite(r2, t.Item4);
            r2.Close();
            IplImage r4 = Composite(r3, t.Item5);
            r3.Close();
            return r4;
        });
    }

    // ── 6 layers ──
    public IObservable<IplImage> Process(
        IObservable<Tuple<IplImage, IplImage, IplImage, IplImage, IplImage, IplImage>> source)
    {
        return source.Select(t =>
        {
            IplImage r1 = Composite(t.Item1, t.Item2);
            IplImage r2 = Composite(r1, t.Item3);
            r1.Close();
            IplImage r3 = Composite(r2, t.Item4);
            r2.Close();
            IplImage r4 = Composite(r3, t.Item5);
            r3.Close();
            IplImage r5 = Composite(r4, t.Item6);
            r4.Close();
            return r5;
        });
    }

    // ── 7 layers ──
    public IObservable<IplImage> Process(
        IObservable<Tuple<IplImage, IplImage, IplImage, IplImage, IplImage, IplImage, IplImage>> source)
    {
        return source.Select(t =>
        {
            IplImage r1 = Composite(t.Item1, t.Item2);
            IplImage r2 = Composite(r1, t.Item3);
            r1.Close();
            IplImage r3 = Composite(r2, t.Item4);
            r2.Close();
            IplImage r4 = Composite(r3, t.Item5);
            r3.Close();
            IplImage r5 = Composite(r4, t.Item6);
            r4.Close();
            IplImage r6 = Composite(r5, t.Item7);
            r5.Close();
            return r6;
        });
    }
}
