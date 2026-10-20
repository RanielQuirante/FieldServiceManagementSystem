using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations.Design;

namespace FieldService.Api.Persistence.DesignTime;

public sealed class DesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IMigrationsCodeGenerator, CommentFreeMigrationsGenerator>();
    }
}
