namespace Netcorext.Mediator;

public readonly struct None
{
    private static readonly None _value = new();
    public static ref readonly None Value => ref _value;
    public static Task<None> Task { get; } = System.Threading.Tasks.Task.FromResult(_value);
}