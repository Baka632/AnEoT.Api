using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi()
    .AddHttpClient()
    .AddMemoryCache()
    .AddControllers();

builder.WebHost.ConfigureKestrel(options =>
{
    // System.ServiceModel.Syndication.SyndicationFeed 还不支持异步读写
    options.AllowSynchronousIO = true;
});

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();