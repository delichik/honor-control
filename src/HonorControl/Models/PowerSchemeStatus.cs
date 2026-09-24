namespace HonorControl.Models;

public sealed class PowerSchemeStatus
{
    public PowerSchemeStatus(PowerSchemeInfo active, PowerSchemeInfo? balanced, PowerSchemeInfo? honorPerformance)
    {
        Active = active;
        Balanced = balanced;
        HonorPerformance = honorPerformance;
    }

    public PowerSchemeInfo Active { get; }
    public PowerSchemeInfo? Balanced { get; }
    public PowerSchemeInfo? HonorPerformance { get; }

    public PowerSchemeInfo? GetTarget(int mode) => mode switch
    {
        1 => Balanced,
        2 => HonorPerformance,
        _ => null
    };
}
