namespace HonorControl.Models;

public sealed class PowerSchemeInfo
{
    public PowerSchemeInfo(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; }
    public string Name { get; }
}
