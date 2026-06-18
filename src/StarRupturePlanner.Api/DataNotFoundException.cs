namespace StarRupturePlanner.Api;

public sealed class DataNotFoundException : Exception
{
    public DataNotFoundException(string id)
        : base(id)
    {
    }
}
