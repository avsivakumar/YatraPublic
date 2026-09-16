namespace Yatra.Contracts.Routing;

public sealed record ReturnPath
{
    public ReturnPath(string qualifiedPath)
    {
        if (string.IsNullOrWhiteSpace(qualifiedPath))
        {
            throw new ArgumentException("Return path qualified path is required.", nameof(qualifiedPath));
        }

        QualifiedPath = qualifiedPath;
    }

    public string QualifiedPath { get; }
}
