using Buyit.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Register Services
builder.Services.AddControllers();              // enables controller-based endpoints

builder.Services.AddOpenApi();

// swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS Policy
const string DevCors = "DevCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCors, policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

// HTTP pipeline 
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();       // serves the built-in OpenAPI JSON at /openapi/v1.json
    app.UseSwagger();       // serves Swashbuckle's OpenAPI JSON
    app.UseSwaggerUI();     // serves the interactive UI page at /swagger
    app.UseCors(DevCors);   // apply the permissive CORS policy only in development
}

app.UseHttpsRedirection();  // redirect HTTP requests to HTTPS
app.UseAuthorization();     // placeholder for when auth is added later
app.MapControllers();       // route requests to your controllers
app.Run();                  // start listening for requests


