using Microsoft.Extensions.Options;

namespace UpApi.Configuration;

public sealed class ServiceConfigResolver(IOptions<ServiceConfigurations> options) : IServiceConfigResolver
{
    private readonly ServiceConfigurations _services = options.Value;

    public bool TryGet(string serviceName, out ServiceConfiguration configuration)
        {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            configuration = default!;
            return false;
        }

        return _services.Services.TryGetValue(serviceName, out configuration!);
    }
}
