using FluentValidation;

namespace Netcorext.Mediator.Pipelines;

public class ValidatorPipeline : IPipeline
{
    private readonly IServiceProvider _serviceProvider;

    public ValidatorPipeline(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<TResult> InvokeAsync<TResult>(IRequest<TResult> request, PipelineDelegate<TResult> next, CancellationToken cancellationToken = default)
    {
        var genericType = typeof(IValidator<>).MakeGenericType(request.GetType());

        if (_serviceProvider.GetService(genericType) is not IValidator validator)
            return await next(request, cancellationToken);

        var validationContext = new ValidationContext<object>(request);

        var result = await validator.ValidateAsync(validationContext, cancellationToken);

        if (!result.IsValid) throw new ValidationException(result.Errors);

        return await next(request, cancellationToken);
    }
}