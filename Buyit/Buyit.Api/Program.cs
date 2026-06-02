var builder = WebApplication.CreateBuilder(args);

// ---------- 1. REGISTER SERVICES ----------
builder.Services.AddControllers();              // enables controller-based endpoints

// Built-in OpenAPI document (kept from the template; harmless to leave on)
builder.Services.AddOpenApi();

// Swashbuckle (Swagger) — this is what provides the interactive /swagger UI page.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Development-only CORS policy (permissive — DEV USE ONLY, never in production)
const string DevCors = "DevCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCors, policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// ---------- 2. CONFIGURE THE HTTP PIPELINE (order matters) ----------
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