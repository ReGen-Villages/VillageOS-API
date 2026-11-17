namespace vos.Service.Shared;

// It lasts a day and the service runs longer, so it is replaced in place and every client reads the current one.
public sealed class LaunchToken
{
    private string _current;

    public LaunchToken(string token) => _current = token;

    public string Current => Volatile.Read(ref _current);

    internal void Replace(string token) => Volatile.Write(ref _current, token);
}
