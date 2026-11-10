namespace Advertisement;

internal enum HtmlDecision { Paused, Print, Expired }

internal static class HtmlPlayback
{
    // Count eligible ticks rather than wall time: alive ticks must not consume dead-only ads.
    internal static HtmlDecision Advance(ref long elapsed, double durationTicks, bool deadOnly, bool alive)
    {
        if (deadOnly && alive) return HtmlDecision.Paused;
        return elapsed++ >= durationTicks ? HtmlDecision.Expired : HtmlDecision.Print;
    }
}
