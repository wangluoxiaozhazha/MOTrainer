using MOTrainer.Core.Session;

namespace MOTrainer.Core.SmokeTests;

internal sealed class FakeSessionProbe : IGameSessionProbe
{
    private readonly GameSessionSnapshot snapshot;

    public FakeSessionProbe(GameSessionSnapshot snapshot)
    {
        this.snapshot = snapshot;
    }

    public int ProbeCount { get; private set; }

    public GameSessionSnapshot Probe()
    {
        ProbeCount++;
        return snapshot;
    }
}
