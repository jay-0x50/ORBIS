using System;
using System.Collections.Generic;
using Orbis.M0;

// Unity 설치 없이 순수 콤보 FSM을 검증하는 보조 실행 파일 소스.
internal static class ComboSequenceHarness
{
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        var combo = new ComboSequence();
        var starts = new List<int>();
        var active = new List<int>();
        combo.StepStarted += starts.Add;
        combo.ActiveWindowVisited += active.Add;
        Check(!combo.RequestAttack(false), "Air attacks must not start.");
        Check(combo.RequestAttack(true), "Grounded input must start first strike.");
        combo.Tick(0.1f);
        Check(!combo.RequestAttack(true), "Early input must be ignored.");
        Check(active.Count == 0, "Windup must not hit.");
        combo.Tick(0.25f);
        Check(active.Count == 1 && active[0] == 1, "Long frames must not skip active window.");
        Check(combo.RequestAttack(true), "Late input must buffer.");
        for (int i = 0; i < 10; i++)
            Check(!combo.RequestAttack(true), "Spam cannot queue multiple strikes.");
        combo.Tick(0.5f);
        Check(combo.CurrentStep == 2, "Buffered input starts second strike.");
        Check(combo.RequestAttack(true), "Second strike can buffer third.");
        combo.Tick(0.65f);
        Check(combo.CurrentStep == 3, "Third strike starts.");
        Check(!combo.RequestAttack(true), "Third strike cannot queue a fourth.");
        combo.Tick(2f);
        Check(!combo.IsAttacking && combo.NormalizedTime == 0f, "Sequence returns to idle.");
        Check(string.Join(",", starts) == "1,2,3", "Exactly three strikes start in order.");
        Check(string.Join(",", active) == "1,2,3", "Each started strike reaches active window.");
        Check(combo.RequestAttack(true), "No cooldown is added after the combo.");
        combo.Tick(0.35f);
        combo.RequestAttack(true);
        combo.CancelAttack();
        combo.Tick(2f);
        Check(!combo.IsAttacking && !combo.HasBufferedAttack, "Cancel must clear pending input.");
        combo.RequestAttack(true);
        Check(combo.CurrentStep == 1, "Cancellation resets the next attack to step one.");
        combo.Tick(0f);
        Check(combo.NormalizedTime == 0f, "Zero delta cannot advance time.");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            bool rejected = false;
            try { combo.Tick(invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Invalid delta must be rejected.");
        }
        combo.CancelAttack();
        starts.Clear();
        combo.RequestAttack(true);
        combo.Tick(0.35f);
        combo.RequestAttack(true);
        combo.Tick(5f);
        Check(string.Join(",", starts) == "1,2", "Buffered input cannot also prequeue third strike.");
        Console.WriteLine("PASS: " + checks + " combat FSM checks.");
    }
}
