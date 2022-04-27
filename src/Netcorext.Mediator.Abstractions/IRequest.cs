namespace Netcorext.Mediator;

public interface IRequest { }

public interface IRequest<out TResult> : IRequest { }