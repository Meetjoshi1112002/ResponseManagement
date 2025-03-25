using ReponseManagement.Data;
using ReponseManagement.Services;
using YourMicroservice.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            policy.AllowAnyOrigin()  // Allows requests from any origin
                  .AllowAnyMethod()  // Allows all HTTP methods (GET, POST, PUT, DELETE, etc.)
                  .AllowAnyHeader(); // Allows all headers
        });
});

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<AppDbContext>();
builder.Services.AddScoped<JWTHelper>();
builder.Services.AddSingleton<JWTLoginHelper>();

builder.Services.AddScoped<DTOConverter>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseJwtUser(); // Cookie oberserver

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
