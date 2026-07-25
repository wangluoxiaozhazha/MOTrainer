namespace MOTrainer.Core.Session;

public interface IGameSessionProbe
{
    GameSessionSnapshot Probe();
}
