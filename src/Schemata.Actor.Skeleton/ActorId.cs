namespace Schemata.Actor.Skeleton;

public readonly record struct ActorId(string Type, string Key)
{
    public override string ToString() => $"{Type}/{Key}";
}
