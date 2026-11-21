// AVA_TrialManager.cs
// Static class that broadcasts the current trial state across the Bonsai workflow.
//
// Two ways to read state:
//   1. Static fields: AVA_TrialManager.Phase, .TrialIndex, etc. (direct, any node)
//   2. Observable: drop an AVA_TrialPhaseSource node into the workflow and subscribe
//      to phase changes using standard Bonsai operators (Condition, Where, etc.)
//
// @hamid

using System;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Bonsai;

public enum AVA_TrialPhase
{
    Idle,           // No trial active, waiting for session start
    Staging,        // Pattern is being staged on Arduino (no show yet)
    WaitForTrigger, // Staged and waiting for trigger event (mouse enters zone, or keypress)
    StimulusOn,     // LEDs are on, waiting for response
    WaitForResponse,// Within the response window
    ITI,            // Inter-trial interval
    Complete        // All trials finished
}

public static class AVA_TrialManager
{
    private static AVA_TrialPhase _phase;
    private static int _trialIndex;
    private static int _totalTrials;
    private static string _currentTrialType;
    private static string _rewardTile;

    private static BehaviorSubject<AVA_TrialPhase> _phaseSubject =
        new BehaviorSubject<AVA_TrialPhase>(AVA_TrialPhase.Idle);

    // Observable stream of phase changes — AVA_TrialPhaseSource subscribes to this
    public static IObservable<AVA_TrialPhase> PhaseChanged
    {
        get { return _phaseSubject; }
    }

    public static AVA_TrialPhase Phase
    {
        get { return _phase; }
        set
        {
            _phase = value;
            _phaseSubject.OnNext(value);
            Console.WriteLine(
                String.Format("TrialState: {0} (trial {1}/{2})",
                    _phase, _trialIndex, _totalTrials));
        }
    }

    public static int TrialIndex
    {
        get { return _trialIndex; }
    }

    public static int TotalTrials
    {
        get { return _totalTrials; }
    }

    public static string CurrentTrialType
    {
        get { return _currentTrialType; }
    }

    public static string RewardTile
    {
        get { return _rewardTile; }
    }

    public static bool IsComplete
    {
        get { return _trialIndex >= _totalTrials; }
    }

    public static void Reset(int totalTrials)
    {
        _phase = AVA_TrialPhase.Idle;
        _trialIndex = 0;
        _totalTrials = totalTrials;
        _currentTrialType = "";
        _rewardTile = "";
        _phaseSubject.OnNext(AVA_TrialPhase.Idle);
    }

    public static void SetTrial(int index, string trialType, string rewardTile)
    {
        _trialIndex = index;
        _currentTrialType = trialType;
        _rewardTile = rewardTile;
    }
}


// Source node that emits the current trial phase whenever it changes.
// Drop this into the workflow and use Condition, Where, etc. downstream.
//
// Example — gate the trigger key:
//   KeyDown (T) → WithLatestFrom (AVA_TrialPhaseSource) → Condition (it != Complete)
//
// Or simply subscribe to see phase transitions in real time.

[Description("Emits the current trial phase whenever it changes. Subscribe to gate or monitor trial state.")]
public class AVA_TrialPhaseSource : Source<AVA_TrialPhase>
{
    public override IObservable<AVA_TrialPhase> Generate()
    {
        return AVA_TrialManager.PhaseChanged;
    }
}


// Source node that emits a formatted trial-info string on every phase change.
// Reads phase, trial index, total trials, and trial type from AVA_TrialManager.
// Wire into AVA_HUDtext for live HUD display.
//
// Wiring:
//   AVA_TrialInfo → AVA_HUDtext → CombineLatest → AVA_HUD
//
// Format placeholders:
//   {0}=TrialIndex  {1}=TotalTrials  {2}=Phase  {3}=TrialType  {4}=RewardTile

[Description("Emits a formatted trial-info string on every phase change. Wire into AVA_HUDtext for HUD display.")]
public class AVA_TrialInfo : Source<string>
{
    private string _format;

    public AVA_TrialInfo()
    {
        _format = "Trial {0}/{1}  Phase: {2}";
    }

    [Description("Format string. {0}=TrialIndex, {1}=TotalTrials, {2}=Phase, {3}=TrialType, {4}=RewardTile.")]
    public string Format
    {
        get { return _format; }
        set { _format = value; }
    }

    public override IObservable<string> Generate()
    {
        return AVA_TrialManager.PhaseChanged.Select(phase =>
            String.Format(_format,
                AVA_TrialManager.TrialIndex,
                AVA_TrialManager.TotalTrials,
                phase,
                AVA_TrialManager.CurrentTrialType ?? "",
                AVA_TrialManager.RewardTile ?? ""));
    }
}
