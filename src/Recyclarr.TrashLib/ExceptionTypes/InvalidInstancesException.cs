namespace Recyclarr.TrashLib.ExceptionTypes;

public class InvalidInstancesException : Exception
{
    public IReadOnlyCollection<string> InstanceNames { get; }

    public InvalidInstancesException(IReadOnlyCollection<string> instanceNames)
    {
        InstanceNames = instanceNames;
    }
}
