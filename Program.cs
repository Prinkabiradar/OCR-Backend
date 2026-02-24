using Microsoft.IdentityModel.Tokens;
using OCR_BACKEND.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<SqlDBHelper>();
builder.Services.AddSingleton<JwtHelper>();


builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<UserDBHelper>();

builder.Services.AddScoped<IUserAddService, UserAddService>();
builder.Services.AddScoped<UserAddDBHelper>();

builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<MenuDBHelper>();

builder.Services.AddScoped<IDocumentTypeService, DocumentTypeService>();
builder.Services.AddScoped<DocumentTypeDBHelper>();

builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<DocumentDBHelper>();

builder.Services.AddScoped<IDocumentPageService, DocumentPageService>();
builder.Services.AddScoped<DocumentPageDBHelper>();




builder.Services.AddHttpClient<GeminiService>();


builder.Services.AddAuthentication("Bearer")
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])
        )
    };
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular",
        policy =>
        {
            policy.WithOrigins("http://localhost:4200")
                  .AllowAnyHeader() 
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});

var app = builder.Build();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAngular");
app.UseAuthentication();
 
app.UseAuthorization();

app.MapControllers();

app.Run();
