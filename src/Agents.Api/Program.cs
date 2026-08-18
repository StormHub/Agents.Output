using Agents.Api.Options;
using Agents.Api.Tools;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
   builder.Configuration.AddUserSecrets<Program>();
}

builder.Services.AddTools();
builder.Services.AddAgent(builder.Configuration);

var app = builder.Build();
app.ConfigureAgent();

await app.RunAsync();