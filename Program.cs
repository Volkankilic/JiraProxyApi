using System.Text;

var builder = WebApplication.CreateBuilder(args);

// CORS ayarları - Extension'dan çağrı yapabilmek için
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient(); // HttpClientFactory için

var app = builder.Build();

// CORS'u aktif et
app.UseCors("AllowAll");

// Swagger (development için)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

