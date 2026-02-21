namespace OutOfFuel.Agent.src.Sim;

public interface ISimDataSource : IDisposable
{
    SimDataSnapshot Poll();
}
